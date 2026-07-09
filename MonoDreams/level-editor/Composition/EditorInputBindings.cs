#nullable enable
using System;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// The game-specific input surface the <see cref="EditorOverlay"/> needs for its <b>tool-contextual</b>
/// keys — the ones whose context is a tool being armed/laying, not the global editor shortcut gate: the
/// palette/boundary Escape (<see cref="CancelRequested"/>), the boundary commit
/// (<see cref="CommitRequested"/>), the palette ghost-rotate (<see cref="RotateCwRequested"/> /
/// <see cref="RotateCcwRequested"/>), and the optional within-band order nudges
/// (<see cref="OrderForwardRequested"/> / <see cref="OrderBackRequested"/>). All optional — a screen
/// wires only the tool keys it wants; the overlay stays game-agnostic (it never references a game's
/// input enum; the game's screen wires <c>_ =&gt; InputState.X.JustPressed()</c>).
///
/// <para><b>The GLOBAL editor shortcuts are NOT here (UX3-E).</b> Delete, frame-scene, undo, and redo —
/// plus <c>Shift+A</c> (Add menu) — were consolidated into the ONE <c>EditorShortcuts</c> chord table,
/// read by <c>EditorShortcutSystem</c> through the raw keyboard. Those are editor-standard (Blender
/// parity), not a game's remappable keys, so they no longer flow through this game-supplied surface. The
/// pre-existing bare <c>Z</c>/<c>Y</c> undo/redo were removed (bare keys are reserved for tools).</para>
///
/// <para>There is deliberately NO edit-mode toggle key here: under the transport model the editor
/// is always visible when composed, and the run state is driven by the toolbar's Play/Pause /
/// Restart transport buttons (or the headless transport ops) through
/// <see cref="EditorTransport"/>.</para>
/// </summary>
public sealed class EditorInputBindings(
    Func<GameState, bool>? cancelRequested = null,
    Func<GameState, bool>? orderForwardRequested = null,
    Func<GameState, bool>? orderBackRequested = null,
    Func<GameState, bool>? commitRequested = null,
    Func<GameState, bool>? rotateCwRequested = null,
    Func<GameState, bool>? rotateCcwRequested = null)
{
    /// <summary>Cancels the current tool arm — the palette's Escape (right-click always disarms
    /// too). Optional (default null = no keyboard cancel); additive, so pre-palette call sites
    /// compile unchanged.</summary>
    public Func<GameState, bool>? CancelRequested { get; } = cancelRequested;

    /// <summary>Keyboard nudge for the within-band Bring forward ordering action (e.g. PageUp).
    /// Optional; the toolbar button always works.</summary>
    public Func<GameState, bool>? OrderForwardRequested { get; } = orderForwardRequested;

    /// <summary>Keyboard nudge for Send back (e.g. PageDown). Optional.</summary>
    public Func<GameState, bool>? OrderBackRequested { get; } = orderBackRequested;

    /// <summary>Commits the in-progress boundary lay — the boundary tool's Enter (a double-click
    /// always commits too). Optional; the toolbar/headless <c>boundary:commit</c> also works.</summary>
    public Func<GameState, bool>? CommitRequested { get; } = commitRequested;

    /// <summary>Rotates the armed palette ghost clockwise (e.g. E) before stamping — road pieces /
    /// props land oriented (Slice 4). Optional; the headless <c>ghost:cw</c> op also works.</summary>
    public Func<GameState, bool>? RotateCwRequested { get; } = rotateCwRequested;

    /// <summary>Rotates the armed palette ghost counter-clockwise (e.g. Q). Optional; the headless
    /// <c>ghost:ccw</c> op also works.</summary>
    public Func<GameState, bool>? RotateCcwRequested { get; } = rotateCcwRequested;
}
