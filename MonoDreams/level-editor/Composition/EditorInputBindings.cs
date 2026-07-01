#nullable enable
using System;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// The game-specific input surface the <see cref="EditorOverlay"/> needs: five just-pressed
/// predicates over <see cref="GameState"/>. The overlay (and every editor system behind it) stays
/// game-agnostic — it never references a game's input enum; the game's screen wires its own
/// actions here (e.g. <c>_ =&gt; InputState.Editor.JustPressed()</c>). Pure data holder, per the
/// same pattern as <c>EditorModeToggleSystem</c> / <c>EditorCommandSystem</c> taking predicates.
/// </summary>
public sealed class EditorInputBindings(
    Func<GameState, bool> toggleEditRequested,
    Func<GameState, bool> deleteRequested,
    Func<GameState, bool> undoRequested,
    Func<GameState, bool> redoRequested,
    Func<GameState, bool> frameRequested)
{
    /// <summary>Toggles Play ↔ Edit (the editor screen wires F1).</summary>
    public Func<GameState, bool> ToggleEditRequested { get; } =
        toggleEditRequested ?? throw new ArgumentNullException(nameof(toggleEditRequested));

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
}
