#nullable enable
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Proxy;
using MonoDreams.LevelEditor.Selection;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// Click-to-select picking for the editor. On a left-button press in
/// <see cref="RunMode.Edit"/> it hit-tests the cursor against every rendered sprite candidate and
/// selects the <b>topmost</b> — the one the renderer draws frontmost — clearing any prior
/// selection. A click on empty space clears the selection.
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
/// <para><b>Gizmo proxies join the SAME pick (Wave 8b), as border-only candidates.</b> Collider
/// proxy entities (<see cref="GizmoProxyComponent"/>) carry no <c>SpriteInfoComponent</c>, so they
/// are folded into the pick as a second candidate source with the SAME rank + depth + id ordering
/// (never a second pick path): rank = Main (they are world-space outlines), depth = their
/// <c>DrawComponent.LayerDepth</c> (drawn on top of game sprites, so they win where they visibly
/// overlap), id = the same <see cref="EditorIdComponent"/> tiebreak. Their hit-test is the shape's
/// <b>border</b> within a <c>1/Camera.Zoom</c>-scaled tolerance — never the fill — so a collider
/// that covers its entity's sprite doesn't make the sprite unselectable: click the outline to grab
/// the proxy, click inside to pick the entity.</para>
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

    private readonly World _world;
    private readonly Camera? _camera;
    private readonly EntitySet _spriteSet;
    private readonly EntitySet _cursorSet;
    private readonly EntitySet _selectedSet;
    private readonly EntitySet _proxySet;
    private readonly EntitySet _gizmoStateSet;
    private int _nextEditorId;

    public bool IsEnabled { get; set; } = true;

    // Per-frame pick state (no per-frame allocation in the hot path).
    private bool _picking;
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
        _gizmoStateSet = world.GetEntities().With<GizmoStateComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        _picking = false;
        _hasBest = false;

        // Edit-guarded: inert in Play.
        if (state.RunMode != RunMode.Edit) return;

        ArmPickFromCursor();
        if (!_picking) return;

        // Sprite candidates first, then the sprite-less proxy candidates, through ONE ordering.
        foreach (var entity in _spriteSet.GetEntities())
            EvaluateSpriteCandidate(entity);
        EvaluateProxyCandidates();

        // Clear the previous selection (single-select). Materialize first — mutating components
        // while iterating an EntitySet is unsafe.
        ClearSelection();

        if (_hasBest)
            _best.Set(new SelectedComponent());
        // else: click on empty space → selection stays cleared.
    }

    private void ArmPickFromCursor()
    {
        // Read the single cursor (the editor screen creates exactly one).
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            if (!input.LeftButtonPressed) return; // only act on the press edge
            // A press over the editor chrome / letterbox margins is not a scene click:
            // WorldPosition AND VirtualPosition are frozen at their last inside-the-viewport
            // values there, so picking (or clearing the selection) would act on a stale point.
            if (input.OutsideViewport) return;
            // Click-ownership: a press the gizmo claimed — it landed on the active tool's handle,
            // or a handle drag is in progress — is not a scene click either. Rotate/scale handles
            // (and a collider proxy's centre move-handle) routinely lie OUTSIDE the selected
            // sprite's bounds; processing that press here would read as click-empty and clear the
            // selection (or re-pick an overlapped sprite) in the very frame the drag began,
            // killing the drag. Same-frame read is safe: GizmoSystem writes the claim in the
            // UPDATE pipeline; this system runs at the end of the DRAW pipeline.
            if (GizmoClaimedPress()) return;
            _picking = true;
            _worldPoint = input.WorldPosition;
            _virtualPoint = input.VirtualPosition;
            return;
        }
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
            if (!ProxyGeometry.TryGetWorldOutline(binding.Target, binding.Kind, out var outline)) continue;
            if (!ProxyGeometry.BorderContains(outline, _worldPoint, tolerance)) continue;

            if (!proxy.Has<EditorIdComponent>())
                proxy.Set(new EditorIdComponent(_nextEditorId++));
            var id = proxy.Get<EditorIdComponent>().Id;
            var depth = proxy.Get<DrawComponent>().LayerDepth;

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
        _gizmoStateSet.Dispose();
    }
}
