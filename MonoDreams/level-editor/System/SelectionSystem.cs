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
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Proxy;
using MonoDreams.LevelEditor.Selection;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.State;
using MonoDreams.UI;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// Click-to-select picking for the editor. On a left-button press in
/// <see cref="RunMode.Edit"/> it hit-tests the cursor against every rendered sprite candidate and
/// selects the <b>topmost</b> — the one the renderer draws frontmost — clearing any prior
/// selection. A click on empty space clears the selection.
///
/// <para><b>Tool modality: selection acts only in <see cref="EditorToolMode.SelectTransform"/>.</b>
/// The coarse mode on the shared <see cref="GizmoStateComponent"/> decides which tool family owns
/// a viewport press at all; in <see cref="EditorToolMode.Place"/> (and the future brush modes) the
/// system is dormant — no pick and no click-empty clear, so a placement click cannot disturb the
/// selection (the placement system auto-selects what it stamps).</para>
///
/// <para><b>Viewport right-click opens the entity menu (UX2-D).</b> Also only in
/// <see cref="EditorToolMode.SelectTransform"/> (when a tool is armed, right-click-as-disarm belongs to
/// the palette/boundary and this system is dormant), a RIGHT-button press picks the entity under the
/// cursor with the SAME <see cref="TryPick"/> logic; on a HIT it selects that entity via
/// <see cref="SelectExclusive"/> (keeping an existing selection when you right-clicked the
/// already-selected one) and raises <see cref="ViewportContextMenuRequested"/> so the overlay opens the
/// entity context menu at the cursor. A right-click over empty space opens no menu and clears no
/// selection — click-empty stays a left-click behavior.</para>
///
/// <para><b>Click-ownership: the gizmo's presses are skipped.</b> A press the gizmo claimed
/// (<see cref="GizmoStateComponent.PressClaimed"/> — the press landed on the active tool's handle,
/// or a handle drag is in progress) is not processed at all: no re-pick, no click-empty clear.
/// Rotate/scale handles (and a collider proxy's centre move-handle) routinely lie outside the
/// selected sprite's bounds, so without the claim the same frame's selection pass would clear the
/// selection (or re-pick an overlapped sprite) and kill the drag the gizmo just began. Ordering
/// dependency: <c>GizmoSystem</c> writes the claim in the UPDATE pipeline, this system reads it at
/// the end of the DRAW pipeline — the same frame's claim is always already written. Releases are
/// never processed (only the press edge is), so releasing over empty space never clears.</para>
///
/// <para><b>Target-aware hit-testing (Wave 8a).</b> Candidates live in two coordinate spaces:
/// <c>Main</c>-target sprites are world-space (drawn through the camera), so they hit-test against
/// <see cref="CursorInputComponent.WorldPosition"/>; <c>UI</c>/<c>HUD</c>/<c>Scroll</c>-target
/// sprites are screen-space (their transforms are in virtual coordinates), so they hit-test against
/// <see cref="CursorInputComponent.VirtualPosition"/> — the letterbox-scaled, pre-camera coordinate
/// that never desyncs from on-screen UI when the camera moves. <c>Editor</c>-target entities are
/// the editor's own chrome and are never selection candidates.</para>
///
/// <para><b>Edit-guarded, registered RunNormally.</b> The system is pre-registered in both modes
/// (per the editor-screen contract) but no-ops in <see cref="RunMode.Play"/>, so it is inert until
/// the designer enters editing. It must be ordered <b>after</b> the draw prep + <c>YSortSystem</c>
/// each frame, because it reads the <b>final</b> post-Y-sort <c>DrawComponent.LayerDepth</c> as the
/// "frontmost" key (mirroring <c>MasterRenderSystem</c>, which sorts on that same final depth).</para>
///
/// <para><b>Topmost = composite order first, then MAX final LayerDepth, then the selection-owned
/// tiebreak.</b> Across targets the final composite stacks Main under UI under HUD under Scroll
/// (<c>FinalDrawSystem</c>'s layer order), so a UI/HUD sprite under the cursor beats an overlapping
/// world sprite regardless of their per-target depths — the rank mirrors what the player sees on
/// top (see <see cref="TargetRank"/>). Within a target the key is MAX final <c>LayerDepth</c>; for
/// an exact tie the renderer's tiebreak (its private per-frame insertion index) cannot be observed,
/// so the system assigns each candidate a stable <see cref="EditorIdComponent"/> the first time it
/// sees it (a monotonic counter = first-seen / creation order) and breaks ties by MAX id — the
/// later-seen entity, which an undisturbed scene renders last. See <see cref="PickTopmost(int,float,int,bool,int,float,int)"/>.</para>
///
/// <para><b>Spriteless entities join the SAME pick, as border-only candidates.</b> A collider
/// ENTITY (colliders-as-entities), a boundary, the camera entity, and the surviving sub-element
/// proxies (vertex/thickness handles) carry no pickable <c>SpriteInfoComponent</c>, so each is
/// folded into the pick as a second candidate source with the SAME rank + depth + id ordering
/// (never a second pick path): rank = Main (world-space outlines), depth =
/// <see cref="ProxyBorderPickDepth"/> (a constant "drawn on top of game sprites" rank — the
/// outline VISUAL renders on the Editor overlay layer above the whole scene, so they win where
/// they visibly overlap; a bake product sits a hair lower at <see cref="BakedProductPickDepth"/>,
/// a vertex handle a hair higher at <see cref="ProxyVertexPickDepth"/>), id = the same
/// <see cref="EditorIdComponent"/> tiebreak. The hit-test is the shape's <b>border</b> within a
/// <c>1/Camera.Zoom</c>-scaled tolerance — never the fill — so a collider that covers a sprite
/// doesn't make the sprite unselectable: click the outline to grab the collider, click inside to
/// pick the sprite.</para>
/// </summary>
/// <remarks>A plain <see cref="ISystem{T}"/> iterating its own candidate set — deliberately NOT an
/// <c>AEntitySetSystem</c>, whose <c>Update</c> early-outs entirely when the set is empty: in a
/// scene with zero rendered sprites (e.g. a fresh scene holding only collider entities) that
/// early-out would silently disable proxy border-picking and click-empty clearing.</remarks>
public sealed class SelectionSystem : ISystem<GameState>
{
    /// <summary>How close (in screen pixels, divided by zoom for world units) a click must land to
    /// a proxy's border to pick it. Generous enough to grab a 2px outline comfortably.</summary>
    public const float ProxyBorderPickTolerancePixels = 8f;

