#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Proxy;
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
/// <para><b>Click-ownership.</b> The gizmo publishes a frame-scoped claim
/// (<see cref="GizmoStateComponent.PressClaimed"/>) on every Edit frame: true when the press edge
/// landed on the active tool's handle or while a drag is in progress, false otherwise.
/// <c>SelectionSystem</c> (end of the draw pipeline, same frame) skips a claimed press entirely —
/// otherwise a handle that lies outside the selected sprite's bounds (the rotate ring, the scale
/// handle, a proxy's centre move-handle) would read as click-empty and clear the selection in the
/// very frame the drag began, cancelling the drag one frame later.</para>
///
/// <para><b>Target-aware space (Wave 8a).</b> A selected entity whose render target is
/// <c>UI</c>/<c>HUD</c>/<c>Scroll</c> lives in <b>virtual</b> (screen-space) coordinates, not world
/// space: for those the gizmo reads the cursor's <c>VirtualPosition</c>, draws its overlays on the
/// entity's own target (so outline + handles composite exactly over it), and sizes handles with no
/// zoom compensation (screen-space passes have no camera). The transform math
/// (<see cref="GizmoTransform"/>) is space-agnostic, so move / rotate / scale all work — the only
/// difference is which coordinate pair feeds it. <c>Main</c>-target entities keep the world-space
/// path (cursor <c>WorldPosition</c>, overlays on Main, <c>1/Camera.Zoom</c> handle sizing).</para>
///
/// <para><b>Proxy targets write back into the bound component, never the proxy (Wave 8b).</b> When
/// the selected entity is a collider gizmo proxy (<see cref="GizmoProxyComponent"/>), the drag is
/// mechanically identical — same handle hit-test at the proxy's pivot, same coalescing transaction,
/// one undo step per drag — but each frame pushes a <see cref="ColliderEditCommand"/> against the
/// proxy's BOUND game entity (shifting <c>BoxColliderComponent.Bounds</c>, or translating every
/// <c>ConvexColliderComponent.ModelVertices</c> entry via the inverse-transformed world delta)
/// instead of a <see cref="TransformEditCommand"/>: the proxy is transient (despawned on deselect),
/// so a command recorded against it would dangle, and its transform is re-derived from the collider
/// by <c>ProxySyncSystem</c> anyway. Only the <b>move</b> tool applies to proxies this wave (the
/// active tool is forced to Move; scale-resize is a documented follow-up); grid-snap quantizes the
/// shape's world reference point (box top-left / convex centroid) like a move-drag's position.</para>
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

    // Proxy drag state (Wave 8b): the write-back target is the proxy's BOUND game entity and its
    // collider field, snapshotted immutably at drag-start (same recompute-from-start design).
    private bool _dragIsProxy;
    private Entity _dragOwner;
    private ProxyBindingKind _dragBindingKind;
    private Rectangle _beforeBounds;
    private Vector2[]? _beforeVertices;
    private Vector2 _dragStartRefWorld; // box top-left / convex centroid, world, at drag-start

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
            WriteClaim(false);
            return;
        }

        if (!TryGetSelected(out var target))
        {
            // Nothing selected — finish any in-flight drag and hide the overlays. No selection
            // means no handles, so nothing to claim: the selection pass owns every press.
            if (_dragging) CancelDrag();
            HideOverlays();
            WriteClaim(false);
            return;
        }

        ref readonly var gizmo = ref GetGizmoState();
        ref readonly var transform = ref target.Get<TransformComponent>();
        var pivot = transform.WorldPosition;

        // A collider proxy edits component-local spatial data: only Move applies this wave, so the
        // active tool is forced to Move regardless of the toolbar selection (rotate/scale handles
        // would imply an edit the write-back cannot express yet — documented follow-up).
        var tool = target.Has<GizmoProxyComponent>() ? GizmoTool.Move : gizmo.Tool;

        // Target-aware space: Main-target entities are world-space (cursor WorldPosition, overlays
        // on Main, handles sized by 1/Zoom); UI/HUD/Scroll-target entities are screen-space (their
        // transforms are virtual coordinates → cursor VirtualPosition, overlays on their own
        // target, no zoom compensation — screen-space passes have no camera).
        var space = OverlaySpace(target);
        var worldSpace = space == RenderTargetID.Main;
        var invZoom = worldSpace && _camera.Zoom > 0f ? 1f / _camera.Zoom : 1f;

        if (!TryGetCursor(out var cursor))
        {
            WriteClaim(false);
            EnsureOverlays();
            UpdateOverlayMeshes(target, tool, pivot, invZoom, space);
            return;
        }

        var cursorPoint = worldSpace ? cursor.WorldPosition : cursor.VirtualPosition;
        // Click-ownership: publish whether the gizmo owns this frame's press (a handle was hit on
        // the press edge, or a drag is in progress) so the SAME frame's selection pass — which
        // runs later, at the end of the draw pipeline — neither re-picks nor click-empty-clears
        // under a handle that lies outside the selected sprite's bounds. Written every Edit frame
        // (set or cleared), so it cannot go stale while the gizmo runs.
        WriteClaim(ProcessDrag(target, tool, cursor, cursorPoint, pivot, invZoom));

        EnsureOverlays();
        UpdateOverlayMeshes(target, tool, pivot, invZoom, space);
    }

    /// <summary>Advances the drag lifecycle for this frame. Returns whether the gizmo owns the
    /// cursor's left press this frame (the click-ownership claim the selection pass honors).</summary>
    private bool ProcessDrag(Entity target, GizmoTool tool, in CursorInputComponent cursor,
        Vector2 cursorPoint, Vector2 pivot, float invZoom)
    {
        if (_dragging)
        {
            // The drag is bound to the entity it grabbed. If the selection moved elsewhere
            // mid-drag (Delete/undo/a headless op — a click cannot: the claim suppresses it),
            // the drag's premise is gone; cancel exactly like a cleared selection.
            if (target != _dragTarget)
            {
                CancelDrag();
                return false;
            }

            // Apply the live edit from the drag-start cursor → current cursor so snapping is stable
            // and floating error does not accumulate frame-to-frame (the target is recomputed from
            // the immutable drag-start state each frame, not stacked on the previous frame's result).
            ApplyDragEdit(target, cursorPoint);

            if (cursor.LeftButtonReleased || !cursor.LeftButton)
                EndDrag();
            // The (possibly just-ended) drag owned this frame's cursor: even a spurious same-frame
            // press edge belongs to the gizmo, never to the selection pass.
            return true;
        }

        // Not dragging: a press over the active handle starts a drag. A press over the editor
        // chrome / letterbox margins never starts one — the cursor's world AND virtual positions
        // are frozen at their last inside-the-viewport values there (a toolbar click must not
        // grab the gizmo).
        if (!cursor.LeftButtonPressed || cursor.OutsideViewport) return false;
        if (!HandleHit(tool, pivot, cursorPoint, invZoom)) return false;

        BeginDrag(target, tool, cursorPoint, pivot);
        // Claim even when BeginDrag refused (an unsnapshottable proxy binding): the press landed
        // on a handle, so it must not fall through to selection as a click-empty / re-pick.
        return true;
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
        // A proxy drag writes back into the bound collider field — snapshot that binding first;
        // an unsnapshottable binding (owner died / lost the collider) starts no drag at all.
        _dragIsProxy = target.Has<GizmoProxyComponent>();
        if (_dragIsProxy && !TrySnapshotProxyBinding(target))
        {
            _dragIsProxy = false;
            return;
        }

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

    /// <summary>Snapshots the proxy's bound collider field at drag-start (the immutable "before"
    /// every drag frame recomputes from) plus the shape's world reference point — the box's world
    /// top-left / the convex world centroid — which the snapped move delta is measured against.</summary>
    private bool TrySnapshotProxyBinding(Entity proxyEntity)
    {
        var binding = proxyEntity.Get<GizmoProxyComponent>();
        var owner = binding.Target;
        if (!owner.IsAlive || !owner.Has<TransformComponent>()) return false;
        var ownerTransform = owner.Get<TransformComponent>();

        switch (binding.Kind)
        {
            case ProxyBindingKind.BoxColliderBounds:
                if (!owner.Has<BoxColliderComponent>()) return false;
                var box = owner.Get<BoxColliderComponent>();
                _beforeBounds = box.Bounds;
                _dragStartRefWorld = ownerTransform.WorldPosition + new Vector2(box.Bounds.X, box.Bounds.Y);
                break;

            case ProxyBindingKind.ConvexColliderShape:
                if (!owner.Has<ConvexColliderComponent>()) return false;
                var convex = owner.Get<ConvexColliderComponent>();
                if (convex.ModelVertices == null || convex.ModelVertices.Length < 3) return false;
                _beforeVertices = (Vector2[])convex.ModelVertices.Clone();
                _dragStartRefWorld = ProxyGeometry.Centroid(
                    ProxyGeometry.ConvexWorldVertices(ownerTransform, convex));
                break;

            default:
                return false;
        }

        _dragOwner = owner;
        _dragBindingKind = binding.Kind;
        return true;
    }

    private void ApplyDragEdit(Entity target, Vector2 currentCursorWorld)
    {
        if (_dragIsProxy)
        {
            ApplyProxyDragEdit(currentCursorWorld);
            return;
        }

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

    /// <summary>
    /// The proxy write-back (Wave 8b): recompute the shape's world reference point from the
    /// immutable drag-start state through the SAME move math (and snap semantics) a transform drag
    /// uses, then push a <see cref="ColliderEditCommand"/> against the bound game entity — one per
    /// frame, coalesced by the open transaction into one undo step on release.
    /// </summary>
    private void ApplyProxyDragEdit(Vector2 currentCursorWorld)
    {
        if (!_dragOwner.IsAlive) return;

        var (afterRef, _, _, _) = GizmoTransform.Compute(
            GizmoTool.Move,
            _dragStartRefWorld, 0f, Vector2.One, Vector2.Zero,
            _dragStartPivot, _dragStartCursorWorld, currentCursorWorld,
            SnapStep(), 0f);
        var worldDelta = afterRef - _dragStartRefWorld;

        switch (_dragBindingKind)
        {
            case ProxyBindingKind.BoxColliderBounds:
            {
                if (!_dragOwner.Has<BoxColliderComponent>()) return;
                // Bounds is an int Rectangle: the world delta rounds to whole units by nature.
                var after = new Rectangle(
                    _beforeBounds.X + (int)MathF.Round(worldDelta.X),
                    _beforeBounds.Y + (int)MathF.Round(worldDelta.Y),
                    _beforeBounds.Width, _beforeBounds.Height);
                _history.Push(ColliderEditCommand.ForBox(_dragOwner, after));
                break;
            }
            case ProxyBindingKind.ConvexColliderShape:
            {
                if (_beforeVertices == null) return;
                if (!_dragOwner.Has<ConvexColliderComponent>() || !_dragOwner.Has<TransformComponent>()) return;
                // Translate the WORLD outline by the delta: model vertices shift by the
                // inverse-transformed delta (rotation/scale honored, IgnoreTransformRotation kept).
                var convex = _dragOwner.Get<ConvexColliderComponent>();
                var modelDelta = ProxyGeometry.WorldDeltaToModelDelta(
                    _dragOwner.Get<TransformComponent>(), convex.IgnoreTransformRotation, worldDelta);
                var after = new Vector2[_beforeVertices.Length];
                for (var i = 0; i < after.Length; i++) after[i] = _beforeVertices[i] + modelDelta;
                _history.Push(ColliderEditCommand.ForConvex(_dragOwner, after));
                break;
            }
        }
    }

    private void EndDrag()
    {
        _dragging = false;
        _dragTarget = default;
        ClearProxyDragState();
        // Commit the accumulated transaction → exactly one history entry for the whole drag. An
        // empty transaction (no movement at all) commits nothing.
        _history.CommitTransaction();
    }

    private void CancelDrag()
    {
        _dragging = false;
        _dragTarget = default;
        ClearProxyDragState();
        if (_history.InTransaction) _history.CancelTransaction();
    }

    private void ClearProxyDragState()
    {
        _dragIsProxy = false;
        _dragOwner = default;
        _beforeVertices = null;
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
        e.Set(new EditorInfrastructureComponent()); // survives a transport Restart
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

        // A selected collider proxy outlines its bound shape (the same border the pick tested),
        // so the selection feedback traces the thing being edited.
        if (target.Has<GizmoProxyComponent>())
        {
            var binding = target.Get<GizmoProxyComponent>();
            if (ProxyGeometry.TryGetWorldOutline(binding.Target, binding.Kind, out var shape))
                return new PolygonOutlineMeshGenerator(shape, thickness, color, closed: true).Generate();
        }

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

    private ref readonly GizmoStateComponent GetGizmoState() => ref GetGizmoStateEntity().Get<GizmoStateComponent>();

    private Entity GetGizmoStateEntity()
    {
        foreach (var e in _gizmoStateSet.GetEntities())
            return e;
        // No state entity registered — create one with defaults so the gizmo still works standalone.
        var created = _world.CreateEntity();
        created.Set(new EditorInfrastructureComponent()); // survives a transport Restart
        created.Set(GizmoStateComponent.Default);
        return created;
    }

    /// <summary>Publishes the click-ownership claim onto the shared gizmo-state entity (see
    /// <see cref="GizmoStateComponent.PressClaimed"/>). Written every frame the gizmo runs, so the
    /// claim is exactly as fresh as the drag state it mirrors.</summary>
    private void WriteClaim(bool claimed)
    {
        ref var gizmoState = ref GetGizmoStateEntity().Get<GizmoStateComponent>();
        gizmoState.PressClaimed = claimed;
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
