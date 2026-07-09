#nullable enable
namespace MonoDreams.LevelEditor.Input;

/// <summary>
/// The single context predicate every editor shortcut checks (UX3-E design §4). An editing shortcut
/// fires ONLY when the cursor is over the game viewport, no modal (dialog or context menu) owns input,
/// and the transport is Paused (<c>RunMode.Edit</c>). Sharing one predicate is what keeps a chord from
/// firing while the designer types in a dialog field or hovers a panel — the SAME suppression game keys
/// get (the screen ORs <c>Dialog.IsOpen || Menu.IsOpen</c> into the host keyboard's
/// <c>ShouldSuppressInput</c>; this predicate mirrors it for the shortcut path).
///
/// <para>Pure value type: the system fills the four booleans from the cursor / dialog / menu / run mode
/// each frame, and hand-built instances test the gate directly.</para>
/// </summary>
public readonly struct ViewportShortcutContext
{
    /// <summary>The cursor is inside the game viewport (<c>!CursorInputComponent.OutsideViewport</c>) —
    /// false over the chrome margins/panels or when there is no cursor.</summary>
    public bool CursorOverViewport { get; init; }

    /// <summary>A modal editor dialog owns input (<c>EditorDialogSystem.IsOpen</c>).</summary>
    public bool DialogOpen { get; init; }

    /// <summary>A context menu owns input (<c>EditorContextMenuSystem.IsOpen</c>).</summary>
    public bool MenuOpen { get; init; }

    /// <summary>The transport is Paused — <c>RunMode.Edit</c> (editing shortcuts are inert while
    /// Playing; a viewport click/keystroke belongs to the game then).</summary>
    public bool Editing { get; init; }

    /// <summary>Whether an EDITING shortcut may fire: over the viewport, no modal open, Paused.</summary>
    public bool AllowsEditing => CursorOverViewport && !DialogOpen && !MenuOpen && Editing;
}
