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
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The transform gizmo (Wave 4b). In <see cref="RunMode.Edit"/> it draws move / rotate / scale
/// handles plus a selection outline around the entity tagged <c>SelectedComponent</c>, and turns a
/// drag on the active handle into a transform edit that is <b>one undo step per drag</b>.
///
/// <para><b>Overlay entities are standalone, native-resolution chrome.</b> The handles + outline
/// are mesh entities the gizmo creates and owns (tagged <see cref="GizmoOverlayComponent"/>); they
/// are <b>never</b> <c>ChildOf</c>-parented to the selected game entity
/// (<c>HierarchySystem.DisposeOrphans</c> runs in Edit and would cascade-dispose them). Their
/// visuals are emitted by <see cref="EmitOverlays"/> — called from the DRAW pipeline (via
/// <c>EditorOverlayPrepSystem</c>, after <c>SelectionSystem</c>, before the render passes) so they
/// read the frame's FINAL camera and selection — in <b>screen pixels</b> on the native-resolution
/// <c>RenderTargetID.Editor</c> target: world geometry is projected through the pure
/// <see cref="OverlayProjection"/> (camera view matrix → aspect-fit destination mapping), sizes
/// are virtual-pixel constants scaled by the aspect-fit factor only (never the camera zoom — the
/// projection replaces the old <c>1/Zoom</c> world-space compensation with the same apparent
/// size), and every mesh is clipped to the game viewport rectangle
/// (<see cref="OverlayMeshClip"/>) so nothing draws over the letterbox bars; the shell's opaque
/// panels sit above the overlay depth band and cover the chrome margins. Per the chrome rule the
/// overlay entities carry <b>no</b> <c>VisibleComponent</c> (the Editor pass renders every
/// matching entity; its presence would pull them into <c>MeshPrepSystem</c>, which overwrites the
/// identity <c>WorldMatrix</c> the screen-baked vertices require).</para>
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
/// <para><b>Tool modality.</b> The gizmo acts only while the shared
/// <see cref="GizmoStateComponent.Mode"/> is <see cref="EditorToolMode.SelectTransform"/>: in
/// <see cref="EditorToolMode.Place"/> (and the future brush modes) it cancels any in-flight drag,
/// hides its overlays, and claims nothing — activating a placement/brush tool visibly deactivates
/// the transform gizmo (the Unity/Godot convention; wave-repass §S1).</para>
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
/// space: for those the gizmo reads the cursor's <c>VirtualPosition</c> and hit-tests handles with
/// no zoom compensation (screen-space passes have no camera). The transform math
/// (<see cref="GizmoTransform"/>) is space-agnostic, so move / rotate / scale all work — the only
/// difference is which coordinate pair feeds it. <c>Main</c>-target entities keep the world-space
/// path (cursor <c>WorldPosition</c>, <c>1/Camera.Zoom</c> handle hit-test sizing). The VISUALS
/// always land on the Editor target; the entity's own space only selects which
/// <see cref="OverlayProjection"/> factory maps them to the screen.</para>
///
/// <para><b>Proxy targets write back into the bound component, never the proxy (Wave 8b).</b> When
/// the selected entity is a collider gizmo proxy (<see cref="GizmoProxyComponent"/>), the drag is
/// mechanically identical — same handle hit-test at the proxy's pivot, same coalescing transaction,
/// one undo step per drag — but each frame pushes a <see cref="ColliderEditCommand"/> against the
/// proxy's BOUND game entity (shifting <c>BoxColliderComponent.Bounds</c>, translating every
/// <c>ConvexColliderComponent.ModelVertices</c> entry, or — for a
/// <see cref="ProxyBindingKind.ConvexVertex"/> handle — moving ONE model vertex via the
/// inverse-transformed world delta) instead of a <see cref="TransformEditCommand"/>: the proxy is
/// transient (despawned on deselect), so a command recorded against it would dangle, and its
/// transform is re-derived from the collider by <c>ProxySyncSystem</c> anyway. The active tool is
/// forced to Move for proxies; grid-snap quantizes the shape's world reference point (box
/// top-left / convex centroid / the vertex) like a move-drag's position.</para>
///
/// <para><b>Box proxies grow resize handles (island-authoring Slice 2).</b> While the selected
/// proxy binds <c>BoxColliderComponent.Bounds</c>, eight extra handles — the box's corners and
/// edge midpoints (<see cref="BoxResize"/>) — are hit-tested BEFORE the centre move handle; a
/// press on one starts a resize drag that moves exactly the grabbed edge(s), opposite edges
/// anchored, sides clamped at <see cref="BoxResize.MinSize"/>, through the same
/// one-drag-one-undo <see cref="ColliderEditCommand"/> path.</para>
///
/// <para><b>Vertex drags reject non-convex results loudly.</b> A
/// <see cref="ProxyBindingKind.ConvexVertex"/> drag frame whose target vertex position would
/// make the polygon non-convex (<see cref="ProxyGeometry.IsConvex"/>) is NOT applied — the
/// vertex visually sticks at its last valid position and a warning is logged once per drag.
/// Auto-hulling instead was rejected: it can reorder or drop vertices mid-drag, invalidating the
/// very (kind, index) binding being dragged (see the vertex-editing premise).</para>
/// </summary>
public sealed class GizmoSystem : ISystem<GameState>
{
    /// <summary>Handle/outline sizing constants in VIRTUAL pixels: hit-tests divide by zoom for
    /// world units (unchanged); visuals scale by the aspect-fit factor via
    /// <see cref="OverlayProjection.ToScreenSize"/> — same apparent size, rasterized natively.</summary>
    private const float MoveHandlePixelRadius = 9f;
    private const float ScaleHandlePixelRadius = 7f;
    private const float ScaleHandlePixelDistance = 48f;
    private const float RotateRingPixelRadius = 40f;
    private const float RotateRingPixelTolerance = 7f;
    private const float OutlinePixelThickness = 2f;
    /// <summary>Box-proxy resize handles: the grab radius (hit-test, ÷ zoom for world units) and
    /// the square visual's half-size (aspect-fit scaled, constant on screen).</summary>
    private const float ResizeHandleHitPixelRadius = 8f;
    private const float ResizeHandlePixelHalfSize = 5f;

