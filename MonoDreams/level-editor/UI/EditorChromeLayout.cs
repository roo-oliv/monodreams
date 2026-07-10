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

    /// <summary>The Scene panel header's <b>tab row</b> height (TB-A row 1), logical points — the full-width
    /// viewport tab strip lives here alone, so many tabs never collide with the tools below.</summary>
    public const int SceneHeaderTabRowHeight = 30;

    /// <summary>The Scene panel header's <b>tool row</b> height (TB-A row 2), logical points — the left tool
    /// cluster (Move/Rotate/Scale/Boundary/Snap · Overlays · Entity ▾) and the right transport cluster
    /// (camera-view · Play/Pause · Restart · Save) share it. Sized like the old single header row so the
    /// button layout math inside it is unchanged.</summary>
    public const int SceneHeaderToolRowHeight = 40;

    /// <summary>The center region's <b>Scene panel header</b> band height, logical points — carved out of
    /// the game viewport just below the top bar (so <see cref="ViewportInset"/>'s top margin is
    /// <see cref="TopBarHeight"/> + this). TWO rows (TB-A): the tab strip (row 1) over the tools + transport
    /// (row 2).</summary>
    public const int SceneHeaderHeight = SceneHeaderTabRowHeight + SceneHeaderToolRowHeight;

    /// <summary>The window <b>status bar</b> height (UX3-F), logical points — a thin strip flush with
    /// the window bottom, BELOW the assets shelf, so <see cref="ViewportInset"/>'s bottom margin is the
    /// bottom shelf PLUS this. Blender/IntelliJ-style: the modal readout / contextual status on the left,
    /// the scene id + mode on the right (see <c>EditorStatusBarSystem</c>). Not resizable.</summary>
    public const int StatusBarHeight = 22;

    /// <summary>Toolbar button height, logical points (fits the top bar with breathing room).</summary>
    public const int ButtonHeight = 30;

    /// <summary>Horizontal gap between toolbar buttons, logical points.</summary>
    public const int ButtonGap = 8;

    /// <summary>Extra horizontal gap between button CLUSTERS in a row (logical points) — UX2-C: the
    /// Scene panel header sets the transport cluster apart from the tool cluster with this wider gap.</summary>
    public const int ClusterGap = 18;

    /// <summary>Horizontal padding inside a viewport tab (logical points).</summary>
    public const int ViewportTabPaddingX = 10;

    /// <summary>The box (logical points, square) a viewport tab's ▶ play marker / <c>×</c> close glyph
    /// occupies in the tab's left / right gutter.</summary>
    public const int ViewportTabGlyph = 12;

    /// <summary>The gap (logical points) between a viewport tab's glyph and its label.</summary>
    public const int ViewportTabGlyphGap = 4;

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
    /// the global top bar PLUS the Scene panel header carved out of the game viewport (UX2-B); the
    /// <b>bottom</b> margin is the assets shelf PLUS the <see cref="StatusBarHeight"/> status bar
    /// (UX3-F) — one inset source, so compositing + mouse mapping + <c>OutsideViewport</c> all follow
    /// the header + status bar for free (pre-mortem #6). <paramref name="leftWidthPt"/>/<paramref name="rightWidthPt"/>/<paramref name="bottomHeightPt"/>
    /// default to the shell constants.</summary>
    public static (int Left, int Top, int Right, int Bottom) ViewportInset(
        float scale = 1f, int leftWidthPt = LeftPanelWidth, int rightWidthPt = RightPanelWidth,
        int bottomHeightPt = BottomBarHeight) =>
        (Px(leftWidthPt, scale), Px(TopBarHeight, scale) + Px(SceneHeaderHeight, scale),
            Px(rightWidthPt, scale), Px(bottomHeightPt, scale) + Px(StatusBarHeight, scale));

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
        Math.Max(1, screenHeight - Px(TopBarHeight, scale) - Px(bottomHeightPt, scale) - Px(StatusBarHeight, scale)));

    /// <summary>The center region's <b>Scene panel header</b> band: docked below the top bar, between
    /// the left and right strips, <see cref="SceneHeaderHeight"/> tall — the extra top inset the
    /// viewport gives up. Hosts the transport (UX2-B) and later header controls.</summary>
    public static Rectangle SceneHeader(int screenWidth, int screenHeight, float scale = 1f,
        int leftWidthPt = LeftPanelWidth, int rightWidthPt = RightPanelWidth) => new(
        Px(leftWidthPt, scale),
        Px(TopBarHeight, scale),
        Math.Max(1, screenWidth - Px(leftWidthPt, scale) - Px(rightWidthPt, scale)),
        Px(SceneHeaderHeight, scale));

    /// <summary>The Scene header's <b>tab row</b> (TB-A row 1): the top band, full header width, hosting
    /// the viewport tab strip alone.</summary>
    public static Rectangle SceneHeaderTabRow(Rectangle sceneHeader, float scale = 1f) =>
        new(sceneHeader.X, sceneHeader.Y, sceneHeader.Width, Px(SceneHeaderTabRowHeight, scale));

    /// <summary>The Scene header's <b>tool row</b> (TB-A row 2): the bottom band, below the tab row, full
    /// header width — the left tool cluster and the right transport cluster live here.</summary>
    public static Rectangle SceneHeaderToolRow(Rectangle sceneHeader, float scale = 1f) => new(
        sceneHeader.X,
        sceneHeader.Y + Px(SceneHeaderTabRowHeight, scale),
        sceneHeader.Width,
        Px(SceneHeaderToolRowHeight, scale));

    /// <summary>The right panel strip: between the top bar and the bottom shelf, docked right.
    /// <paramref name="rightWidthPt"/>/<paramref name="bottomHeightPt"/> default to the fixed
    /// constants.</summary>
    public static Rectangle RightPanel(int screenWidth, int screenHeight, float scale = 1f,
        int rightWidthPt = RightPanelWidth, int bottomHeightPt = BottomBarHeight) => new(
        Math.Max(0, screenWidth - Px(rightWidthPt, scale)),
        Px(TopBarHeight, scale),
        Px(rightWidthPt, scale),
        Math.Max(1, screenHeight - Px(TopBarHeight, scale) - Px(bottomHeightPt, scale) - Px(StatusBarHeight, scale)));

    /// <summary>The bottom shelf: full window width, docked ABOVE the status bar (UX3-F — the shelf sits
    /// just above the <see cref="StatusBarHeight"/> strip). <paramref name="bottomHeightPt"/> defaults to
    /// the fixed constant.</summary>
    public static Rectangle BottomBar(int screenWidth, int screenHeight, float scale = 1f,
        int bottomHeightPt = BottomBarHeight) =>
        new(0, Math.Max(0, screenHeight - Px(bottomHeightPt, scale) - Px(StatusBarHeight, scale)),
            Math.Max(1, screenWidth), Px(bottomHeightPt, scale));

    /// <summary>The window <b>status bar</b> strip (UX3-F): full width, <see cref="StatusBarHeight"/>
    /// tall, flush with the window bottom — below the assets shelf. Its band + labels render on the
    /// Editor target; it is part of the ONE viewport inset (the shelf sits above it).</summary>
    public static Rectangle StatusBar(int screenWidth, int screenHeight, float scale = 1f) =>
        new(0, Math.Max(0, screenHeight - Px(StatusBarHeight, scale)),
            Math.Max(1, screenWidth), Px(StatusBarHeight, scale));

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
    /// Lays a right-anchored button cluster in the Scene header's tool row (TB-A row 2 far right): the
    /// <b>camera-view · Play/Pause · Restart · Save</b> cluster, in the given left-to-right order, docked at
    /// the row's RIGHT edge (inset by the row margin) and vertically centered. Returns one rect per width,
    /// in order (so the LAST entry sits at the far-right corner).
    /// </summary>
    public static Rectangle[] SceneHeaderRightCluster(Rectangle toolRow, IReadOnlyList<int> buttonWidths, float scale = 1f)
    {
        var rects = new Rectangle[buttonWidths.Count];
        var height = Px(ButtonHeight, scale);
        var gap = Px(ButtonGap, scale);
        var y = toolRow.Y + (toolRow.Height - height) / 2;
        var total = 0;
        for (var i = 0; i < buttonWidths.Count; i++) total += buttonWidths[i] + (i > 0 ? gap : 0);
        var x = toolRow.Right - Px(RowMarginX, scale) - total;
        for (var i = 0; i < buttonWidths.Count; i++)
        {
            rects[i] = new Rectangle(x, y, buttonWidths[i], height);
            x += buttonWidths[i] + gap;
        }
        return rects;
    }

    // ── The viewport tab strip (TB-A): the tabs own the header's full-width TAB ROW (row 1) ─────────

    /// <summary>A viewport tab's total width (screen pixels) for a <paramref name="labelWidthPx"/> already
    /// measured in screen pixels: symmetric padding + the label + a glyph box for the ▶ play marker
    /// (<paramref name="showPlayMarker"/>) and/or the <c>×</c> close (<paramref name="closable"/>).</summary>
    public static int ViewportTabWidth(float labelWidthPx, bool showPlayMarker, bool closable, float scale = 1f)
    {
        var w = Px(ViewportTabPaddingX, scale) * 2 + (int)MathF.Ceiling(labelWidthPx);
        if (showPlayMarker) w += Px(ViewportTabGlyph + ViewportTabGlyphGap, scale);
        if (closable) w += Px(ViewportTabGlyph + ViewportTabGlyphGap, scale);
        return w;
    }

    /// <summary>Lays the viewport tabs out left-to-right (adjacent, mirroring the panel tab bar) filling the
    /// Scene header's <b>tab row</b> (TB-A row 1), from the left row margin, each tab the FULL tab-row
    /// height (so the active-tab underline sits flush against the tool row below). Pass
    /// <see cref="SceneHeaderTabRow"/> as <paramref name="tabRow"/>. Returns one rect per entry of
    /// <paramref name="tabWidths"/>, in order.</summary>
    public static Rectangle[] ViewportTabRow(Rectangle tabRow, IReadOnlyList<int> tabWidths, float scale = 1f)
    {
        var rects = new Rectangle[tabWidths.Count];
        var x = tabRow.X + Px(RowMarginX, scale);
        for (var i = 0; i < tabWidths.Count; i++)
        {
            rects[i] = new Rectangle(x, tabRow.Y, tabWidths[i], tabRow.Height);
            x += tabWidths[i];
        }
        return rects;
    }

    /// <summary>The ▶ play-marker glyph box in a viewport tab's LEFT gutter (a square in the padding).</summary>
    public static Rectangle ViewportTabPlayMarker(Rectangle tab, float scale = 1f)
    {
        var m = Px(ViewportTabGlyph, scale);
        return new Rectangle(tab.X + Px(ViewportTabPaddingX, scale), tab.Y + (tab.Height - m) / 2, m, m);
    }

    /// <summary>The <c>×</c> close glyph / hit box in a viewport tab's RIGHT gutter (a square in the
    /// padding). Also the click hit-zone the tab-strip system tests for a close.</summary>
    public static Rectangle ViewportTabClose(Rectangle tab, float scale = 1f)
    {
        var m = Px(ViewportTabGlyph, scale);
        return new Rectangle(tab.Right - Px(ViewportTabPaddingX, scale) - m, tab.Y + (tab.Height - m) / 2, m, m);
    }

    /// <summary>Where a viewport tab's LABEL starts (screen pixels) — past the left padding and the ▶
    /// play-marker gutter when present.</summary>
    public static int ViewportTabLabelX(Rectangle tab, bool showPlayMarker, float scale = 1f)
    {
        var x = tab.X + Px(ViewportTabPaddingX, scale);
        if (showPlayMarker) x += Px(ViewportTabGlyph + ViewportTabGlyphGap, scale);
        return x;
    }
}
