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
/// <see cref="RunMode.Edit"/> it hit-tests the cursor's <see cref="CursorInputComponent.WorldPosition"/>
/// against every rendered sprite candidate and selects the <b>topmost</b> — the one the renderer
/// draws frontmost — clearing any prior selection. A click on empty space clears the selection.
///
/// <para><b>Edit-guarded, registered RunNormally.</b> The system is pre-registered in both modes
/// (per the editor-screen contract) but no-ops in <see cref="RunMode.Play"/>, so it is inert until
/// the designer enters editing. It must be ordered <b>after</b> the draw prep + <c>YSortSystem</c>
/// each frame, because it reads the <b>final</b> post-Y-sort <c>DrawComponent.LayerDepth</c> as the
/// "frontmost" key (mirroring <c>MasterRenderSystem</c>, which sorts on that same final depth).</para>
///
/// <para><b>Topmost = MAX final LayerDepth, selection-owned tiebreak.</b> The renderer's tiebreak
/// for an exact-depth tie is its private per-frame insertion index, which selection cannot observe.
/// Instead the system assigns each candidate a stable <see cref="EditorIdComponent"/> the first time
/// it sees it (a monotonic counter = first-seen / creation order) and breaks exact-depth ties by MAX
/// id — the later-seen entity, which an undisturbed scene renders last. This is deterministic and
/// reproducible, unlike the renderer's index. See <see cref="PickTopmost"/>.</para>
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
    private bool _hasBest;
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
            // A press over the editor chrome / letterbox margins is not a world click:
            // WorldPosition is frozen at its last inside-the-viewport value there, so picking
            // (or clearing the selection) from it would act on a stale point.
            if (input.OutsideViewport) return;
            _picking = true;
            _worldPoint = input.WorldPosition;
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
        if (!SpriteHitTest.Contains(transform, sprite, _worldPoint)) return;

        var depth = entity.Get<DrawComponent>().LayerDepth;
        if (Beats(depth, id, _hasBest, _bestDepth, _bestId))
        {
            _hasBest = true;
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
    /// Pure tiebreak rule: does a candidate at (<paramref name="depth"/>, <paramref name="id"/>) beat
    /// the current best? Frontmost = MAX final <c>LayerDepth</c>; on an exact-depth tie, MAX
    /// <see cref="EditorIdComponent.Id"/> (later-seen / created-later entity wins, matching the
    /// renderer's "drawn last is on top"). Exposed for direct unit testing of the ordering.
    /// </summary>
    public static bool PickTopmost(float depth, int id, bool hasBest, float bestDepth, int bestId)
        => Beats(depth, id, hasBest, bestDepth, bestId);

    private static bool Beats(float depth, int id, bool hasBest, float bestDepth, int bestId)
    {
        if (!hasBest) return true;
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
