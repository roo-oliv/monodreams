#nullable enable
using System;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// The game-specific input surface the <see cref="EditorOverlay"/> needs: four just-pressed
/// predicates over <see cref="GameState"/> (plus an optional tool-cancel — the palette's Escape). The overlay (and every editor system behind it) stays
/// game-agnostic — it never references a game's input enum; the game's screen wires its own
/// actions here (e.g. <c>_ =&gt; InputState.Delete.JustPressed()</c>). Pure data holder, per the
/// same pattern as <c>EditorCommandSystem</c> taking predicates.
///
/// <para>There is deliberately NO edit-mode toggle key here: under the transport model the editor
/// is always visible when composed, and the run state is driven by the toolbar's Play/Pause /
/// Restart transport buttons (or the headless transport ops) through
/// <see cref="EditorTransport"/>.</para>
/// </summary>
public sealed class EditorInputBindings(
    Func<GameState, bool> deleteRequested,
    Func<GameState, bool> undoRequested,
    Func<GameState, bool> redoRequested,
    Func<GameState, bool> frameRequested,
    Func<GameState, bool>? cancelRequested = null)
{
    /// <summary>Deletes the selected entity (reversible, via the shared history).</summary>
    public Func<GameState, bool> DeleteRequested { get; } =
        deleteRequested ?? throw new ArgumentNullException(nameof(deleteRequested));

    /// <summary>Undo (the shared bounded history).</summary>
    public Func<GameState, bool> UndoRequested { get; } =
        undoRequested ?? throw new ArgumentNullException(nameof(undoRequested));

    /// <summary>Redo (the shared bounded history).</summary>
    public Func<GameState, bool> RedoRequested { get; } =
        redoRequested ?? throw new ArgumentNullException(nameof(redoRequested));

    /// <summary>Frames the editor camera on all renderable content (centre + zoom-fit).</summary>
    public Func<GameState, bool> FrameRequested { get; } =
        frameRequested ?? throw new ArgumentNullException(nameof(frameRequested));

    /// <summary>Cancels the current tool arm — the palette's Escape (right-click always disarms
    /// too). Optional (default null = no keyboard cancel); additive, so pre-palette call sites
    /// compile unchanged.</summary>
    public Func<GameState, bool>? CancelRequested { get; } = cancelRequested;
}
