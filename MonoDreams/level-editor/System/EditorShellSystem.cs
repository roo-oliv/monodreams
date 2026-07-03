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
/// Keeps the Blender-style editor shell applied while the editor is composed (Wave 7; transport
/// model since the F1-toggle retirement): under the editor run configuration the shell is
/// <b>constant</b> — it does not collapse when the transport is Playing, because the editor is
/// always visible and the game simply runs inside the inset viewport. Each frame it maintains:
///
/// <list type="number">
///   <item><b>The viewport inset.</b> It applies <see cref="EditorChromeLayout.ViewportInset"/> to
///   the <see cref="ViewportManager"/> (<c>SetViewportInset</c>), shrinking the aspect-fit game
///   viewport into the center of the window with the chrome margins reserved around it. Because
///   the ViewportManager is the single source of truth, BOTH the FinalDraw compositing AND
///   <c>ScaleMouseToVirtualCoordinates</c> follow the same rectangle: clicks in the inset game
///   viewport keep mapping to correct world positions with no extra math; clicks in the margins
///   map outside (null) and are consumed by the chrome in screen space.</item>
///   <item><b>Chrome layout.</b> The native-resolution chrome (panels + toolbar) lays out in
///   physical pixels, so the system relayouts it whenever the window size differs from the last
///   laid-out size (covers live window resize).</item>
///   <item><b>The cursor.</b> The OS cursor is the one visible pointer (it must reach the chrome
///   margins, where the game cursor's position mapping nulls out) and the in-game HUD cursor
///   sprite is hidden (its controller's <c>IsVisible</c> plus clearing the stale draw texture) —
///   in both transport states, since the chrome is always interactive. Applied once (the shell
///   never flips back while composed), so nothing else driving cursor visibility is fought per
///   frame.</item>
/// </list>
///
/// <para>A screen without the editor composed never constructs this system, and its viewport stays
/// the historical full-window letterbox — byte-identical. <see cref="Dispose"/> clears the inset
/// and re-hides the OS cursor — the ViewportManager and the host Game outlive the screen, so a
/// screen swap must not leak the shell onto the next screen.</para>
/// </summary>
public sealed class EditorShellSystem : ISystem<GameState>
{
    private readonly ViewportManager _viewportManager;
    private readonly EditorChromeBuilder _chrome;
    private readonly Action<bool>? _setOsCursorVisible;
    private readonly EntitySet _cursorSet;

    private bool _cursorApplied;

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

        // Native chrome tracks the window AND the device-pixel ratio: relayout when either
        // changed (or on entry). The DPR scales the chrome's point-based metrics so its physical
        // size is stable on a device-resolution (HiDPI) backbuffer — see EditorChromeLayout.
        var scale = _viewportManager.DevicePixelRatio;
        if (_chrome.LaidOutWidth != _viewportManager.ScreenWidth ||
            _chrome.LaidOutHeight != _viewportManager.ScreenHeight ||
            _chrome.LaidOutScale != scale)
            _chrome.Relayout(_viewportManager.ScreenWidth, _viewportManager.ScreenHeight, scale);

        var (left, top, right, bottom) = EditorChromeLayout.ViewportInset(scale);
        _viewportManager.SetViewportInset(left, top, right, bottom); // no-op when unchanged

        // One pointer at a time, in both transport states: the OS cursor (it must reach the
        // chrome), never the game cursor sprite. Applied once — game entities created later in
        // Load are covered because the first Update runs after Load.
        if (!_cursorApplied)
        {
            ApplyEditorCursor();
            _cursorApplied = true;
        }
    }

    private void ApplyEditorCursor()
    {
        _setOsCursorVisible?.Invoke(true);

        foreach (var cursor in _cursorSet.GetEntities())
        {
            ref var controller = ref cursor.Get<CursorControllerComponent>();
            controller.IsVisible = false;
            if (cursor.Has<DrawComponent>())
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