    /// <summary>The depth a proxy candidate competes with in the pick — the "drawn on top of the
    /// game sprites" rank the proxies have always had. A CONSTANT, deliberately decoupled from
    /// the proxy's <c>DrawComponent.LayerDepth</c>: the visual now lives in the Editor target's
    /// low overlay band (<c>ProxySyncSystem.ProxyLayerDepth</c>, under the chrome panels), where
    /// the raw value would lose to any game sprite even though the outline visibly draws above
    /// them all.</summary>
    public const float ProxyBorderPickDepth = 0.998f;

    /// <summary>The pick depth of a <see cref="ProxyBindingKind.ConvexVertex"/> handle —
    /// deliberately a hair above <see cref="ProxyBorderPickDepth"/>: a vertex handle sits ON the
    /// shape's border by construction, so where they coincide the click must grab the vertex
    /// (the finer element), deterministically, not fall to the id tiebreak.</summary>
    public const float ProxyVertexPickDepth = 0.9985f;

    /// <summary>The pick depth of a <b>bake product</b> (a boundary's baked segment collider) —
    /// deliberately a hair BELOW <see cref="ProxyBorderPickDepth"/>: a bake product is derived
    /// geometry that overlaps its authoring source (the boundary polyline picks at
    /// <see cref="ProxyBorderPickDepth"/>), so where they coincide the SOURCE wins and stays the
    /// edit surface. The product is still pickable (inspectable) but movement-refused — it
    /// regenerates from its source (see the boundary premise). Still above game sprites, so a
    /// segment reads as on-top chrome like every collider outline.</summary>
    public const float BakedProductPickDepth = 0.9975f;

