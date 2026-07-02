#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The transform gizmo (Wave 4b). In <see cref="RunMode.Edit"/> it draws move / rotate / scale
/// handles plus a selection outline around the entity tagged <c>SelectedComponent</c>, and turns a
/// drag on the active handle into a transform edit that is <b>one undo step per drag</b>.
///
/// <para><b>Overlay entities are standalone.</b> The handles + outline are mesh entities the gizmo
/// creates and owns (tagged <see cref="GizmoOverlayComponent"/>); they are <b>never</b>
/// <c>ChildOf</c>-parented to the selected game entity (<c>HierarchySystem.DisposeOrphans</c> runs
/// in Edit and would cascade-dispose them) and they set <c>VisibleComponent</c> themselves
/// (<c>CullingSystem</c> only visits <c>SpriteInfoComponent</c> entities). They draw world-space on
/// <c>Main</c> so the handles track the entity; handle sizes are scaled by <c>1/Camera.Zoom</c> for
/// constant on-screen size.</para>
///
/// <para><b>Drag → one undo step.</b> On the press edge over a handle the gizmo snapshots the
/// before-transform and calls <c>EditorHistory.BeginTransaction()</c>; each drag frame it computes
/// the target transform from the <b>total</b> cursor delta (start → current, applying grid-snap when
/// enabled) and pushes a <see cref="TransformEditCommand"/> via <c>FromCurrent</c> (which applies
/// live); on release it calls <c>CommitTransaction()</c> — collapsing the whole drag into a single
/// <c>CompositeCommand</c> entry, so one drag is exactly one undo step. The transform math is the
/// pure, unit-tested <see cref="GizmoTransform"/>.</para>
///
/// <para><b>Edit-guarded, registered RunNormally.</b> Inert in Play (it tears its overlays down and
/// returns early), active in Edit. It must run in the Update phase, before <c>HierarchySystem</c>,
/// so an edit propagates to world space the same frame.</para>
///
/// <para><b>Target-aware space (Wave 8a).</b> A selected entity whose render target is
/// <c>UI</c>/<c>HUD</c>/<c>Scroll</c> lives in <b>virtual</b> (screen-space) coordinates, not world
/// space: for those the gizmo reads the cursor's <c>VirtualPosition</c>, draws its overlays on the
/// entity's own target (so outline + handles composite exactly over it), and sizes handles with no
/// zoom compensation (screen-space passes have no camera). The transform math
/// (<see cref="GizmoTransform"/>) is space-agnostic, so move / rotate / scale all work — the only
/// difference is which coordinate pair feeds it. <c>Main</c>-target entities keep the world-space
/// path (cursor <c>WorldPosition</c>, overlays on Main, <c>1/Camera.Zoom</c> handle sizing).</para>
/// </summary>
public sealed class GizmoSystem : ISystem<GameState>
{
    /// <summary>On-screen handle/outline sizing constants (in screen pixels; divided by zoom for world units).</summary>
    private const float MoveHandlePixelRadius = 9f;
    private const float ScaleHandlePixelRadius = 7f;
    private const float ScaleHandlePixelDistance = 48f;
    private const float RotateRingPixelRadius = 40f;
    private const float RotateRingPixelTolerance = 7f;
    private const float OutlinePixelThickness = 2f;
    private const float OverlayLayerDepth = 0.999f; // draw on top of game sprites on Main

    private readonly World _world;
    private readonly Camera _camera;
    private readonly EditorHistory _history;
    private readonly EntitySet _selectedSet;
    private readonly EntitySet _gizmoStateSet;
    private readonly EntitySet _cursorSet;

    // Owned overlay entities (created lazily, reused, disposed on teardown).
    private Entity _outline;
    private Entity _handle;       // the move/scale handle, or the rotate ring
    private bool _overlaysCreated;

    // Live drag state (private hot-path frame state, not data other systems read — ECS purity).
    private bool _dragging;
    private GizmoTool _dragTool;
    private Entity _dragTarget;
    private Vector2 _dragStartCursorWorld;
    private Vector2 _dragStartPivot; // the world pivot at drag-start; stable rotate/scale centre
    private Vector2 _beforePosition, _beforeScale, _beforeOrigin;
    private float _beforeRotation;

    public bool IsEnabled { get; set; } = true;

