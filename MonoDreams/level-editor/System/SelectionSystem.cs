#nullable enable
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Selection;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// Click-to-select picking for the editor. On a left-button press in
/// <see cref="RunMode.Edit"/> it hit-tests the cursor against every rendered sprite candidate and
/// selects the <b>topmost</b> — the one the renderer draws frontmost — clearing any prior
/// selection. A click on empty space clears the selection.
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
/// </summary>
[With(typeof(SpriteInfoComponent), typeof(TransformComponent), typeof(DrawComponent), typeof(VisibleComponent))]
public sealed class SelectionSystem : AEntitySetSystem<GameState>
{
    private readonly World _world;
    private readonly EntitySet _cursorSet;
    private readonly EntitySet _selectedSet;
    private int _nextEditorId;

    // Per-frame pick state (no per-frame allocation in the hot path).
    private bool _picking;
    private Vector2 _worldPoint;
    private Vector2 _virtualPoint;
    private bool _hasBest;
    private int _bestRank;
    private float _bestDepth;
    private int _bestId;
    private Entity _best;

    public SelectionSystem(World world)
        : base(world.GetEntities()
            .With<SpriteInfoComponent>().With<TransformComponent>()
            .With<DrawComponent>().With<VisibleComponent>().AsSet())
    {
        _world = world;
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
        _selectedSet = world.GetEntities().With<SelectedComponent>().AsSet();
    }

    protected override void PreUpdate(GameState state)
    {
        _picking = false;
        _hasBest = false;

        // Edit-guarded: inert in Play.
        if (state.RunMode != RunMode.Edit) return;

        // Read the single cursor (the editor screen creates exactly one).
        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            if (!input.LeftButtonPressed) return; // only act on the press edge
            // A press over the editor chrome / letterbox margins is not a scene click:
            // WorldPosition AND VirtualPosition are frozen at their last inside-the-viewport
            // values there, so picking (or clearing the selection) would act on a stale point.
            if (input.OutsideViewport) return;
            _picking = true;
            _worldPoint = input.WorldPosition;
            _virtualPoint = input.VirtualPosition;
            return;
        }
    }

    protected override void Update(GameState state, in Entity entity)
    {
        if (!_picking) return;

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

    protected override void PostUpdate(GameState state)
    {
        if (!_picking) return;

        // Clear the previous selection (single-select). Materialize first — mutating components
        // while iterating an EntitySet is unsafe.
        ClearSelection();

        if (_hasBest)
            _best.Set(new SelectedComponent());
        // else: click on empty space → selection stays cleared.
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

    public override void Dispose()
    {
        _cursorSet.Dispose();
        _selectedSet.Dispose();
        base.Dispose();
    }
}