    private readonly World _world;
    private readonly Camera? _camera;
    private readonly EntitySet _spriteSet;
    private readonly EntitySet _cursorSet;
    private readonly EntitySet _selectedSet;
    private readonly EntitySet _proxySet;
    private readonly EntitySet _boxColliderSet;
    private readonly EntitySet _convexColliderSet;
    private readonly EntitySet _boundarySet;
    private readonly EntitySet _cameraSet;
    private readonly EntitySet _buttonSet;
    private readonly EntitySet _gizmoStateSet;
    private int _nextEditorId;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Fired when a RIGHT-button press in <see cref="EditorToolMode.SelectTransform"/> HITS an
    /// entity in the viewport (UX2-D): the system selects the hit entity first (via
    /// <see cref="SelectExclusive"/>), then raises this so the overlay opens the entity context menu at
    /// the cursor. A right-click over empty space raises nothing and clears nothing (click-empty stays a
    /// LEFT-click behavior). Null (the default / tests without a menu) makes the right-click select-only.</summary>
    public Action<GameState>? ViewportContextMenuRequested { get; set; }

    // Per-frame pick state (no per-frame allocation in the hot path).
    private Vector2 _worldPoint;
    private Vector2 _virtualPoint;
    private bool _hasBest;
    private int _bestRank;
    private float _bestDepth;
    private int _bestId;
    private Entity _best;

    /// <param name="world">The world to pick in.</param>
    /// <param name="camera">The Main-pass camera, used to scale the proxy border pick tolerance by
    /// <c>1/Zoom</c> (constant on-screen grab width). Null (the pre-8b signature) falls back to a
    /// zoom of 1 — sprite picking is unaffected either way.</param>
    public SelectionSystem(World world, Camera? camera = null)
    {
        _world = world;
        _camera = camera;
        _spriteSet = world.GetEntities()
            .With<SpriteInfoComponent>().With<TransformComponent>()
            .With<DrawComponent>().With<VisibleComponent>().AsSet();
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
        _selectedSet = world.GetEntities().With<SelectedComponent>().AsSet();
        _proxySet = world.GetEntities()
            .With<GizmoProxyComponent>().With<TransformComponent>().With<DrawComponent>().AsSet();
        // Collider ENTITIES are spriteless — they border-pick on their world shape (the camera-entity
        // precedent). Queried by the SHAPE component (not ColliderTagComponent, which is only
        // auto-applied when a detection system is composed — absent in a selection-only editor / a
        // bare unit test), so a collider entity is always pickable.
        _boxColliderSet = world.GetEntities()
            .With<BoxColliderComponent>().With<TransformComponent>().AsSet();
        _convexColliderSet = world.GetEntities()
            .With<ConvexColliderComponent>().With<TransformComponent>().AsSet();
        _boundarySet = world.GetEntities()
            .With<BoundaryComponent>().With<TransformComponent>().AsSet();
        // The camera ENTITY is spriteless — it border-picks on its frustum world-rect (CM: an ordinary
        // scene entity now, not an editor rig; queried by CameraComponent).
        _cameraSet = world.GetEntities()
            .With<CameraComponent>().With<TransformComponent>().AsSet();
        // Menu buttons are SimpleButtonComponent meshes with NO SpriteInfoComponent, so they join the
        // pick as their own candidate source (like colliders / the camera). Queried by the component; the
        // Editor-target + EditorInfrastructureComponent gate in EvaluateButtonCandidates keeps the
        // editor's own chrome buttons out.
        _buttonSet = world.GetEntities()
            .With<SimpleButtonComponent>().With<TransformComponent>().AsSet();
        _gizmoStateSet = world.GetEntities().With<GizmoStateComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        // Edit-guarded: inert in Play.
        if (state.RunMode != RunMode.Edit) return;

        // Tool modality (island-authoring §S1): viewport presses belong to selection only in
        // SelectTransform. In Place (and the future brush modes) the placement system owns every
        // viewport press — no pick, no click-empty clear (a placement click must not deselect).
        if (ActiveToolMode() != EditorToolMode.SelectTransform) return;

        // Read the single cursor (the editor screen creates exactly one): capture the press edge +
        // points, then act OUTSIDE the iteration so component mutations don't disturb the set.
        var leftPress = false;
        var rightPress = false;
        var worldPoint = Vector2.Zero;
        var virtualPoint = Vector2.Zero;
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            // A press over the editor chrome / letterbox margins is not a scene click:
            // WorldPosition AND VirtualPosition are frozen at their last inside-the-viewport values
            // there, so picking (or clearing the selection) would act on a stale point.
            if (input.OutsideViewport) return;
            // Click-ownership: a press the gizmo claimed — it landed on the active tool's handle, or a
            // handle drag is in progress — is not a scene click either (see the class doc). Same-frame
            // read is safe: GizmoSystem writes the claim in the UPDATE pipeline; this runs at the end of
            // the DRAW pipeline.
            if (GizmoClaimedPress()) return;
            leftPress = input.LeftButtonPressed;
            rightPress = input.RightButtonPressed;
            worldPoint = input.WorldPosition;
            virtualPoint = input.VirtualPosition;
            break; // single cursor
        }