    public GizmoSystem(World world, Camera camera, EditorHistory history)
    {
        _world = world;
        _camera = camera;
        _history = history;
        _selectedSet = world.GetEntities().With<SelectedComponent>().AsSet();
        _gizmoStateSet = world.GetEntities().With<GizmoStateComponent>().AsSet();
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        // Edit-guarded: inert in Play. Tear the overlays down so they don't linger when editing exits.
        if (state.RunMode != RunMode.Edit)
        {
            if (_dragging) CancelDrag();
            HideOverlays();
            return;
        }

        if (!TryGetSelected(out var target))
        {
            // Nothing selected — finish any in-flight drag and hide the overlays.
            if (_dragging) CancelDrag();
            HideOverlays();
            return;
        }

        ref readonly var gizmo = ref GetGizmoState();
        ref readonly var transform = ref target.Get<TransformComponent>();
        var pivot = transform.WorldPosition;

        // Target-aware space: Main-target entities are world-space (cursor WorldPosition, overlays
        // on Main, handles sized by 1/Zoom); UI/HUD/Scroll-target entities are screen-space (their
        // transforms are virtual coordinates → cursor VirtualPosition, overlays on their own
        // target, no zoom compensation — screen-space passes have no camera).
        var space = OverlaySpace(target);
        var worldSpace = space == RenderTargetID.Main;
        var invZoom = worldSpace && _camera.Zoom > 0f ? 1f / _camera.Zoom : 1f;

        if (!TryGetCursor(out var cursor))
        {
            EnsureOverlays();
            UpdateOverlayMeshes(target, gizmo.Tool, pivot, invZoom, space);
            return;
        }

        var cursorPoint = worldSpace ? cursor.WorldPosition : cursor.VirtualPosition;
        ProcessDrag(target, gizmo, cursor, cursorPoint, pivot, invZoom);

        EnsureOverlays();
        UpdateOverlayMeshes(target, gizmo.Tool, pivot, invZoom, space);
    }

    private void ProcessDrag(Entity target, in GizmoStateComponent gizmo, in CursorInputComponent cursor,
        Vector2 cursorPoint, Vector2 pivot, float invZoom)
    {
        if (_dragging)
        {
            // Apply the live edit from the drag-start cursor → current cursor so snapping is stable
            // and floating error does not accumulate frame-to-frame (the target is recomputed from
            // the immutable drag-start state each frame, not stacked on the previous frame's result).
            ApplyDragEdit(target, cursorPoint);

            if (cursor.LeftButtonReleased || !cursor.LeftButton)
                EndDrag();
            return;
        }

        // Not dragging: a press over the active handle starts a drag. A press over the editor
        // chrome / letterbox margins never starts one — the cursor's world AND virtual positions
        // are frozen at their last inside-the-viewport values there (a toolbar click must not
        // grab the gizmo).
        if (!cursor.LeftButtonPressed || cursor.OutsideViewport) return;
        if (!HandleHit(gizmo.Tool, pivot, cursorPoint, invZoom)) return;

        BeginDrag(target, gizmo.Tool, cursorPoint, pivot);
    }

    /// <summary>
    /// The coordinate space (as a render target) the selected entity — and therefore the gizmo
    /// overlay — lives in: the sprite's target when it has one, else the draw target, else Main.
    /// The editor's own chrome target never hosts a selection; map it to Main defensively.
    /// </summary>
    private static RenderTargetID OverlaySpace(Entity target)
    {
        var space = RenderTargetID.Main;
        if (target.Has<SpriteInfoComponent>()) space = target.Get<SpriteInfoComponent>().Target;
        else if (target.Has<DrawComponent>()) space = target.Get<DrawComponent>().Target;
        return space == RenderTargetID.Editor ? RenderTargetID.Main : space;
    }

    private void BeginDrag(Entity target, GizmoTool tool, Vector2 cursorWorld, Vector2 pivot)
    {
        ref readonly var t = ref target.Get<TransformComponent>();
        _dragging = true;
        _dragTool = tool;
        _dragTarget = target;
        _dragStartCursorWorld = cursorWorld;
        _dragStartPivot = pivot;
        _beforePosition = t.Position;
        _beforeRotation = t.Rotation;
        _beforeScale = t.Scale;
        _beforeOrigin = t.Origin;
        _history.BeginTransaction();
    }

