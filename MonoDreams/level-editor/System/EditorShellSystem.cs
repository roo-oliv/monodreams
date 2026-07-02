#nullable enable
using System;
using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.UI;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// Keeps the Blender-style editor shell in sync with the run mode (Wave 7). Each frame it makes
/// three things track <see cref="GameState.RunMode"/>:
///
/// <list type="number">
///   <item><b>The viewport inset.</b> In Edit it applies
///   <see cref="EditorChromeLayout.ViewportInset"/> to the <see cref="ViewportManager"/>
///   (<c>SetViewportInset</c>), shrinking the aspect-fit game viewport into the center of the
///   window with the chrome margins reserved around it; in Play it clears the inset — the
///   full-window letterbox, byte-identical to a screen without the editor. Because the
///   ViewportManager is the single source of truth, BOTH the FinalDraw compositing AND
///   <c>ScaleMouseToVirtualCoordinates</c> follow the same rectangle: clicks in the inset game
///   viewport keep mapping to correct world positions with no extra math; clicks in the margins
///   map outside (null) and are consumed by the chrome in screen space.</item>
///   <item><b>Chrome layout.</b> The native-resolution chrome (panels + toolbar) lays out in
///   physical pixels, so the system relayouts it whenever the window size differs from the last
///   laid-out size while editing (covers live window resize).</item>
///   <item><b>The cursor.</b> In Edit the OS cursor interacts with the chrome (screen space), so
///   the host-supplied setter shows it and the in-game HUD cursor sprite is hidden (its
///   controller's <c>IsVisible</c> plus clearing the stale draw texture) — exactly one visible
///   pointer, the high-res OS one, which also drives the game-viewport picking since the world
///   mapping derives from the same <c>ScreenPosition</c>. In Play both revert (game cursor
///   sprite, OS cursor hidden). Applied on mode <i>transitions</i> only, so nothing else that
///   drives cursor visibility is fought per frame.</item>
/// </list>
///
/// <para>Reading the mode every frame (not just a toggle event) makes the shell follow every way
/// the mode can flip: F1 (<c>EditorModeToggleSystem</c>), boot-in-Edit (<c>--editor</c>), or a
/// headless <c>ToggleMode</c> op. <see cref="Dispose"/> clears the inset and re-hides the OS
/// cursor — the ViewportManager and the host Game outlive the screen, so a screen swap while
/// editing must not leak the shell onto the next screen.</para>
/// </summary>
public sealed class EditorShellSystem : ISystem<GameState>
{
    private readonly ViewportManager _viewportManager;
    private readonly EditorChromeBuilder _chrome;
    private readonly Action<bool>? _setOsCursorVisible;
    private readonly EntitySet _cursorSet;

    private bool _initialized;
    private bool _editingApplied;

    public bool IsEnabled { get; set; } = true;

    public EditorShellSystem(
        World world,
        ViewportManager viewportManager,
        EditorChromeBuilder chrome,
        Action<bool>? setOsCursorVisible = null)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        _viewportManager = viewportManager ?? throw new ArgumentNullException(nameof(viewportManager));
        _chrome = chrome ?? throw new ArgumentNullException(nameof(chrome));
        _setOsCursorVisible = setOsCursorVisible;
        _cursorSet = world.GetEntities().With<CursorControllerComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        var editing = state.RunMode == RunMode.Edit;

        if (editing)
        {
            // Native chrome tracks the window: relayout when the size changed (or on entry).
            if (_chrome.LaidOutWidth != _viewportManager.ScreenWidth ||
                _chrome.LaidOutHeight != _viewportManager.ScreenHeight)
                _chrome.Relayout(_viewportManager.ScreenWidth, _viewportManager.ScreenHeight);

            var (left, top, right, bottom) = EditorChromeLayout.ViewportInset;
            _viewportManager.SetViewportInset(left, top, right, bottom); // no-op when unchanged
        }
        else
        {
            _viewportManager.ClearViewportInset(); // no-op when already clear
        }

        if (!_initialized || editing != _editingApplied)
        {
            ApplyCursorMode(editing);
            _initialized = true;
            _editingApplied = editing;
        }
    }

    private void ApplyCursorMode(bool editing)
    {
        // One pointer at a time: the OS cursor while editing (it must reach the chrome margins,
        // where the game cursor cannot go), the game cursor sprite while playing.
        _setOsCursorVisible?.Invoke(editing);

        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref var controller = ref cursor.Get<CursorControllerComponent>();
            controller.IsVisible = !editing;
            if (editing && cursor.Has<DrawComponent>())
            {
                // CursorDrawPrepSystem skips invisible cursors but leaves the last texture on the
                // DrawComponent — clear it so the sprite disappears rather than freezing in place.
                ref var draw = ref cursor.Get<DrawComponent>();
                draw.Texture = null;
            }
        }
    }

    public void Dispose()
    {
        // The ViewportManager + host Game outlive this screen: never leak the editor inset or a
        // visible OS cursor onto the next screen.
        _viewportManager.ClearViewportInset();
        _setOsCursorVisible?.Invoke(false);
        _cursorSet.Dispose();
    }
}
