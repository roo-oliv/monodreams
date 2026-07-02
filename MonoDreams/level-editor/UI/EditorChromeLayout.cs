#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// Pure layout math for the Blender-style editor shell, all in <b>physical screen pixels</b>
/// (the chrome renders at native window resolution — see <c>RenderTargetID.Editor</c>). The shell
/// reserves margins around the game viewport: a top bar (the toolbar), a right panel strip
/// (reserved now for the Wave-8 systems panel), and a thin bottom status strip. The same numbers
/// feed two consumers that must never disagree: the chrome entity layout (panels + buttons) and
/// the <c>ViewportManager.SetViewportInset</c> call that shrinks the game viewport — both derive
/// from <see cref="ViewportInset"/>, so the panels always exactly cover the reserved margins.
/// World-free and cursor-free so it is unit-testable like <c>CameraNav</c>/<c>GizmoTransform</c>.
/// </summary>
public static class EditorChromeLayout
{
    /// <summary>Top bar height (the toolbar strip), px.</summary>
    public const int TopBarHeight = 44;

    /// <summary>Right panel strip width — reserved for the Wave-8 systems panel, px.</summary>
    public const int RightPanelWidth = 280;

    /// <summary>Bottom status strip height, px.</summary>
    public const int BottomBarHeight = 24;

    /// <summary>No left strip today (kept as an explicit 0 so the inset shape is symmetrical).</summary>
    public const int LeftPanelWidth = 0;

    /// <summary>Toolbar button height, px (fits the top bar with breathing room).</summary>
    public const int ButtonHeight = 30;

    /// <summary>Horizontal gap between toolbar buttons, px.</summary>
    public const int ButtonGap = 8;

    /// <summary>Horizontal label padding inside a button, px.</summary>
    public const int ButtonPaddingX = 10;

    /// <summary>Left margin before the first toolbar button, px.</summary>
    public const int RowMarginX = 10;

    /// <summary>The viewport-inset margins the shell reserves — pass to
    /// <c>ViewportManager.SetViewportInset(left, top, right, bottom)</c>.</summary>
    public static (int Left, int Top, int Right, int Bottom) ViewportInset =>
        (LeftPanelWidth, TopBarHeight, RightPanelWidth, BottomBarHeight);

    /// <summary>The top bar rectangle: full window width, docked at the top.</summary>
    public static Rectangle TopBar(int screenWidth) =>
        new(0, 0, Math.Max(1, screenWidth), TopBarHeight);

    /// <summary>The right panel strip: between the top bar and the bottom strip, docked right.</summary>
    public static Rectangle RightPanel(int screenWidth, int screenHeight) => new(
        Math.Max(0, screenWidth - RightPanelWidth),
        TopBarHeight,
        RightPanelWidth,
        Math.Max(1, screenHeight - TopBarHeight - BottomBarHeight));

    /// <summary>The bottom status strip: full window width, docked at the bottom.</summary>
    public static Rectangle BottomBar(int screenWidth, int screenHeight) =>
        new(0, Math.Max(0, screenHeight - BottomBarHeight), Math.Max(1, screenWidth), BottomBarHeight);

    /// <summary>A toolbar button's width for a (already scale-adjusted) label width, px.</summary>
    public static int ButtonWidth(float labelWidth) =>
        (int)MathF.Ceiling(labelWidth) + ButtonPaddingX * 2;

    /// <summary>
    /// Lays the toolbar buttons out left-to-right inside the top bar, vertically centered.
    /// Returns one rectangle per entry of <paramref name="buttonWidths"/>, in order.
    /// </summary>
    public static Rectangle[] ButtonRow(IReadOnlyList<int> buttonWidths)
    {
        var rects = new Rectangle[buttonWidths.Count];
        var x = RowMarginX;
        var y = (TopBarHeight - ButtonHeight) / 2;
        for (var i = 0; i < buttonWidths.Count; i++)
        {
            rects[i] = new Rectangle(x, y, buttonWidths[i], ButtonHeight);
            x += buttonWidths[i] + ButtonGap;
        }
        return rects;
    }
}
