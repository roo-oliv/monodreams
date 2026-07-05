#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// Pure layout math for the Blender-style editor shell, all in <b>physical screen pixels</b>
/// (the chrome renders at native window resolution — see <c>RenderTargetID.Editor</c>). The shell
/// reserves margins around the game viewport: a top bar (the toolbar), a right panel strip
/// (the systems panel's home), and a thin bottom status strip. The same numbers feed two
/// consumers that must never disagree: the chrome entity layout (panels + buttons) and the
/// <c>ViewportManager.SetViewportInset</c> call that shrinks the game viewport — both derive
/// from <see cref="ViewportInset"/>, so the panels always exactly cover the reserved margins.
/// World-free and cursor-free so it is unit-testable like <c>CameraNav</c>/<c>GizmoTransform</c>.
///
/// <para><b>Device-pixel-ratio scaling.</b> The metric constants are authored in LOGICAL points
/// (a 44-point toolbar). Every layout function takes a <c>scale</c> — the viewport manager's
/// <c>DevicePixelRatio</c> — and multiplies the metrics by it, so when the editor runs on a
/// device-resolution backbuffer (macOS Retina, <c>EditorHiDpi</c>: screen sizes and cursor
/// <c>ScreenPosition</c> are device pixels at 2× the point size) the chrome keeps its physical
/// on-screen size while gaining pixel density. The default <c>scale = 1</c> is byte-identical to
/// the pre-DPR layout, so every non-HiDPI run (and every existing test) is untouched.</para>
/// </summary>
public static class EditorChromeLayout
{
    /// <summary>Top bar height (the toolbar strip), logical points.</summary>
    public const int TopBarHeight = 44;

    /// <summary>Right panel strip width — the systems panel's home, logical points.</summary>
    public const int RightPanelWidth = 280;

    /// <summary>Bottom strip height, logical points — the asset palette's home (island-authoring
    /// plan §3): a band-selector header row plus a scrollable grid of palette <b>cards</b> (a sprite
    /// preview on top, a text label on the bottom — see <see cref="PaletteLayout"/>). Raised from the
    /// v1 flat-row strip (104) to give the cards real screen real estate (one full card row under the
    /// header, wheel-scroll for overflow). Constant whether or not a screen supplies a palette, so
    /// every consumer of the inset (shell, chrome, mouse mapping) stays in lockstep by
    /// construction — a screen without a palette simply shows the empty strip.</summary>
    public const int BottomBarHeight = 168;

    /// <summary>No left strip today (kept as an explicit 0 so the inset shape is symmetrical).</summary>
    public const int LeftPanelWidth = 0;

    /// <summary>Toolbar button height, logical points (fits the top bar with breathing room).</summary>
    public const int ButtonHeight = 30;

    /// <summary>Horizontal gap between toolbar buttons, logical points.</summary>
    public const int ButtonGap = 8;

    /// <summary>Horizontal label padding inside a button, logical points.</summary>
    public const int ButtonPaddingX = 10;

    /// <summary>Left margin before the first toolbar button, logical points.</summary>
    public const int RowMarginX = 10;

    /// <summary>A logical-point metric in screen pixels at <paramref name="scale"/> (the device
    /// pixel ratio), rounded to whole pixels.</summary>
    public static int Px(int points, float scale) => (int)MathF.Round(points * scale);

    /// <summary>The viewport-inset margins the shell reserves, in screen pixels at
    /// <paramref name="scale"/> — pass to <c>ViewportManager.SetViewportInset(left, top, right,
    /// bottom)</c>.</summary>
    public static (int Left, int Top, int Right, int Bottom) ViewportInset(float scale = 1f) =>
        (Px(LeftPanelWidth, scale), Px(TopBarHeight, scale), Px(RightPanelWidth, scale), Px(BottomBarHeight, scale));

    /// <summary>The top bar rectangle: full window width, docked at the top.</summary>
    public static Rectangle TopBar(int screenWidth, float scale = 1f) =>
        new(0, 0, Math.Max(1, screenWidth), Px(TopBarHeight, scale));

    /// <summary>The right panel strip: between the top bar and the bottom strip, docked right.</summary>
    public static Rectangle RightPanel(int screenWidth, int screenHeight, float scale = 1f) => new(
        Math.Max(0, screenWidth - Px(RightPanelWidth, scale)),
        Px(TopBarHeight, scale),
        Px(RightPanelWidth, scale),
        Math.Max(1, screenHeight - Px(TopBarHeight, scale) - Px(BottomBarHeight, scale)));

    /// <summary>The bottom status strip: full window width, docked at the bottom.</summary>
    public static Rectangle BottomBar(int screenWidth, int screenHeight, float scale = 1f) =>
        new(0, Math.Max(0, screenHeight - Px(BottomBarHeight, scale)), Math.Max(1, screenWidth), Px(BottomBarHeight, scale));

    /// <summary>A toolbar button's width for a label width already measured in screen pixels
    /// (the caller scales the label), padded at <paramref name="scale"/>.</summary>
    public static int ButtonWidth(float labelWidth, float scale = 1f) =>
        (int)MathF.Ceiling(labelWidth) + Px(ButtonPaddingX, scale) * 2;

    /// <summary>
    /// Lays the toolbar buttons out left-to-right inside the top bar, vertically centered.
    /// Returns one rectangle per entry of <paramref name="buttonWidths"/>, in order.
    /// </summary>
    public static Rectangle[] ButtonRow(IReadOnlyList<int> buttonWidths, float scale = 1f)
    {
        var rects = new Rectangle[buttonWidths.Count];
        var height = Px(ButtonHeight, scale);
        var gap = Px(ButtonGap, scale);
        var x = Px(RowMarginX, scale);
        var y = (Px(TopBarHeight, scale) - height) / 2;
        for (var i = 0; i < buttonWidths.Count; i++)
        {
            rects[i] = new Rectangle(x, y, buttonWidths[i], height);
            x += buttonWidths[i] + gap;
        }
        return rects;
    }
}
