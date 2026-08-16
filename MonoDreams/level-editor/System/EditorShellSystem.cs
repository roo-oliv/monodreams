#nullable enable
using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.UI;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.UI;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// Keeps the Blender-style editor shell applied while the editor is composed (Wave 7; transport
/// model since the F1-toggle retirement): under the editor run configuration the shell is
/// <b>constant</b> — it does not collapse when the transport is Playing, because the editor is
/// always visible and the game simply runs inside the inset viewport. Each frame it maintains:
///
/// <list type="number">
///   <item><b>Region splitters (UX-B/UX2-B).</b> It hit-tests the three splitter drag zones (the
///   left strip's right edge, the right strip's left edge, the bottom shelf's top edge) against the
///   cursor's raw <c>ScreenPosition</c> and, on a drag, resizes
///   <see cref="EditorShellStateComponent.LeftWidthPt"/> /
///   <see cref="EditorShellStateComponent.RightWidthPt"/>
///   / <see cref="EditorShellStateComponent.BottomHeightPt"/> (device-px delta → points via the
///   DPR). A splitter drag claims the shared <see cref="EditorShellStateComponent.ActiveDrag"/>
///   token on the press edge and holds it through the release edge (cleared the frame after), so
///   the panel / palette / toolbar stand down and the drag never also fires a row / card / button
///   click. The zones sit in the reserved margins (a drag there is <c>OutsideViewport</c>, so
///   viewport tools are muted already).</item>
///   <item><b>The viewport inset.</b> It applies <see cref="EditorChromeLayout.ViewportInset"/>
///   (derived from the shell state's region sizes) to the <see cref="ViewportManager"/>
///   (<c>SetViewportInset</c>). Because the ViewportManager is the single source of truth, BOTH the
///   FinalDraw compositing AND <c>MapMouse</c> follow the same rectangle.</item>
///   <item><b>Chrome layout.</b> The native-resolution chrome (panels + toolbar + splitters + the
///   bottom tab) lays out in physical pixels, so the system relayouts it whenever the window size,
///   the DPR, OR a region size (a splitter drag) differs from the last laid-out values.</item>
///   <item><b>The cursor.</b> The OS cursor is the one visible pointer (it must reach the chrome
///   margins) and the in-game HUD cursor sprite is hidden — in both transport states.</item>
/// </list>
///
/// <para>A screen without the editor composed never constructs this system, and its viewport stays
/// the historical full-window letterbox — byte-identical. <see cref="Dispose"/> clears the inset
/// and re-hides the OS cursor.</para>
/// </summary>
public sealed class EditorShellSystem : ISystem<GameState>
{
    private readonly ViewportManager _viewportManager;
    private readonly EditorChromeBuilder _chrome;
    private readonly Action<bool>? _setOsCursorVisible;
    private readonly EntitySet _cursorSet;
    private readonly EntitySet _cursorInputSet;
    private readonly EditorShellStateComponent _state;

    private bool _cursorApplied;

    public bool IsEnabled { get; set; } = true;

    /// <summary>The shared region-layout state (region sizes, active tabs, drag ownership). Exposed
    /// for tests.</summary>
    public EditorShellStateComponent State => _state;

    public EditorShellSystem(
        World world,
        ViewportManager viewportManager,
        EditorChromeBuilder chrome,
        Action<bool>? setOsCursorVisible = null,
        EditorShellStateComponent? shellState = null)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        _viewportManager = viewportManager ?? throw new ArgumentNullException(nameof(viewportManager));
        _chrome = chrome ?? throw new ArgumentNullException(nameof(chrome));
        _setOsCursorVisible = setOsCursorVisible;
        _state = shellState ?? new EditorShellStateComponent();
        _cursorSet = world.GetEntities().With<CursorControllerComponent>().AsSet();
        _cursorInputSet = world.GetEntities().With<CursorInputComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        var scale = _viewportManager.DevicePixelRatio;

        // Splitters first: a drag mutates the region sizes this frame, so the relayout + inset below
        // (and every chrome system's re-read of the layout) reflect the new sizes with no lag.
        HandleSplitters(scale);

        // Native chrome tracks the window, the DPR, AND the runtime region sizes: relayout when any
        // changed (or on entry). The DPR scales the chrome's point-based metrics.
        if (_chrome.LaidOutWidth != _viewportManager.ScreenWidth ||
            _chrome.LaidOutHeight != _viewportManager.ScreenHeight ||
            _chrome.LaidOutScale != scale ||
            _chrome.LaidOutLeftWidthPt != _state.LeftWidthPt ||
            _chrome.LaidOutRightWidthPt != _state.RightWidthPt ||
            _chrome.LaidOutBottomHeightPt != _state.BottomHeightPt)
            _chrome.Relayout(_viewportManager.ScreenWidth, _viewportManager.ScreenHeight, scale,
                _state.LeftWidthPt, _state.RightWidthPt, _state.BottomHeightPt);

        var (left, top, right, bottom) =
            EditorChromeLayout.ViewportInset(scale, _state.LeftWidthPt, _state.RightWidthPt, _state.BottomHeightPt);
        _viewportManager.SetViewportInset(left, top, right, bottom); // no-op when unchanged

        RecolorSplitters(scale);