        if (leftPress)
        {
            // Left click: select the topmost candidate; a click on empty space clears (click-empty).
            if (TryPick(worldPoint, virtualPoint, out var hit))
            {
                ClearSelection();
                ResolveViewportSelection(hit).Set(new SelectedComponent());
            }
            else
            {
                ClearSelection();
            }
        }
        else if (rightPress)
        {
            // Right click (UX2-D): open the entity context menu, but ONLY on a hit — a right-click over
            // empty space opens no menu and clears NOTHING (click-empty stays a left-click behavior).
            // Select the hit first (keeping it if it was already selected), then raise the request.
            if (TryPick(worldPoint, virtualPoint, out var hit))
            {
                SelectExclusive(ResolveViewportSelection(hit));
                ViewportContextMenuRequested?.Invoke(state);
            }
        }
    }

    /// <summary>
    /// Runs the editor's topmost-pick against <paramref name="worldPoint"/> /
    /// <paramref name="virtualPoint"/> — the SAME candidate evaluation (sprites, collider/boundary
    /// proxies) + rank/depth/id ordering the left-click selection uses — and returns the winning entity.
    /// Exposed so the viewport RIGHT-click (this system) and the <c>menu:open viewport</c> op (the
    /// overlay) reuse ONE pick, never forking the topmost logic.
    /// </summary>
    public bool TryPick(Vector2 worldPoint, Vector2 virtualPoint, out Entity hit)
    {
        _worldPoint = worldPoint;
        _virtualPoint = virtualPoint;
        _hasBest = false;
        _bestRank = 0;
        _bestDepth = 0f;
        _bestId = 0;
        _best = default;

        foreach (var entity in _spriteSet.GetEntities())
            EvaluateSpriteCandidate(entity);
        EvaluateProxyCandidates();
        EvaluateColliderCandidates();
        EvaluateBoundaryCandidates();
        EvaluateCameraCandidate();
        EvaluateButtonCandidates();

        hit = _best;
        return _hasBest;
    }

    /// <summary>Single-selects <paramref name="target"/>: clears every OTHER selection tag and sets it
    /// on the target (a no-op re-affirm when it is already the selection, so a right-click on the
    /// already-selected entity keeps it). Shared by the viewport right-click, the panel row right-click,
    /// and the <c>menu:open</c> ops.</summary>
    public void SelectExclusive(Entity target)
    {
        if (!target.IsAlive) return;
        List<Entity>? toClear = null;
        foreach (var e in _selectedSet.GetEntities())
            if (!e.Equals(target))
                (toClear ??= new List<Entity>()).Add(e);
        if (toClear != null)
            foreach (var e in toClear)
                if (e.IsAlive && e.Has<SelectedComponent>())
                    e.Remove<SelectedComponent>();
        if (!target.Has<SelectedComponent>())
            target.Set(new SelectedComponent());
    }

    /// <summary>
    /// Unity's instance-pick model (PF-G): a VIEWPORT pick that lands on a prefab-owned CHILD resolves to
    /// the whole instance's editable <b>ROOT</b> (<see cref="PrefabGuards.InstanceRootOf"/>), so clicking
    /// anywhere on a placed instance selects — and thus moves / rotates / scales — the instance rather than
    /// its prefab-owned child (whose edits the PF-D guardrail refuses). A pick on a plain entity, an
    /// instance root itself, or a non-child candidate (a collider proxy, a boundary, the camera entity) is
    /// returned unchanged. The <b>Entities tree</b> deliberately does NOT route through here: it selects a
    /// child directly for inspection (edits still refused with the status hint). Shared by this system's
    /// left/right viewport press and the overlay's <c>menu:open viewport</c> op — the two viewport picks.
    /// </summary>
    public static Entity ResolveViewportSelection(Entity hit)
    {
        var root = PrefabGuards.InstanceRootOf(hit);
        return root == default ? hit : root;
    }

    private void EvaluateSpriteCandidate(in Entity entity)
    {
        // Assign a stable selection-owned tiebreak id the first time this candidate is seen.
        if (!entity.Has<EditorIdComponent>())
            entity.Set(new EditorIdComponent(_nextEditorId++));
        var id = entity.Get<EditorIdComponent>().Id;

        ref readonly var transform = ref entity.Get<TransformComponent>();
        ref readonly var sprite = ref entity.Get<SpriteInfoComponent>();

        // Target-aware branch: world-space (Main) candidates test the world point; screen-space
        // (UI/HUD/Scroll) candidates test the virtual point (their transforms are virtual coords).
        // The editor's own chrome (Editor target) is never a candidate.
        var rank = TargetRank(sprite.Target);
        if (rank < 0) return;
        var point = sprite.Target == RenderTargetID.Main ? _worldPoint : _virtualPoint;
        if (!SpriteHitTest.Contains(transform, sprite, point)) return;

        var depth = entity.Get<DrawComponent>().LayerDepth;
        if (Beats(rank, depth, id, _hasBest, _bestRank, _bestDepth, _bestId))
        {
            _hasBest = true;
            _bestRank = rank;
            _bestDepth = depth;
            _bestId = id;
            _best = entity;
        }
    }

    /// <summary>
    /// Folds the collider gizmo proxies (Wave 8b) into the current pick: each live-target proxy
    /// whose shape <b>border</b> lies under the world cursor competes through the same
    /// rank + depth + id rule as the sprite candidates (rank = Main; depth = the proxy's on-top
    /// overlay depth; id = the shared <see cref="EditorIdComponent"/> tiebreak).
    /// </summary>
    private void EvaluateProxyCandidates()
    {
        var invZoom = _camera != null && _camera.Zoom > 0f ? 1f / _camera.Zoom : 1f;
        var tolerance = ProxyBorderPickTolerancePixels * invZoom;
        var rank = TargetRank(RenderTargetID.Main); // proxies are world-space outlines on Main

        foreach (var proxy in _proxySet.GetEntities())
        {
            var binding = proxy.Get<GizmoProxyComponent>();
            if (!ProxyGeometry.TryGetWorldOutline(binding.Target, binding.Kind, binding.Index, out var outline)) continue;
            if (!ProxyGeometry.BorderContains(outline, _worldPoint, tolerance)) continue;

            if (!proxy.Has<EditorIdComponent>())
                proxy.Set(new EditorIdComponent(_nextEditorId++));
            var id = proxy.Get<EditorIdComponent>().Id;
            // Constants — see their docs (the visual depth is Editor-band); a vertex handle (and the
            // boundary thickness handle) outranks the shape/boundary border it rides near.
            var depth = binding.Kind is ProxyBindingKind.ConvexVertex or ProxyBindingKind.BoundaryThickness
                ? ProxyVertexPickDepth
                : ProxyBorderPickDepth;

            if (Beats(rank, depth, id, _hasBest, _bestRank, _bestDepth, _bestId))
            {
                _hasBest = true;
                _bestRank = rank;
                _bestDepth = depth;
                _bestId = id;
                _best = proxy;
            }
        }
    }

    /// <summary>
    /// Folds collider ENTITIES (colliders-as-entities) into the pick: a click within the border
    /// tolerance of a collider's world shape (box corners or convex world vertices) selects the
    /// collider entity itself — like the camera entity, a spriteless first-class entity (rank
    /// Main; depth <see cref="ProxyBorderPickDepth"/> — the same on-top rank the old collider proxy
    /// had; id the shared tiebreak). It is a <b>border-only</b> candidate — a collider covering a
    /// sprite never shadows the sprite (click the outline to grab the collider, click inside to pick
    /// the sprite). A <b>bake product</b> (a boundary's baked segment) picks at the lower
    /// <see cref="BakedProductPickDepth"/>, so its authoring source (the boundary polyline) wins
    /// where they overlap. The collider entity is then moved/scaled by the ordinary gizmo (a
    /// <c>TransformEditCommand</c> on its own transform) — a first-class entity, not a proxy;
    /// once selected, a convex collider's per-vertex grips (spawned by <c>ProxySyncSystem</c>) rank
    /// higher (<see cref="ProxyVertexPickDepth"/>) so a click near a vertex grabs the vertex.
    /// </summary>
    private void EvaluateColliderCandidates()
    {
        var invZoom = _camera != null && _camera.Zoom > 0f ? 1f / _camera.Zoom : 1f;
        var tolerance = ProxyBorderPickTolerancePixels * invZoom;
        foreach (var e in _boxColliderSet.GetEntities()) EvaluateColliderCandidate(e, tolerance);
        foreach (var e in _convexColliderSet.GetEntities()) EvaluateColliderCandidate(e, tolerance);
    }

    private void EvaluateColliderCandidate(Entity entity, float tolerance)
    {
        if (!entity.IsAlive) return;
        if (!ProxyGeometry.TryGetColliderWorldShape(entity, out var outline)) return;
        if (!ProxyGeometry.BorderContains(outline, _worldPoint, tolerance)) return;

        if (!entity.Has<EditorIdComponent>())
            entity.Set(new EditorIdComponent(_nextEditorId++));
        var id = entity.Get<EditorIdComponent>().Id;
        var rank = TargetRank(RenderTargetID.Main); // collider outlines are world-space on Main
        // A bake product picks below a normal collider + below its source boundary polyline.
        var depth = entity.Has<BakedProductComponent>() ? BakedProductPickDepth : ProxyBorderPickDepth;

        if (Beats(rank, depth, id, _hasBest, _bestRank, _bestDepth, _bestId))
        {
            _hasBest = true;
            _bestRank = rank;
            _bestDepth = depth;
            _bestId = id;
            _best = entity;
        }
    }

    /// <summary>
    /// Folds freeform boundary entities (island-authoring Slice 3) into the pick: a click within
    /// the border tolerance of a boundary's OPEN polyline selects the boundary entity itself (rank
    /// Main; depth <see cref="ProxyBorderPickDepth"/> — the same on-top rank as a proxy border; id
    /// the shared tiebreak). Selecting a boundary is what makes <c>ProxySyncSystem</c> spawn its
    /// per-vertex proxies (a boundary has no shape proxy to click through first — it IS its points),
    /// and those vertex proxies rank at the higher <see cref="ProxyVertexPickDepth"/>, so once the
    /// boundary is selected a click near a vertex grabs the vertex, a click on the line grabs the
    /// boundary.
    /// </summary>
    private void EvaluateBoundaryCandidates()
    {
        var invZoom = _camera != null && _camera.Zoom > 0f ? 1f / _camera.Zoom : 1f;
        var tolerance = ProxyBorderPickTolerancePixels * invZoom;
        var rank = TargetRank(RenderTargetID.Main);

        foreach (var boundary in _boundarySet.GetEntities())
        {
            if (!boundary.IsAlive) continue;
            var component = boundary.Get<BoundaryComponent>();
            if (component.Points == null || component.Points.Length < 2) continue;
            var worldPoly = Boundary.BoundaryGeometry.WorldPolyline(
                component.Points, boundary.Get<TransformComponent>().Position);
            if (!Boundary.BoundaryGeometry.PolylineContains(worldPoly, _worldPoint, tolerance)) continue;

            if (!boundary.Has<EditorIdComponent>())
                boundary.Set(new EditorIdComponent(_nextEditorId++));
            var id = boundary.Get<EditorIdComponent>().Id;

            if (Beats(rank, ProxyBorderPickDepth, id, _hasBest, _bestRank, _bestDepth, _bestId))
            {
                _hasBest = true;
                _bestRank = rank;
                _bestDepth = ProxyBorderPickDepth;
                _bestId = id;
                _best = boundary;
            }
        }
    }

    /// <summary>
    /// Folds the camera ENTITY (CM) into the pick: a click within the border tolerance of the scene
    /// camera's frustum world-rect selects the camera entity (rank Main; depth
    /// <see cref="ProxyBorderPickDepth"/> — the same on-top rank a proxy/boundary border has; id the
    /// shared tiebreak). The camera is an ordinary spriteless scene entity now, so it border-picks on its
    /// frustum exactly like a collider entity — a <b>border-only</b> candidate (the frustum's fill never
    /// shadows a sprite under it). Skipped when no camera is available (the frustum world-rect needs the
    /// view's virtual resolution). The camera is then moved/rotated by the ordinary gizmo (a
    /// <c>TransformEditCommand</c>) and Scale→Zoom, so it needs no <c>ProxyBindingKind</c>.
    /// </summary>
    private void EvaluateCameraCandidate()
    {
        if (_camera == null) return;
        var invZoom = _camera.Zoom > 0f ? 1f / _camera.Zoom : 1f;
        var tolerance = ProxyBorderPickTolerancePixels * invZoom;
        var rank = TargetRank(RenderTargetID.Main); // the frustum is a world-space outline on Main

        foreach (var camera in _cameraSet.GetEntities())
        {
            if (!camera.IsAlive) continue;
            var corners = CameraEntityGlyph.FrustumWorldCorners(
                camera.Get<TransformComponent>().WorldPosition, camera.Get<CameraComponent>().Zoom,
                _camera.LayoutWidth, _camera.LayoutHeight);
            if (!ProxyGeometry.BorderContains(corners, _worldPoint, tolerance)) continue;

            if (!camera.Has<EditorIdComponent>())
                camera.Set(new EditorIdComponent(_nextEditorId++));
            var id = camera.Get<EditorIdComponent>().Id;

            if (Beats(rank, ProxyBorderPickDepth, id, _hasBest, _bestRank, _bestDepth, _bestId))
            {
                _hasBest = true;
                _bestRank = rank;
                _bestDepth = ProxyBorderPickDepth;
                _bestId = id;
                _best = camera;
            }
        }
    }

    /// <summary>
    /// Folds menu buttons (Wave TB-B) into the pick: a <see cref="SimpleButtonComponent"/> mesh has no
    /// <see cref="SpriteInfoComponent"/>, so it never entered the sprite candidate set — the reason
    /// menu buttons read as "unclickable" in Edit. It competes through the SAME rank + depth + id rule
    /// as a sprite: rank = the button's <c>Target</c> composite rank, depth = its final draw
    /// <c>DrawComponent.LayerDepth</c> (ButtonMeshPrepSystem's baked value, treating unset as the 0.95
    /// default), tested with the button's own axis-aligned quad (world top-left origin + <c>Size</c> —
    /// the same rect <c>ButtonInteractionSystem</c> hover-tests) in the target's space (Main →
    /// <see cref="CursorInputComponent.WorldPosition"/>, UI/HUD → <c>VirtualPosition</c>). The editor's
    /// OWN toolbar / tab-strip / panel buttons are NEVER candidates — they live on the
    /// <see cref="RenderTargetID.Editor"/> target (rank &lt; 0) AND carry
    /// <see cref="EditorInfrastructureComponent"/>; both gates are checked so a stray editor button on a
    /// scene target still can't be scene-selected (the chrome rule).
    /// </summary>
    private void EvaluateButtonCandidates()
    {
        foreach (var entity in _buttonSet.GetEntities())
        {
            // The chrome rule (belt-and-suspenders): the editor's own buttons must never become
            // scene-pickable. Gate on the infrastructure tag AND the Editor target rank below.
            if (entity.Has<EditorInfrastructureComponent>()) continue;

            ref readonly var button = ref entity.Get<SimpleButtonComponent>();
            var rank = TargetRank(button.Target);
            if (rank < 0) continue; // Editor chrome target (or any non-scene target)

            var point = button.Target == RenderTargetID.Main ? _worldPoint : _virtualPoint;
            var origin = entity.Get<TransformComponent>().WorldPosition;
            // Axis-aligned quad from the world top-left origin + Size — identical to the button's
            // hover hit-test, so "click selects it" matches "hover highlights it".
            var bounds = new Rectangle((int)origin.X, (int)origin.Y, (int)button.Size.X, (int)button.Size.Y);
            if (!bounds.Contains(point)) continue;

            if (!entity.Has<EditorIdComponent>())
                entity.Set(new EditorIdComponent(_nextEditorId++));
            var id = entity.Get<EditorIdComponent>().Id;
            // Frontmost key = the button's baked draw depth; unset (no DrawComponent yet, or LayerDepth
            // 0) resolves to ButtonMeshPrepSystem's 0.95 default so a button ranks above the menu content.
            var depth = entity.Has<DrawComponent>() ? entity.Get<DrawComponent>().LayerDepth : 0f;
            if (depth <= 0f) depth = 0.95f;

            if (Beats(rank, depth, id, _hasBest, _bestRank, _bestDepth, _bestId))
            {
                _hasBest = true;
                _bestRank = rank;
                _bestDepth = depth;
                _bestId = id;
                _best = entity;
            }
        }
    }

    /// <summary>The active coarse tool mode (see <see cref="EditorToolMode"/>). No gizmo-state
    /// entity — e.g. a selection-only composition — means the default
    /// <see cref="EditorToolMode.SelectTransform"/>.</summary>
    private EditorToolMode ActiveToolMode()
    {
        foreach (var e in _gizmoStateSet.GetEntities())
            return e.Get<GizmoStateComponent>().Mode;
        return EditorToolMode.SelectTransform;
    }

    /// <summary>Whether the gizmo claimed this frame's left press (see
    /// <see cref="GizmoStateComponent.PressClaimed"/>). No gizmo-state entity — e.g. a
    /// selection-only composition — means no claim.</summary>
    private bool GizmoClaimedPress()
    {
        foreach (var e in _gizmoStateSet.GetEntities())
            return e.Get<GizmoStateComponent>().PressClaimed;
        return false;
    }

    private void ClearSelection()
    {
        List<Entity>? toClear = null;
        foreach (var e in _selectedSet.GetEntities())
            (toClear ??= new List<Entity>()).Add(e);
        if (toClear == null) return;
        foreach (var e in toClear)
            if (e.IsAlive && e.Has<SelectedComponent>())
                e.Remove<SelectedComponent>();
    }

    /// <summary>
    /// The cross-target selection rank, mirroring the final composite's stacking order
    /// (<c>FinalDrawSystem</c> layers Main, then UI, then HUD, with Scroll — the screen-space
    /// overlay — on top): a higher rank is composited above and wins the pick. <c>Editor</c> (the
    /// chrome) returns -1 = never a candidate.
    /// </summary>
    public static int TargetRank(RenderTargetID target) => target switch
    {
        RenderTargetID.Main => 0,
        RenderTargetID.UI => 1,
        RenderTargetID.HUD => 2,
        RenderTargetID.Scroll => 3,
        _ => -1, // Editor chrome (and any future non-scene target) is not selectable
    };

    /// <summary>
    /// Pure same-target tiebreak rule (kept for the single-target ordering contract): does a
    /// candidate at (<paramref name="depth"/>, <paramref name="id"/>) beat the current best?
    /// Frontmost = MAX final <c>LayerDepth</c>; on an exact-depth tie, MAX
    /// <see cref="EditorIdComponent.Id"/> (later-seen / created-later entity wins, matching the
    /// renderer's "drawn last is on top"). Exposed for direct unit testing of the ordering.
    /// </summary>
    public static bool PickTopmost(float depth, int id, bool hasBest, float bestDepth, int bestId)
        => Beats(0, depth, id, hasBest, 0, bestDepth, bestId);

    /// <summary>
    /// Pure cross-target pick rule: composite rank first (<see cref="TargetRank"/> — UI/HUD beat
    /// Main because they composite above it), then MAX final depth, then MAX id. Exposed for
    /// direct unit testing of the cross-target ordering.
    /// </summary>
    public static bool PickTopmost(int targetRank, float depth, int id,
        bool hasBest, int bestTargetRank, float bestDepth, int bestId)
        => Beats(targetRank, depth, id, hasBest, bestTargetRank, bestDepth, bestId);

    private static bool Beats(int rank, float depth, int id,
        bool hasBest, int bestRank, float bestDepth, int bestId)
    {
        if (!hasBest) return true;
        if (rank != bestRank) return rank > bestRank; // composited-above target wins
        if (depth > bestDepth) return true;
        if (depth < bestDepth) return false;
        return id > bestId; // exact-depth tie → larger id (seen later / drawn last) wins
    }

    public void Dispose()
    {
        _spriteSet.Dispose();
        _cursorSet.Dispose();
        _selectedSet.Dispose();
        _proxySet.Dispose();
        _boxColliderSet.Dispose();
        _convexColliderSet.Dispose();
        _boundarySet.Dispose();
        _cameraSet.Dispose();
        _buttonSet.Dispose();
        _gizmoStateSet.Dispose();
    }
}