    private void ApplyDragEdit(Entity target, Vector2 currentCursorWorld)
    {
        if (!target.IsAlive || !target.Has<TransformComponent>()) return;

        // Use the drag-START pivot as the rotate/scale centre so it stays fixed through the drag
        // (the live WorldPosition would drift as the entity rotates/scales about a non-zero origin).
        var (afterPos, afterRot, afterScale, afterOrigin) = GizmoTransform.Compute(
            _dragTool,
            _beforePosition, _beforeRotation, _beforeScale, _beforeOrigin,
            _dragStartPivot, _dragStartCursorWorld, currentCursorWorld,
            SnapStep(), RotationSnapStep());

        // One command per frame; the coalescing transaction collapses them into one undo step on
        // commit. FromCurrent reads the live (last-frame) transform as the "before", so the
        // composite's revert chain walks all the way back to the pre-drag state in one undo.
        _history.Push(TransformEditCommand.FromCurrent(target, afterPos, afterRot, afterScale, afterOrigin));
    }

    private void EndDrag()
    {
        _dragging = false;
        _dragTarget = default;
        // Commit the accumulated transaction → exactly one history entry for the whole drag. An
        // empty transaction (no movement at all) commits nothing.
        _history.CommitTransaction();
    }

    private void CancelDrag()
    {
        _dragging = false;
        _dragTarget = default;
        if (_history.InTransaction) _history.CancelTransaction();
    }

    private float SnapStep()
    {
        ref readonly var gizmo = ref GetGizmoState();
        return gizmo.SnapEnabled && gizmo.GridStep > 0f ? gizmo.GridStep : 0f;
    }

    private float RotationSnapStep()
    {
        ref readonly var gizmo = ref GetGizmoState();
        return gizmo.SnapEnabled && gizmo.RotationStepRadians > 0f ? gizmo.RotationStepRadians : 0f;
    }

    // ---- Hit-testing (analytic, world-space) ----

    private static bool HandleHit(GizmoTool tool, Vector2 pivot, Vector2 cursorWorld, float invZoom)
    {
        var d = Vector2.Distance(cursorWorld, pivot);
        return tool switch
        {
            GizmoTool.Move => d <= MoveHandlePixelRadius * invZoom,
            GizmoTool.Rotate => MathF.Abs(d - RotateRingPixelRadius * invZoom) <= RotateRingPixelTolerance * invZoom,
            GizmoTool.Scale => Vector2.Distance(cursorWorld, ScaleHandlePosition(pivot, invZoom))
                               <= ScaleHandlePixelRadius * invZoom,
            _ => false,
        };
    }

    private static Vector2 ScaleHandlePosition(Vector2 pivot, float invZoom)
        => pivot + new Vector2(ScaleHandlePixelDistance, -ScaleHandlePixelDistance) * invZoom;

    // ---- Overlay entity lifecycle ----

    private void EnsureOverlays()
    {
        if (_overlaysCreated && _outline.IsAlive && _handle.IsAlive) return;

        if (!(_overlaysCreated && _outline.IsAlive)) _outline = CreateOverlayEntity();
        if (!(_overlaysCreated && _handle.IsAlive)) _handle = CreateOverlayEntity();
        _overlaysCreated = true;
    }