    /// <summary>The gizmo overlays' depth band on the Editor target: above the proxy outlines
    /// (<see cref="ProxySyncSystem.ProxyLayerDepth"/>), below the shell's opaque panels
    /// (<c>EditorChromeBuilder.PanelDepth</c> = 0.1) — so the panels clip the overlays wherever
    /// the chrome margins are.</summary>
    public const float OverlayLayerDepth = 0.04f;

    private readonly World _world;
    private readonly Camera _camera;
    private readonly EditorHistory _history;
    private readonly ViewportManager? _viewportManager;
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
    private int _dragBindingIndex;
    private Rectangle _beforeBounds;
    private Vector2[]? _beforeVertices;
    private Vector2 _dragStartRefWorld; // box top-left / convex centroid / vertex, world, at drag-start
    private BoxResizeHandle _dragResizeHandle; // None = a plain move drag
    private bool _convexRejectLogged; // the once-per-drag loud reject

    public bool IsEnabled { get; set; } = true;

    /// <param name="viewportManager">Supplies the aspect-fit destination the overlay visuals are
    /// projected into (see <see cref="OverlayProjection"/>). Null (world-free unit tests) degrades
    /// to the identity aspect-fit — screen == virtual.</param>
    public GizmoSystem(World world, Camera camera, EditorHistory history,
        ViewportManager? viewportManager = null)
    {
        _world = world;
        _camera = camera;
        _history = history;
        _viewportManager = viewportManager;
        _selectedSet = world.GetEntities().With<SelectedComponent>().AsSet();
        _gizmoStateSet = world.GetEntities().With<GizmoStateComponent>().AsSet();
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        // Edit-guarded: inert in Play. (The overlay visuals are owned by EmitOverlays — the
        // draw-phase emission pass — which tears them down when editing exits.)
        if (state.RunMode != RunMode.Edit)
        {
            if (_dragging) CancelDrag();
            WriteClaim(false);
            return;
        }

        // Tool modality (island-authoring §S1): the transform gizmo owns viewport presses only in
        // SelectTransform. In Place (and the future brush modes) it is visibly deactivated — any
        // in-flight drag is cancelled, no handle is hit-tested, and nothing is claimed (the mode,
        // not the claim, is what mutes the selection pass there).
        if (GetGizmoState().Mode != EditorToolMode.SelectTransform)
        {
            if (_dragging) CancelDrag();
            WriteClaim(false);
            return;
        }

        if (!TryGetSelected(out var target))
        {
            // Nothing selected — finish any in-flight drag. No selection means no handles, so
            // nothing to claim: the selection pass owns every press.
            if (_dragging) CancelDrag();
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

        // Target-aware space: Main-target entities are world-space (cursor WorldPosition, handle
        // hit-tests sized by 1/Zoom); UI/HUD/Scroll-target entities are screen-space (their
        // transforms are virtual coordinates → cursor VirtualPosition, no zoom compensation —
        // screen-space passes have no camera).
        var space = OverlaySpace(target);
        var worldSpace = space == RenderTargetID.Main;
        var invZoom = worldSpace && _camera.Zoom > 0f ? 1f / _camera.Zoom : 1f;

        if (!TryGetCursor(out var cursor))
        {
            WriteClaim(false);
            return;
        }

        var cursorPoint = worldSpace ? cursor.WorldPosition : cursor.VirtualPosition;
        // Click-ownership: publish whether the gizmo owns this frame's press (a handle was hit on
        // the press edge, or a drag is in progress) so the SAME frame's selection pass — which
        // runs later, at the end of the draw pipeline — neither re-picks nor click-empty-clears
        // under a handle that lies outside the selected sprite's bounds. Written every Edit frame
        // (set or cleared), so it cannot go stale while the gizmo runs.
        WriteClaim(ProcessDrag(target, tool, cursor, cursorPoint, pivot, invZoom));
    }

    /// <summary>
    /// Emits (or hides) the overlay VISUALS for this frame — the selection outline + active-tool
    /// handle — in screen pixels on the native-resolution Editor target. Called from the DRAW
    /// pipeline (the <c>editor.overlayPrep</c> entry, via <c>EditorOverlayPrepSystem</c>) after
    /// <c>SelectionSystem</c> and after the frame's camera is final, so the overlays never lag a
    /// camera pan/zoom or a same-frame selection change. Geometry is projected through the pure
    /// <see cref="OverlayProjection"/> and clipped to the game viewport rectangle
    /// (<see cref="OverlayMeshClip"/>).
    /// </summary>
    public void EmitOverlays(GameState state)
    {
        if (!IsEnabled || state.RunMode != RunMode.Edit || !TryGetSelected(out var target)
            || GetGizmoState().Mode != EditorToolMode.SelectTransform)
        {
            // Outside SelectTransform the gizmo is visibly deactivated (§S1) — no handles, no
            // outline — even while something stays selected.
            HideOverlays();
            return;
        }

        ref readonly var gizmo = ref GetGizmoState();
        var tool = target.Has<GizmoProxyComponent>() ? GizmoTool.Move : gizmo.Tool;
        var pivot = target.Get<TransformComponent>().WorldPosition;
        var space = OverlaySpace(target);
        // The scale handle's SOURCE-space offset mirrors the hit-test's (world units ÷ zoom /
        // virtual units), so its visual is projected from the exact point HandleHit tests.
        var invZoom = space == RenderTargetID.Main && _camera.Zoom > 0f ? 1f / _camera.Zoom : 1f;
        var projection = OverlayProjection.For(space, _camera, _viewportManager);

        EnsureOverlays();
        SetMesh(_outline, OverlayMeshClip.ClipToRect(BuildOutline(target, pivot, projection), projection.Viewport));
        // A box proxy's handle set = the centre move handle PLUS the eight resize squares; every
        // other target keeps the active tool's single handle.
        var handleMesh = TryGetBoxProxyWorldRect(target, out var boxMin, out var boxMax)
            ? BuildBoxProxyHandles(boxMin, boxMax, pivot, projection)
            : BuildHandle(tool, pivot, invZoom, projection);
        SetMesh(_handle, OverlayMeshClip.ClipToRect(handleMesh, projection.Viewport));
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

        // A box proxy's resize handles are tested BEFORE the centre move handle (they win where
        // a small box packs them close). None found falls through to the ordinary handle test.
        var resizeHandle = BoxResizeHandle.None;
        if (TryGetBoxProxyWorldRect(target, out var boxMin, out var boxMax))
            resizeHandle = BoxResize.HitTest(boxMin, boxMax, cursorPoint, ResizeHandleHitPixelRadius * invZoom);

        if (resizeHandle == BoxResizeHandle.None && !HandleHit(tool, pivot, cursorPoint, invZoom))
            return false;

        BeginDrag(target, tool, cursorPoint, pivot, resizeHandle);
        // Claim even when BeginDrag refused (an unsnapshottable proxy binding): the press landed
        // on a handle, so it must not fall through to selection as a click-empty / re-pick.
        return true;
    }

    /// <summary>The bound box collider's axis-aligned world rectangle when <paramref name="target"/>
    /// is a box-bounds proxy with a live owner — the rect the resize handles sit on.</summary>
    private static bool TryGetBoxProxyWorldRect(Entity target, out Vector2 min, out Vector2 max)
    {
        min = max = default;
        if (!target.Has<GizmoProxyComponent>()) return false;
        var binding = target.Get<GizmoProxyComponent>();
        if (binding.Kind != ProxyBindingKind.BoxColliderBounds) return false;
        if (!ProxyGeometry.TryGetWorldOutline(binding.Target, binding.Kind, binding.Index, out var corners))
            return false;
        min = corners[0]; // TL
        max = corners[2]; // BR
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

    private void BeginDrag(Entity target, GizmoTool tool, Vector2 cursorWorld, Vector2 pivot,
        BoxResizeHandle resizeHandle = BoxResizeHandle.None)
    {
        // A proxy drag writes back into the bound collider field — snapshot that binding first;
        // an unsnapshottable binding (owner died / lost the collider) starts no drag at all.
        _dragIsProxy = target.Has<GizmoProxyComponent>();
        if (_dragIsProxy && !TrySnapshotProxyBinding(target))
        {
            _dragIsProxy = false;
            return;
        }

        _dragResizeHandle = _dragIsProxy ? resizeHandle : BoxResizeHandle.None;
        _convexRejectLogged = false;
        if (_dragResizeHandle != BoxResizeHandle.None)
        {
            // A resize drag's snapped reference point is the GRABBED handle (corner / edge
            // midpoint), not the box top-left — so with snap on, the dragged edge lands on grid.
            var ownerWorld = _dragOwner.Get<TransformComponent>().WorldPosition;
            var min = ownerWorld + new Vector2(_beforeBounds.Left, _beforeBounds.Top);
            var max = ownerWorld + new Vector2(_beforeBounds.Right, _beforeBounds.Bottom);
            _dragStartRefWorld = BoxResize.HandleWorld(min, max, _dragResizeHandle);
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

            case ProxyBindingKind.ConvexVertex:
                if (!owner.Has<ConvexColliderComponent>()) return false;
                var vertexCollider = owner.Get<ConvexColliderComponent>();
                if (vertexCollider.ModelVertices == null
                    || binding.Index < 0 || binding.Index >= vertexCollider.ModelVertices.Length) return false;
                _beforeVertices = (Vector2[])vertexCollider.ModelVertices.Clone();
                _dragStartRefWorld = ProxyGeometry.ConvexVertexWorld(
                    ownerTransform, vertexCollider, binding.Index);
                break;

            case ProxyBindingKind.BoundaryVertex:
                if (!owner.Has<BoundaryComponent>()) return false;
                var boundary = owner.Get<BoundaryComponent>();
                if (boundary.Points == null
                    || binding.Index < 0 || binding.Index >= boundary.Points.Length) return false;
                _beforeVertices = (Vector2[])boundary.Points.Clone();
                // Boundary points are local to Position (no rotation/scale).
                _dragStartRefWorld = ownerTransform.Position + boundary.Points[binding.Index];
                break;

            default:
                return false;
        }

        _dragOwner = owner;
        _dragBindingKind = binding.Kind;
        _dragBindingIndex = binding.Index;
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
                // A resize drag moves only the grabbed edge(s); a move drag shifts the whole rect.
                var after = _dragResizeHandle != BoxResizeHandle.None
                    ? BoxResize.Apply(_beforeBounds, _dragResizeHandle, worldDelta)
                    : new Rectangle(
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
            case ProxyBindingKind.ConvexVertex:
            {
                if (_beforeVertices == null) return;
                if (!_dragOwner.Has<ConvexColliderComponent>() || !_dragOwner.Has<TransformComponent>()) return;
                if (_dragBindingIndex < 0 || _dragBindingIndex >= _beforeVertices.Length) return;
                // Move ONE model vertex by the inverse-transformed delta...
                var vertexCollider = _dragOwner.Get<ConvexColliderComponent>();
                var vertexDelta = ProxyGeometry.WorldDeltaToModelDelta(
                    _dragOwner.Get<TransformComponent>(), vertexCollider.IgnoreTransformRotation, worldDelta);
                var afterVertices = (Vector2[])_beforeVertices.Clone();
                afterVertices[_dragBindingIndex] = _beforeVertices[_dragBindingIndex] + vertexDelta;
                // ...rejecting (loudly, once per drag) a result that breaks convexity: the vertex
                // sticks at its last valid position instead of applying an invalid shape. See the
                // class doc for why auto-hulling was rejected.
                if (!ProxyGeometry.IsConvex(afterVertices))
                {
                    if (!_convexRejectLogged)
                    {
                        _convexRejectLogged = true;
                        Logger.Warning(
                            "[level-editor] Vertex drag rejected: the result would make the " +
                            "collider non-convex. The vertex stays at its last valid position.");
                    }
                    return;
                }
                _history.Push(ColliderEditCommand.ForConvex(_dragOwner, afterVertices));
                break;
            }
            case ProxyBindingKind.BoundaryVertex:
            {
                if (_beforeVertices == null) return;
                if (!_dragOwner.Has<BoundaryComponent>() || !_dragOwner.Has<TransformComponent>()) return;
                if (_dragBindingIndex < 0 || _dragBindingIndex >= _beforeVertices.Length) return;
                // Move ONE polyline point. A boundary has no convexity constraint (open polyline),
                // so every result is applied; the edit re-fires the bake through BoundaryEditCommand.
                var boundaryDelta = ProxyGeometry.WorldDeltaToModelDelta(
                    _dragOwner.Get<TransformComponent>(), ignoreRotation: true, worldDelta);
                var afterPoints = (Vector2[])_beforeVertices.Clone();
                afterPoints[_dragBindingIndex] = _beforeVertices[_dragBindingIndex] + boundaryDelta;
                _history.Push(BoundaryEditCommand.For(_dragOwner, afterPoints));
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
        _dragBindingIndex = 0;
        _dragResizeHandle = BoxResizeHandle.None;
        _convexRejectLogged = false;
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
        e.Set(new TransformComponent()); // identity — vertices are baked in screen space
        e.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Editor, // native-resolution overlay, under the chrome panels
            LayerDepth = OverlayLayerDepth,
            WorldMatrix = Matrix.Identity,
        });
        // NO VisibleComponent — the chrome rule: the Editor pass renders every matching entity,
        // and its presence would pull the mesh into MeshPrepSystem, which overwrites the identity
        // WorldMatrix the screen-baked vertices require (the Wave-7 double-offset trap).
        return e;
    }

    private void HideOverlays()
    {
        if (!_overlaysCreated) return;
        if (_outline.IsAlive) _outline.Dispose();
        if (_handle.IsAlive) _handle.Dispose();
        _overlaysCreated = false;
    }

    private static void SetMesh(Entity e, MeshData mesh)
    {
        ref var dc = ref e.Get<DrawComponent>();
        dc.Type = DrawElementType.Mesh;
        dc.Vertices = mesh.Vertices;
        dc.Indices = mesh.Indices;
        dc.PrimitiveType = mesh.PrimitiveType;
        dc.WorldMatrix = Matrix.Identity;
        dc.LayerDepth = OverlayLayerDepth;
        dc.Target = RenderTargetID.Editor;
    }

    /// <summary>The selection outline, in screen pixels: the sprite's rendered quad (or the bound
    /// collider shape for a proxy), projected corner-by-corner and stroked at native resolution.
    /// Falls back to a small box around the pivot when the entity has no sprite bounds.</summary>
    private static MeshData BuildOutline(Entity target, Vector2 pivot, in OverlayProjection projection)
    {
        var thickness = projection.ToScreenSize(OutlinePixelThickness);
        var color = Color.Yellow;

        // A selected collider proxy outlines its bound shape (the same border the pick tested),
        // so the selection feedback traces the thing being edited. A vertex handle outlines as a
        // constant-on-screen square around the vertex (its world outline is deliberately tiny).
        if (target.Has<GizmoProxyComponent>())
        {
            var binding = target.Get<GizmoProxyComponent>();
            if (ProxyGeometry.TryGetWorldOutline(binding.Target, binding.Kind, binding.Index, out var shape))
            {
                if (ProxyGeometry.IsVertexHandle(binding.Kind))
                {
                    var centre = projection.ToScreen(ProxyGeometry.Centroid(shape));
                    var vHalf = projection.ToScreenSize(ProxySyncSystem.VertexHandlePixelHalfSize + 2f);
                    var square = new[]
                    {
                        centre + new Vector2(-vHalf, -vHalf), centre + new Vector2(vHalf, -vHalf),
                        centre + new Vector2(vHalf, vHalf), centre + new Vector2(-vHalf, vHalf),
                    };
                    return new PolygonOutlineMeshGenerator(square, thickness, color, closed: true).Generate();
                }
                return new PolygonOutlineMeshGenerator(
                    Project(shape, projection), thickness, color, closed: true).Generate();
            }
        }

        // A selected boundary entity: highlight its open polyline (the thing being edited).
        if (target.Has<MonoDreams.LevelEditor.Component.BoundaryComponent>())
        {
            var boundary = target.Get<MonoDreams.LevelEditor.Component.BoundaryComponent>();
            if (boundary.Points is { Length: >= 2 })
            {
                var worldPoly = Boundary.BoundaryGeometry.WorldPolyline(boundary.Points, pivot);
                return new PolygonOutlineMeshGenerator(
                    Project(worldPoly, projection), thickness, color, closed: false).Generate();
            }
        }

        if (target.Has<SpriteInfoComponent>())
        {
            var corners = GizmoTransform.SpriteWorldQuad(
                target.Get<TransformComponent>(), target.Get<SpriteInfoComponent>());
            return new PolygonOutlineMeshGenerator(
                Project(corners, projection), thickness, color, closed: true).Generate();
        }

        // No sprite bounds: a small box around the pivot (constant screen size).
        var center = projection.ToScreen(pivot);
        var half = projection.ToScreenSize(16f);
        var box = new[]
        {
            center + new Vector2(-half, -half), center + new Vector2(half, -half),
            center + new Vector2(half, half),   center + new Vector2(-half, half),
        };
        return new PolygonOutlineMeshGenerator(box, thickness, color, closed: true).Generate();
    }

    /// <summary>The active tool's handle, in screen pixels around the projected pivot — constant
    /// on-screen size at every camera zoom (the projection scales sizes by the aspect-fit factor
    /// only).</summary>
    private static MeshData BuildHandle(GizmoTool tool, Vector2 pivot, float invZoom, in OverlayProjection projection)
    {
        var center = projection.ToScreen(pivot);
        switch (tool)
        {
            case GizmoTool.Move:
            {
                var r = projection.ToScreenSize(MoveHandlePixelRadius);
                var arm = projection.ToScreenSize(MoveHandlePixelRadius * 2.4f);
                var th = projection.ToScreenSize(OutlinePixelThickness);
                return new CompositeMeshGenerator()
                    .Add(new LineMeshGenerator(center, center + new Vector2(arm, 0f), th, Color.OrangeRed))
                    .Add(new LineMeshGenerator(center, center + new Vector2(0f, -arm), th, Color.LimeGreen))
                    .Add(new CircleMeshGenerator(center, r, Color.White, 18))
                    .Generate();
            }
            case GizmoTool.Rotate:
            {
                var ring = projection.ToScreenSize(RotateRingPixelRadius);
                var th = projection.ToScreenSize(RotateRingPixelTolerance * 0.6f);
                return new CircleOutlineMeshGenerator(center, ring, th, Color.DeepSkyBlue, 28).Generate();
            }
            case GizmoTool.Scale:
            {
                // Projected from the SAME source-space point HandleHit tests, so the visual and
                // the grab region can never diverge (any camera transform included).
                var handlePos = projection.ToScreen(ScaleHandlePosition(pivot, invZoom));
                var r = projection.ToScreenSize(ScaleHandlePixelRadius);
                var th = projection.ToScreenSize(OutlinePixelThickness);
                var box = new Rectangle(
                    (int)(handlePos.X - r), (int)(handlePos.Y - r), (int)(r * 2f), (int)(r * 2f));
                return new CompositeMeshGenerator()
                    .Add(new LineMeshGenerator(center, handlePos, th, Color.Gold))
                    .Add(new FilledRectangleMeshGenerator(box, Color.Gold))
                    .Generate();
            }
            default:
                return new MeshData();
        }
    }

    /// <summary>A box proxy's handle set, in screen pixels: the centre move handle (the same
    /// cross + disc a move drag grabs) plus the eight resize squares at the box's projected
    /// corners and edge midpoints — drawn from the SAME <see cref="BoxResize.HandleWorld"/>
    /// points the hit-test uses, so grab region and visual cannot diverge.</summary>
    private static MeshData BuildBoxProxyHandles(Vector2 boxMin, Vector2 boxMax, Vector2 pivot,
        in OverlayProjection projection)
    {
        var center = projection.ToScreen(pivot);
        var r = projection.ToScreenSize(MoveHandlePixelRadius);
        var half = projection.ToScreenSize(ResizeHandlePixelHalfSize);
        var composite = new CompositeMeshGenerator()
            .Add(new CircleMeshGenerator(center, r, Color.White, 18));
        foreach (var handle in BoxResize.Handles)
        {
            var p = projection.ToScreen(BoxResize.HandleWorld(boxMin, boxMax, handle));
            var square = new Rectangle(
                (int)(p.X - half), (int)(p.Y - half), (int)(half * 2f), (int)(half * 2f));
            composite.Add(new FilledRectangleMeshGenerator(square, Color.Gold));
        }
        return composite.Generate();
    }

    private static Vector2[] Project(Vector2[] points, in OverlayProjection projection)
    {
        var result = new Vector2[points.Length];
        for (var i = 0; i < points.Length; i++) result[i] = projection.ToScreen(points[i]);
        return result;
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
