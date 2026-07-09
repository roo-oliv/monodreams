#nullable enable
using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component.Cursor;
using MonoDreams.Input;
using MonoDreams.LevelEditor.Input;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The editor's keyboard-shortcut owner (UX3-E design §4): each frame it advances a
/// <see cref="KeyChordTracker"/> over the injectable keyboard seam, checks the shared
/// <see cref="ViewportShortcutContext"/> gate, and dispatches the matching <see cref="EditorShortcuts"/>
/// action through a callback the overlay maps to the SAME shared editor instances (history / command
/// system / camera-nav frame / menu coordinator) — never a second path. This is the ONE place editor
/// chords are read; the pre-existing bare <c>Z</c>/<c>Y</c> undo/redo and the scattered
/// delete/frame keyboard predicates were consolidated here.
///
/// <para><b>Weave.</b> Register it with the input-owner block, immediately AFTER <c>editor.dialog</c> and
/// <c>editor.contextMenu</c> so modality wins — when a dialog/menu is open the context gate
/// (<c>DialogOpen</c>/<c>MenuOpen</c>) blocks every shortcut, exactly the suppression the host keyboard
/// gets. Registered <c>RunNormally</c>; the gate's <c>Editing</c> flag makes it inert while Playing, so
/// no extra Edit-guard wrapper is needed.</para>
///
/// <para><b>Headless.</b> The tracker always advances (so press edges never leak across a
/// context change), but in a headless run the keyboard seam reports no keys, so nothing fires — the
/// action channel (ops) drives editing headlessly, per the chord replay caveat. The tracker's platform
/// flag (<c>commandIsMeta</c>) is injected; this system never reads the OS.</para>
/// </summary>
public sealed class EditorShortcutSystem : ISystem<GameState>
{
    private readonly EditorShortcuts _shortcuts;
    private readonly KeyChordTracker _tracker;
    private readonly Action<EditorShortcutAction, GameState> _dispatch;
    private readonly Func<bool> _dialogOpen;
    private readonly Func<bool> _menuOpen;
    private readonly EntitySet _cursorSet;

    public bool IsEnabled { get; set; } = true;

    /// <param name="world">The screen's world (for the cursor query the context gate reads).</param>
    /// <param name="shortcuts">The ONE chord → action table (<see cref="EditorShortcuts"/>).</param>
    /// <param name="dispatch">Maps a matched action to the shared editor instance (the overlay's
    /// callback). Called only when the context gate allows.</param>
    /// <param name="dialogOpen">Whether a modal dialog owns input (<c>EditorDialogSystem.IsOpen</c>).</param>
    /// <param name="menuOpen">Whether a context menu owns input (<c>EditorContextMenuSystem.IsOpen</c>).</param>
    /// <param name="commandIsMeta">Resolve <c>PlatformCommand</c> to ⌘/Meta (macOS) — injected, so the
    /// module reads no OS state.</param>
    /// <param name="getKeyboardState">The keyboard seam (default <see cref="Keyboard.GetState"/>).</param>
    public EditorShortcutSystem(
        World world,
        EditorShortcuts shortcuts,
        Action<EditorShortcutAction, GameState> dispatch,
        Func<bool> dialogOpen,
        Func<bool> menuOpen,
        bool commandIsMeta,
        Func<KeyboardState>? getKeyboardState = null)
    {
        _shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        _dialogOpen = dialogOpen ?? throw new ArgumentNullException(nameof(dialogOpen));
        _menuOpen = menuOpen ?? throw new ArgumentNullException(nameof(menuOpen));
        _tracker = new KeyChordTracker(commandIsMeta, getKeyboardState);
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        // Advance EVERY frame so press edges track correctly even when the gate blocks dispatch — a
        // press consumed while over a panel (or Playing) is never carried into a later allowed frame.
        _tracker.Update();

        var context = new ViewportShortcutContext
        {
            CursorOverViewport = CursorOverViewport(),
            DialogOpen = _dialogOpen(),
            MenuOpen = _menuOpen(),
            Editing = state.RunMode == RunMode.Edit,
        };
        if (!context.AllowsEditing) return;

        if (_shortcuts.Match(_tracker) is { } action) _dispatch(action, state);
    }

    private bool CursorOverViewport()
    {
        foreach (var cursor in _cursorSet.GetEntities())
            return !cursor.Get<CursorInputComponent>().OutsideViewport;
        return false; // no cursor → not over the viewport → no editing shortcut fires
    }

    public void Dispose()
    {
        _cursorSet.Dispose();
        GC.SuppressFinalize(this);
    }
}
