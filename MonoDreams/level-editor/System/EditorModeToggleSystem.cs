#nullable enable
using System;
using DefaultEcs.System;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// Flips <see cref="GameState.RunMode"/> between <see cref="RunMode.Play"/> and
/// <see cref="RunMode.Edit"/> when the supplied <c>toggleRequested</c> predicate fires (e.g. the
/// editor screen wires it to an "Editor" key's just-pressed edge). The toggle happens <b>in place</b>,
/// in the same world — no screen swap, no <c>Dispose</c>/<c>Load</c> — so all world state is preserved
/// across the mode change. The gated game systems then run-or-freeze per their policy on the next
/// frame; the editor systems (pre-registered, Edit-guarded) wake up.
///
/// <para>Game-agnostic by construction: it takes a predicate over <see cref="GameState"/> rather than
/// a concrete input action, so <c>level-editor</c> need not depend on a game's input enum. It runs
/// <see cref="EditTimeBehavior.RunNormally"/> (the toggle must work in both modes — you enter Edit
/// from Play and leave it from Edit). The screen registers it ungated (RunNormally).</para>
/// </summary>
public sealed class EditorModeToggleSystem : ISystem<GameState>
{
    private readonly Func<GameState, bool> _toggleRequested;

    public bool IsEnabled { get; set; } = true;

    public EditorModeToggleSystem(Func<GameState, bool> toggleRequested)
        => _toggleRequested = toggleRequested ?? throw new ArgumentNullException(nameof(toggleRequested));

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        if (!_toggleRequested(state)) return;
        state.RunMode = state.RunMode == RunMode.Edit ? RunMode.Play : RunMode.Edit;
        Logger.Info($"[level-editor] Run mode toggled to {state.RunMode}.");
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