    private Entity CreateOverlayEntity()
    {
        var e = _world.CreateEntity();
        e.Set(new GizmoOverlayComponent());
        e.Set(new TransformComponent()); // identity — vertices are baked in world space
        e.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Main,
            LayerDepth = OverlayLayerDepth,
            WorldMatrix = Matrix.Identity,
        });
        e.Set(new VisibleComponent()); // CullingSystem won't tag this (no SpriteInfoComponent)
        return e;
    }

    private void HideOverlays()
    {
        if (!_overlaysCreated) return;
        if (_outline.IsAlive) _outline.Dispose();
        if (_handle.IsAlive) _handle.Dispose();
        _overlaysCreated = false;
    }

    private void UpdateOverlayMeshes(Entity target, GizmoTool tool, Vector2 pivot, float invZoom,
        RenderTargetID space)
    {
        // Selection outline: the sprite's rendered quad (world- or virtual-space, matching the
        // entity), stroked. Falls back to a small box around the pivot when the entity has no
        // sprite bounds (e.g. a mesh-only entity). Overlays draw on the entity's own target so
        // they composite exactly over it (a virtual-space outline on Main would land wherever the
        // camera happens to look).
        SetMesh(_outline, BuildOutline(target, pivot, invZoom), space);
        // Active-tool handle.
        SetMesh(_handle, BuildHandle(tool, pivot, invZoom), space);
    }

    private static void SetMesh(Entity e, MeshData mesh, RenderTargetID space)
    {
        ref var dc = ref e.Get<DrawComponent>();
        dc.Type = DrawElementType.Mesh;
        dc.Vertices = mesh.Vertices;
        dc.Indices = mesh.Indices;
        dc.PrimitiveType = mesh.PrimitiveType;
        dc.WorldMatrix = Matrix.Identity;
        dc.LayerDepth = OverlayLayerDepth;
        dc.Target = space;
    }

    private static MeshData BuildOutline(Entity target, Vector2 pivot, float invZoom)
    {
        var thickness = OutlinePixelThickness * invZoom;
        var color = Color.Yellow;

        if (target.Has<SpriteInfoComponent>())
        {
            var corners = GizmoTransform.SpriteWorldQuad(
                target.Get<TransformComponent>(), target.Get<SpriteInfoComponent>());
            return new PolygonOutlineMeshGenerator(corners, thickness, color, closed: true).Generate();
        }

        // No sprite bounds: a small box around the pivot.
        var half = 16f * invZoom;
        var box = new[]
        {
            pivot + new Vector2(-half, -half), pivot + new Vector2(half, -half),
            pivot + new Vector2(half, half),   pivot + new Vector2(-half, half),
        };
        return new PolygonOutlineMeshGenerator(box, thickness, color, closed: true).Generate();
    }

    private static MeshData BuildHandle(GizmoTool tool, Vector2 pivot, float invZoom)
    {
        switch (tool)
        {
            case GizmoTool.Move:
            {
                var r = MoveHandlePixelRadius * invZoom;
                var arm = MoveHandlePixelRadius * 2.4f * invZoom;
                var th = OutlinePixelThickness * invZoom;
                return new CompositeMeshGenerator()
                    .Add(new LineMeshGenerator(pivot, pivot + new Vector2(arm, 0f), th, Color.OrangeRed))
                    .Add(new LineMeshGenerator(pivot, pivot + new Vector2(0f, -arm), th, Color.LimeGreen))
                    .Add(new CircleMeshGenerator(pivot, r, Color.White, 18))
                    .Generate();
            }
            case GizmoTool.Rotate:
            {
                var ring = RotateRingPixelRadius * invZoom;
                var th = RotateRingPixelTolerance * 0.6f * invZoom;
                return new CircleOutlineMeshGenerator(pivot, ring, th, Color.DeepSkyBlue, 28).Generate();
            }
            case GizmoTool.Scale:
            {
                var handlePos = ScaleHandlePosition(pivot, invZoom);
                var r = ScaleHandlePixelRadius * invZoom;
                var th = OutlinePixelThickness * invZoom;
                var box = new Rectangle(
                    (int)(handlePos.X - r), (int)(handlePos.Y - r), (int)(r * 2f), (int)(r * 2f));
                return new CompositeMeshGenerator()
                    .Add(new LineMeshGenerator(pivot, handlePos, th, Color.Gold))
                    .Add(new FilledRectangleMeshGenerator(box, Color.Gold))
                    .Generate();
            }
            default:
                return new MeshData();
        }
    }

    // ---- Lookups ----

    private bool TryGetSelected(out Entity target)
    {
        foreach (var e in _selectedSet.GetEntities())
        {
            if (e.IsAlive && e.Has<TransformComponent>())
            {
                target = e;
                return true;
            }
        }
        target = default;
        return false;
    }

    private bool TryGetCursor(out CursorInputComponent cursor)
    {
        foreach (var e in _cursorSet.GetEntities())
        {
            cursor = e.Get<CursorInputComponent>();
            return true;
        }
        cursor = default;
        return false;
    }

    private ref readonly GizmoStateComponent GetGizmoState()
    {
        foreach (var e in _gizmoStateSet.GetEntities())
            return ref e.Get<GizmoStateComponent>();
        // No state entity registered — create one with defaults so the gizmo still works standalone.
        var created = _world.CreateEntity();
        created.Set(GizmoStateComponent.Default);
        return ref created.Get<GizmoStateComponent>();
    }

    public void Dispose()
    {
        if (_dragging) CancelDrag();
        HideOverlays();
        _selectedSet.Dispose();
        _gizmoStateSet.Dispose();
        _cursorSet.Dispose();
        GC.SuppressFinalize(this);
    }
}
