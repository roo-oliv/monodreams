#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// Pure layout math for the Blender-style editor shell, all in <b>physical screen pixels</b>
/// (the chrome renders at native window resolution — see <c>RenderTargetID.Editor</c>). The shell
/// reserves margins around the game viewport: a thin global top bar (the window toolbar), a LEFT
/// panel strip (the Entities / Systems / Scenes tabs — UX2-B), a right panel (the dedicated
/// Inspector), a bottom shelf (the Assets palette), and — carved out of the game viewport itself,
/// below the top bar — the CENTER region's <b>Scene panel header</b> (the transport + future tools).
/// The same numbers feed two consumers that must never disagree: the chrome entity layout (panels +
/// buttons) and the <c>ViewportManager.SetViewportInset</c> call that shrinks the game viewport —
/// both derive from <see cref="ViewportInset"/>, so the panels always exactly cover the reserved
/// margins and the Scene header sits exactly in the extra top inset it adds. World-free and
/// cursor-free so it is unit-testable like <c>CameraNav</c>/<c>GizmoTransform</c>.
///
/// <para><b>Runtime-resizable regions (UX-B/UX2-B).</b> The LEFT strip width, right strip width and
/// bottom shelf height are no longer fixed constants: the region methods take the current sizes in
/// logical points (from <c>EditorShellStateComponent</c>). The parameters DEFAULT to
/// <see cref="LeftPanelWidth"/> / <see cref="RightPanelWidth"/> / <see cref="BottomBarHeight"/>, so
/// every call that omits them stays consistent with the shell defaults.</para>
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

    /// <summary>The right panel strip's DEFAULT width, logical points — the runtime width lives in
    /// <c>EditorShellStateComponent.RightWidthPt</c> (default = this). The Scene / Systems / Project
    /// tabs' home.</summary>
    public const int RightPanelWidth = 280;

    /// <summary>The bottom shelf's DEFAULT height, logical points — the runtime height lives in
    /// <c>EditorShellStateComponent.BottomHeightPt</c> (default = this). The asset palette's home
    /// (island-authoring plan §3): a tab strip + band-selector header + a scrollable grid of palette
    /// <b>cards</b>. Constant whether or not a screen supplies a palette, so every consumer of the
    /// inset (shell, chrome, mouse mapping) stays in lockstep — a screen without a palette simply
    /// shows the empty strip.</summary>
    public const int BottomBarHeight = 168;

    /// <summary>The left panel strip's DEFAULT width, logical points — the runtime width lives in
    /// <c>EditorShellStateComponent.LeftWidthPt</c> (default = this). The Entities / Systems / Scenes
    /// tabs' home (UX2-B activated the left region UX-B reserved at 0).</summary>
    public const int LeftPanelWidth = 240;

    /// <summary>The center region's <b>Scene panel header</b> band height, logical points — carved
    /// out of the game viewport just below the top bar (so <see cref="ViewportInset"/>'s top margin is
    /// <see cref="TopBarHeight"/> + this). Hosts the transport (UX2-B) and, in later waves, the tool
    /// cluster / Entity menu / mode toggle / camera button (the panel-header framework slots).</summary>
    public const int SceneHeaderHeight = 40;

    /// <summary>Toolbar button height, logical points (fits the top bar with breathing room).</summary>
    public const int ButtonHeight = 30;

    /// <summary>Horizontal gap between toolbar buttons, logical points.</summary>
    public const int ButtonGap = 8;

    /// <summary>Extra horizontal gap between button CLUSTERS in a row (logical points) — UX2-C: the
    /// Scene panel header sets the transport cluster apart from the tool cluster with this wider gap.</summary>
    public const int ClusterGap = 18;

    /// <summary>Horizontal label padding inside a button, logical points.</summary>
    public const int ButtonPaddingX = 10;

    /// <summary>Left margin before the first toolbar button, logical points.</summary>
    public const int RowMarginX = 10;

    /// <summary>The tab strip height at the top of the right strip / bottom shelf, logical points.</summary>
    public const int TabStripHeight = 26;

    /// <summary>Horizontal label padding inside a tab, logical points.</summary>
    public const int TabPaddingX = 10;

    /// <summary>The active tab's accent underline thickness, logical points.</summary>
    public const int TabUnderlineHeight = 3;

    /// <summary>The splitter drag zone thickness (on a region's viewport-facing edge), logical points.</summary>
    public const int SplitterThickness = 4;

    /// <summary>A logical-point metric in screen pixels at <paramref name="scale"/> (the device
    /// pixel ratio), rounded to whole pixels.</summary>
    public static int Px(int points, float scale) => (int)MathF.Round(points * scale);

    /// <summary>The viewport-inset margins the shell reserves, in screen pixels at
    /// <paramref name="scale"/> — pass to <c>ViewportManager.SetViewportInset(left, top, right,
    /// bottom)</c>. The <b>left</b> margin is the (now active) left strip; the <b>top</b> margin is
    /// the global top bar PLUS the Scene panel header carved out of the game viewport (UX2-B — one
    /// inset source, so compositing + mouse mapping + <c>OutsideViewport</c> all follow the header for
    /// free). <paramref name="leftWidthPt"/>/<paramref name="rightWidthPt"/>/<paramref name="bottomHeightPt"/>
    /// default to the shell constants.</summary>
    public static (int Left, int Top, int Right, int Bottom) ViewportInset(
        float scale = 1f, int leftWidthPt = LeftPanelWidth, int rightWidthPt = RightPanelWidth,
        int bottomHeightPt = BottomBarHeight) =>
        (Px(leftWidthPt, scale), Px(TopBarHeight, scale) + Px(SceneHeaderHeight, scale),
            Px(rightWidthPt, scale), Px(bottomHeightPt, scale));

    /// <summary>The top bar rectangle: full window width, docked at the top (the thin global bar).</summary>
    public static Rectangle TopBar(int screenWidth, float scale = 1f) =>
        new(0, 0, Math.Max(1, screenWidth), Px(TopBarHeight, scale));

    /// <summary>The LEFT panel strip: between the top bar and the bottom shelf, docked left (the
    /// Entities / Systems / Scenes tabs' home). <paramref name="leftWidthPt"/>/<paramref name="bottomHeightPt"/>
    /// default to the shell constants.</summary>
    public static Rectangle LeftPanel(int screenWidth, int screenHeight, float scale = 1f,
        int leftWidthPt = LeftPanelWidth, int bottomHeightPt = BottomBarHeight) => new(
        0,
        Px(TopBarHeight, scale),
        Px(leftWidthPt, scale),
        Math.Max(1, screenHeight - Px(TopBarHeight, scale) - Px(bottomHeightPt, scale)));

    /// <summary>The center region's <b>Scene panel header</b> band: docked below the top bar, between
    /// the left and right strips, <see cref="SceneHeaderHeight"/> tall — the extra top inset the
    /// viewport gives up. Hosts the transport (UX2-B) and later header controls.</summary>
    public static Rectangle SceneHeader(int screenWidth, int screenHeight, float scale = 1f,
        int leftWidthPt = LeftPanelWidth, int rightWidthPt = RightPanelWidth) => new(
        Px(leftWidthPt, scale),
        Px(TopBarHeight, scale),
        Math.Max(1, screenWidth - Px(leftWidthPt, scale) - Px(rightWidthPt, scale)),
        Px(SceneHeaderHeight, scale));

    /// <summary>The right panel strip: between the top bar and the bottom shelf, docked right.
    /// <paramref name="rightWidthPt"/>/<paramref name="bottomHeightPt"/> default to the fixed
    /// constants.</summary>
    public static Rectangle RightPanel(int screenWidth, int screenHeight, float scale = 1f,
        int rightWidthPt = RightPanelWidth, int bottomHeightPt = BottomBarHeight) => new(
        Math.Max(0, screenWidth - Px(rightWidthPt, scale)),
        Px(TopBarHeight, scale),
        Px(rightWidthPt, scale),
        Math.Max(1, screenHeight - Px(TopBarHeight, scale) - Px(bottomHeightPt, scale)));

    /// <summary>The bottom shelf: full window width, docked at the bottom.
    /// <paramref name="bottomHeightPt"/> defaults to the fixed constant.</summary>
    public static Rectangle BottomBar(int screenWidth, int screenHeight, float scale = 1f,
        int bottomHeightPt = BottomBarHeight) =>
        new(0, Math.Max(0, screenHeight - Px(bottomHeightPt, scale)), Math.Max(1, screenWidth), Px(bottomHeightPt, scale));

    // ── Tab strips (right strip + bottom shelf) ──────────────────────────────────────────────────

    /// <summary>The tab strip rectangle at the top of a region <paramref name="regionRect"/>
    /// (full width, <see cref="TabStripHeight"/> tall). Its left edge is inset by the splitter so a
    /// splitter zone never overlaps a tab.</summary>
    public static Rectangle TabStrip(Rectangle regionRect, float scale = 1f) => new(
        regionRect.X, regionRect.Y, regionRect.Width, Px(TabStripHeight, scale));

    /// <summary>The region body BELOW its tab strip — where the panel rows / palette cards render
    /// (the tab strip is reserved off the top).</summary>
    public static Rectangle RegionBody(Rectangle regionRect, float scale = 1f)
    {
        var strip = Px(TabStripHeight, scale);
        return new Rectangle(regionRect.X, regionRect.Y + strip,
            regionRect.Width, Math.Max(1, regionRect.Height - strip));
    }

    /// <summary>Lays the tabs out left-to-right inside a tab strip, full strip height, starting past
    /// the splitter gutter. Returns one rect per entry of <paramref name="tabWidths"/>, in order.</summary>
    public static Rectangle[] TabRow(Rectangle strip, IReadOnlyList<int> tabWidths, float scale = 1f)
    {
        var rects = new Rectangle[tabWidths.Count];
        var x = strip.X + Px(SplitterThickness, scale); // clear of the left-edge splitter gutter
        for (var i = 0; i < tabWidths.Count; i++)
        {
            rects[i] = new Rectangle(x, strip.Y, tabWidths[i], strip.Height);
            x += tabWidths[i];
        }
        return rects;
    }

    /// <summary>A tab's width for a label width already measured in screen pixels
    /// (the caller scales the label), padded at <paramref name="scale"/>.</summary>
    public static int TabWidth(float labelWidth, float scale = 1f) =>
        (int)MathF.Ceiling(labelWidth) + Px(TabPaddingX, scale) * 2;

    /// <summary>The active-tab accent underline: a full-width bar along the tab's bottom edge.</summary>
    public static Rectangle TabUnderline(Rectangle tab, float scale = 1f)
    {
        var h = Px(TabUnderlineHeight, scale);
        return new Rectangle(tab.X, tab.Bottom - h, tab.Width, h);
    }

    // ── Splitters (on the viewport-facing edge of each resizable region) ─────────────────────────

    /// <summary>The left strip's splitter drag zone — a thin vertical band on the strip's
    /// <b>right</b> (viewport-facing) edge, inside the reserved left margin (so a drag there is
    /// <c>OutsideViewport</c> and never a game click).</summary>
    public static Rectangle LeftSplitter(int screenWidth, int screenHeight, float scale = 1f,
        int leftWidthPt = LeftPanelWidth, int bottomHeightPt = BottomBarHeight)
    {
        var panel = LeftPanel(screenWidth, screenHeight, scale, leftWidthPt, bottomHeightPt);
        var t = Px(SplitterThickness, scale);
        return new Rectangle(panel.Right - t, panel.Y, t, panel.Height);
    }

    /// <summary>The right strip's splitter drag zone — a thin vertical band on the strip's
    /// <b>left</b> (viewport-facing) edge, inside the reserved margin (so a drag there is
    /// <c>OutsideViewport</c> and never a game click) and clear of the row content (which starts past
    /// the padding gutter).</summary>
    public static Rectangle RightSplitter(int screenWidth, int screenHeight, float scale = 1f,
        int rightWidthPt = RightPanelWidth, int bottomHeightPt = BottomBarHeight)
    {
        var panel = RightPanel(screenWidth, screenHeight, scale, rightWidthPt, bottomHeightPt);
        return new Rectangle(panel.X, panel.Y, Px(SplitterThickness, scale), panel.Height);
    }

    /// <summary>The bottom shelf's splitter drag zone — a thin horizontal band on the shelf's
    /// <b>top</b> (viewport-facing) edge, above the tab strip, inside the reserved margin.</summary>
    public static Rectangle BottomSplitter(int screenWidth, int screenHeight, float scale = 1f,
        int bottomHeightPt = BottomBarHeight)
    {
        var bar = BottomBar(screenWidth, screenHeight, scale, bottomHeightPt);
        return new Rectangle(bar.X, bar.Y, bar.Width, Px(SplitterThickness, scale));
    }

    /// <summary>A toolbar button's width for a label width already measured in screen pixels
    /// (the caller scales the label), padded at <paramref name="scale"/>.</summary>
    public static int ButtonWidth(float labelWidth, float scale = 1f) =>
        (int)MathF.Ceiling(labelWidth) + Px(ButtonPaddingX, scale) * 2;

    /// <summary>
    /// Lays a button row out left-to-right inside a bar rectangle, vertically centered, starting past
    /// the left row margin. Returns one rectangle per entry of <paramref name="buttonWidths"/>, in
    /// order. Used by both the window top bar and the Scene panel header (UX2-B transport + UX2-C tools).
    /// <paramref name="separatorAfterIndex"/> (default -1 = none) inserts an extra <see cref="ClusterGap"/>
    /// after that button — the Scene header sets the transport cluster apart from the tool cluster.
    /// </summary>
    public static Rectangle[] ButtonRowIn(Rectangle bar, IReadOnlyList<int> buttonWidths, float scale = 1f,
        int separatorAfterIndex = -1)
    {
        var rects = new Rectangle[buttonWidths.Count];
        var height = Px(ButtonHeight, scale);
        var gap = Px(ButtonGap, scale);
        var clusterGap = Px(ClusterGap, scale);
        var x = bar.X + Px(RowMarginX, scale);
        var y = bar.Y + (bar.Height - height) / 2;
        for (var i = 0; i < buttonWidths.Count; i++)
        {
            rects[i] = new Rectangle(x, y, buttonWidths[i], height);
            x += buttonWidths[i] + gap + (i == separatorAfterIndex ? clusterGap : 0);
        }
        return rects;
    }

    /// <summary>
    /// Lays the window top bar's buttons out left-to-right, vertically centered. A thin wrapper over
    /// <see cref="ButtonRowIn"/> anchored at the top-bar origin — byte-identical to the pre-UX2-B row.
    /// </summary>
    public static Rectangle[] ButtonRow(IReadOnlyList<int> buttonWidths, float scale = 1f) =>
        ButtonRowIn(new Rectangle(0, 0, 1, Px(TopBarHeight, scale)), buttonWidths, scale);

    /// <summary>
    /// The Scene panel header's <b>nav-corner button</b> (UX2-E): a square icon button
    /// (<see cref="ButtonHeight"/> a side) docked at the header's RIGHT edge, inset by the row margin
    /// and vertically centered — the Blender back-to-camera-view affordance, opposite the left-anchored
    /// transport/tool row so it never collides with it. Right-anchored (unlike
    /// <see cref="ButtonRowIn"/>'s left-to-right flow), so it stays in the corner as the header widens.
    /// </summary>
    public static Rectangle SceneHeaderNavButton(Rectangle sceneHeader, float scale = 1f)
    {
        var size = Px(ButtonHeight, scale);
        var margin = Px(RowMarginX, scale);
        return new Rectangle(
            sceneHeader.Right - margin - size,
            sceneHeader.Y + (sceneHeader.Height - size) / 2,
            size, size);
    }
}