        // One pointer at a time, in both transport states.
        if (!_cursorApplied)
        {
            ApplyEditorCursor();
            _cursorApplied = true;
        }
    }

    // ---- Splitters (own the shared drag token; device-px → pt via the DPR) ----

    private void HandleSplitters(float scale)
    {
        foreach (var cursor in _cursorInputSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            var w = _viewportManager.ScreenWidth;
            var h = _viewportManager.ScreenHeight;

            // Clear a finished splitter drag the frame AFTER release (button fully up) — see the
            // ShellDragKind doc: holding the token through the release edge is what makes the
            // "splitter drag never also clicks" exclusion independent of weave order.
            if ((_state.ActiveDrag == ShellDragKind.LeftSplitter ||
                 _state.ActiveDrag == ShellDragKind.RightSplitter ||
                 _state.ActiveDrag == ShellDragKind.BottomSplitter) &&
                !input.LeftButton && !input.LeftButtonReleased)
                _state.ActiveDrag = ShellDragKind.None;

            // Continue / finalise the owned drag (the release edge still carries the final position).
            if (_state.ActiveDrag == ShellDragKind.LeftSplitter && (input.LeftButton || input.LeftButtonReleased))
            {
                var deltaPt = (input.ScreenPosition.X - _state.DragGrabPixel) / scale; // drag right → grow
                _state.LeftWidthPt = (int)MathF.Round(_state.DragGrabValue + deltaPt);
            }
            else if (_state.ActiveDrag == ShellDragKind.RightSplitter && (input.LeftButton || input.LeftButtonReleased))
            {
                var deltaPt = (_state.DragGrabPixel - input.ScreenPosition.X) / scale; // drag left → grow
                _state.RightWidthPt = (int)MathF.Round(_state.DragGrabValue + deltaPt);
            }
            else if (_state.ActiveDrag == ShellDragKind.BottomSplitter && (input.LeftButton || input.LeftButtonReleased))
            {
                var deltaPt = (_state.DragGrabPixel - input.ScreenPosition.Y) / scale; // drag up → grow
                _state.BottomHeightPt = (int)MathF.Round(_state.DragGrabValue + deltaPt);
            }

            // Claim on the press edge (only when no other drag owns the pointer).
            if (_state.ActiveDrag == ShellDragKind.None && input.LeftButtonPressed)
            {
                var point = new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
                if (EditorChromeLayout.LeftSplitter(w, h, scale, _state.LeftWidthPt, _state.BottomHeightPt).Contains(point))
                {
                    _state.ActiveDrag = ShellDragKind.LeftSplitter;
                    _state.DragGrabValue = _state.LeftWidthPt;
                    _state.DragGrabPixel = input.ScreenPosition.X;
                }
                else if (EditorChromeLayout.RightSplitter(w, h, scale, _state.RightWidthPt, _state.BottomHeightPt).Contains(point))
                {
                    _state.ActiveDrag = ShellDragKind.RightSplitter;
                    _state.DragGrabValue = _state.RightWidthPt;
                    _state.DragGrabPixel = input.ScreenPosition.X;
                }
                else if (EditorChromeLayout.BottomSplitter(w, h, scale, _state.BottomHeightPt).Contains(point))
                {
                    _state.ActiveDrag = ShellDragKind.BottomSplitter;
                    _state.DragGrabValue = _state.BottomHeightPt;
                    _state.DragGrabPixel = input.ScreenPosition.Y;
                }
            }
            return;
        }
    }

    private void RecolorSplitters(float scale)
    {
        var w = _viewportManager.ScreenWidth;
        var h = _viewportManager.ScreenHeight;
        var point = CursorPoint();
        var leftZone = EditorChromeLayout.LeftSplitter(w, h, scale, _state.LeftWidthPt, _state.BottomHeightPt);
        var rightZone = EditorChromeLayout.RightSplitter(w, h, scale, _state.RightWidthPt, _state.BottomHeightPt);
        var bottomZone = EditorChromeLayout.BottomSplitter(w, h, scale, _state.BottomHeightPt);

        SetSplitterFill(_chrome.LeftSplitter,
            _state.ActiveDrag == ShellDragKind.LeftSplitter || (point is { } p0 && leftZone.Contains(p0)));
        SetSplitterFill(_chrome.RightSplitter,
            _state.ActiveDrag == ShellDragKind.RightSplitter || (point is { } p1 && rightZone.Contains(p1)));
        SetSplitterFill(_chrome.BottomSplitter,
            _state.ActiveDrag == ShellDragKind.BottomSplitter || (point is { } p2 && bottomZone.Contains(p2)));
    }

    private static void SetSplitterFill(Entity splitter, bool strong)
    {
        if (!splitter.IsAlive || !splitter.Has<SimpleButtonComponent>()) return;
        ref var visual = ref splitter.Get<SimpleButtonComponent>();
        visual.FillColor = strong ? EditorTheme.BorderStrong : EditorTheme.Border;
        visual.Color = visual.FillColor;
    }

    private Point? CursorPoint()
    {
        foreach (var cursor in _cursorInputSet.GetEntities())
        {
            ref readonly var input = ref cursor.Get<CursorInputComponent>();
            return new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y);
        }
        return null;
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
        _cursorInputSet.Dispose();
    }
}
