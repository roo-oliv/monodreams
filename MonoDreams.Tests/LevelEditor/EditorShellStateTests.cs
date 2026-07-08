#nullable enable
using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.LevelEditor.Channel;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.UI;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.System;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the UX-B shell-state model: the ONE source of the resizable region sizes (clamped),
/// the active tab per region, and the drag ownership. Covers the pure clamp math, tab switching,
/// the tab-strip / splitter / scrollbar geometry (with DPR-2 doubling of every new metric — the
/// pre-mortem #8 guard), the live splitter drag through the <see cref="EditorShellSystem"/>, the
/// drag-exclusion (a splitter/scrollbar drag never also fires a panel click), and the headless
/// <c>shell:*</c> / <c>panel:tab</c> ops reaching the named dispatch.
/// </summary>
public class EditorShellStateTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static float Measure(string label) => label.Length * 12f;

    // ---- Region-size clamp -------------------------------------------------

    [Fact]
    public void RegionSizes_DefaultToTheChromeLayoutConstants()
    {
        var state = new EditorShellStateComponent();
        Assert.Equal(EditorChromeLayout.RightPanelWidth, state.RightWidthPt);
        Assert.Equal(EditorChromeLayout.BottomBarHeight, state.BottomHeightPt);
        Assert.Equal(EditorShellStateComponent.DefaultRightWidthPt, EditorChromeLayout.RightPanelWidth);
        Assert.Equal(EditorShellStateComponent.DefaultBottomHeightPt, EditorChromeLayout.BottomBarHeight);
    }

    [Fact]
    public void RightWidth_ClampsToItsRange()
    {
        var state = new EditorShellStateComponent { RightWidthPt = 10 };
        Assert.Equal(EditorShellStateComponent.MinRightWidthPt, state.RightWidthPt);
        state.RightWidthPt = 99999;
        Assert.Equal(EditorShellStateComponent.MaxRightWidthPt, state.RightWidthPt);
        state.RightWidthPt = 400;
        Assert.Equal(400, state.RightWidthPt);
    }

    [Fact]
    public void BottomHeight_ClampsToItsRange()
    {
        var state = new EditorShellStateComponent { BottomHeightPt = 0 };
        Assert.Equal(EditorShellStateComponent.MinBottomHeightPt, state.BottomHeightPt);
        state.BottomHeightPt = 99999;
        Assert.Equal(EditorShellStateComponent.MaxBottomHeightPt, state.BottomHeightPt);
        state.BottomHeightPt = 200;
        Assert.Equal(200, state.BottomHeightPt);
    }

    [Fact]
    public void MarkedTerrain_ReservesLeftAndMenuBarAtZero_AndAssignsRegions()
    {
        var state = new EditorShellStateComponent();
        Assert.Equal(0, state.LeftWidthPt);
        Assert.Equal(0, state.MenuBarHeightPt);
        Assert.Empty(state.RegionPanels[EditorRegion.Left]);
        Assert.Empty(state.RegionPanels[EditorRegion.MenuBar]);
        Assert.Equal(new[] { EditorPanelKind.Scene, EditorPanelKind.Systems, EditorPanelKind.Project },
            state.RegionPanels[EditorRegion.Right]);
        Assert.Equal(new[] { EditorPanelKind.Assets }, state.RegionPanels[EditorRegion.Bottom]);
    }

    // ---- Region rects honour the runtime sizes -----------------------------

    [Fact]
    public void ViewportInset_UsesTheRuntimeRegionSizes()
    {
        Assert.Equal((0, 44, 400, 200), EditorChromeLayout.ViewportInset(1f, 400, 200));
        // DPR-2 doubles the custom sizes too.
        Assert.Equal((0, 88, 800, 400), EditorChromeLayout.ViewportInset(2f, 400, 200));
    }

    [Fact]
    public void RightPanelAndBottomBar_UseTheRuntimeSizes()
    {
        var right = EditorChromeLayout.RightPanel(1600, 900, 1f, 400, 200);
        Assert.Equal(new Rectangle(1200, 44, 400, 900 - 44 - 200), right);
        var bottom = EditorChromeLayout.BottomBar(1600, 900, 1f, 200);
        Assert.Equal(new Rectangle(0, 700, 1600, 200), bottom);
    }

    // ---- Tab strip + body geometry (DPR-2 doubles) -------------------------

    [Fact]
    public void TabStrip_AndBody_SplitTheRegion()
    {
        var panel = new Rectangle(1320, 44, 280, 688);
        var strip = EditorChromeLayout.TabStrip(panel);
        Assert.Equal(new Rectangle(1320, 44, 280, EditorChromeLayout.TabStripHeight), strip);
        var body = EditorChromeLayout.RegionBody(panel);
        Assert.Equal(new Rectangle(1320, 44 + EditorChromeLayout.TabStripHeight, 280, 688 - EditorChromeLayout.TabStripHeight), body);
        // Body + strip cover the region with no gap/overlap.
        Assert.Equal(panel.Height, strip.Height + body.Height);
    }

    [Fact]
    public void TabStrip_AtDpr2_DoublesHeight()
    {
        var panel = new Rectangle(2640, 88, 560, 1376);
        Assert.Equal(EditorChromeLayout.TabStripHeight * 2, EditorChromeLayout.TabStrip(panel, 2f).Height);
        Assert.Equal(EditorChromeLayout.TabStripHeight * 2, panel.Height - EditorChromeLayout.RegionBody(panel, 2f).Height);
    }

    [Fact]
    public void TabRow_LaysTabsAfterTheSplitterGutter_UnderlineOnTheBottomEdge()
    {
        var strip = new Rectangle(1320, 44, 280, 26);
        var rects = EditorChromeLayout.TabRow(strip, new[] { 60, 80, 70 });
        Assert.Equal(strip.X + EditorChromeLayout.SplitterThickness, rects[0].X); // clear of the splitter
        Assert.Equal(rects[0].Right, rects[1].X);
        Assert.Equal(rects[1].Right, rects[2].X);
        Assert.All(rects, r => Assert.Equal(strip.Height, r.Height));
        var underline = EditorChromeLayout.TabUnderline(rects[0]);
        Assert.Equal(EditorChromeLayout.TabUnderlineHeight, underline.Height);
        Assert.Equal(rects[0].Bottom - EditorChromeLayout.TabUnderlineHeight, underline.Y);
        // DPR-2: underline thickness + gutter offset double.
        var rects2 = EditorChromeLayout.TabRow(new Rectangle(2640, 88, 560, 52), new[] { 120 }, 2f);
        Assert.Equal(2640 + EditorChromeLayout.SplitterThickness * 2, rects2[0].X);
        Assert.Equal(EditorChromeLayout.TabUnderlineHeight * 2, EditorChromeLayout.TabUnderline(rects2[0], 2f).Height);
    }

    // ---- Splitter geometry (DPR-2 doubles) ---------------------------------

    [Fact]
    public void RightSplitter_IsOnTheLeftEdge_BottomSplitterOnTheTopEdge()
    {
        var right = EditorChromeLayout.RightSplitter(1600, 900, 1f, 280, 168);
        var panel = EditorChromeLayout.RightPanel(1600, 900, 1f, 280, 168);
        Assert.Equal(panel.X, right.X);          // viewport-facing (left) edge
        Assert.Equal(EditorChromeLayout.SplitterThickness, right.Width);
        Assert.Equal(panel.Height, right.Height);

        var bottom = EditorChromeLayout.BottomSplitter(1600, 900, 1f, 168);
        var bar = EditorChromeLayout.BottomBar(1600, 900, 1f, 168);
        Assert.Equal(bar.Y, bottom.Y);           // viewport-facing (top) edge
        Assert.Equal(EditorChromeLayout.SplitterThickness, bottom.Height);
        Assert.Equal(bar.Width, bottom.Width);
    }

    [Fact]
    public void Splitters_AtDpr2_DoubleThickness()
    {
        Assert.Equal(EditorChromeLayout.SplitterThickness * 2,
            EditorChromeLayout.RightSplitter(3840, 2160, 2f, 280, 168).Width);
        Assert.Equal(EditorChromeLayout.SplitterThickness * 2,
            EditorChromeLayout.BottomSplitter(3840, 2160, 2f, 168).Height);
    }

    // ---- Scrollbar geometry (DPR-2 doubles) --------------------------------

    [Fact]
    public void Scrollbar_HiddenWhenContentFits_ShownWhenItOverflows()
    {
        Assert.False(EditorScrollbar.NeedsScrollbar(5, 10));
        Assert.False(EditorScrollbar.NeedsScrollbar(10, 10));
        Assert.True(EditorScrollbar.NeedsScrollbar(20, 10));
    }

    [Fact]
    public void Scrollbar_TrackOnTheRightEdge_ThumbProportionalAndRoundTrips()
    {
        var body = new Rectangle(1320, 70, 280, 600);
        var track = EditorScrollbar.Track(body);
        Assert.Equal(EditorScrollbar.Width, track.Width);
        Assert.Equal(body.Right - EditorScrollbar.Width - EditorScrollbar.Margin, track.X);

        const int total = 40, visible = 10;
        var thumb = EditorScrollbar.Thumb(track, total, visible, scroll: 0);
        // Proportional: 10/40 of the track (>= MinThumb), at the top when scroll = 0.
        Assert.Equal(track.Y, thumb.Y);
        Assert.True(thumb.Height < track.Height);

        // At max scroll the thumb bottoms out.
        var max = total - visible;
        var atMax = EditorScrollbar.Thumb(track, total, visible, max);
        Assert.Equal(track.Bottom, atMax.Bottom);

        // Dragging the thumb top to the track bottom maps back to max scroll; to the top → 0.
        Assert.Equal(max, EditorScrollbar.ScrollFromThumbTop(track, total, visible, track.Bottom));
        Assert.Equal(0, EditorScrollbar.ScrollFromThumbTop(track, total, visible, track.Y));
    }

    [Fact]
    public void Scrollbar_AtDpr2_DoublesWidthAndMargin()
    {
        var body = new Rectangle(2640, 140, 560, 1200);
        var track = EditorScrollbar.Track(body, 2f);
        Assert.Equal(EditorScrollbar.Width * 2, track.Width);
        Assert.Equal(body.Right - EditorScrollbar.Width * 2 - EditorScrollbar.Margin * 2, track.X);
    }

    // ---- Tab switching -----------------------------------------------------

    [Fact]
    public void HostTab_ActivatesTheTabThatHostsASectionOp()
    {
        using var world = new World();
        var vm = new ViewportManager(null, 800, 600) { ScreenWidth = 1600, ScreenHeight = 900 };
        var shell = new EditorShellStateComponent();
        using var panel = new EditorPanelSystem(world, vm, font: null,
            () => ((MonoDreams.LevelEditor.Composition.EditorPipelineRegistrar?)null, null), shell);

        Assert.Equal(EditorRightTab.Scene, shell.ActiveRightTab); // default

        panel.ToggleSection(EditorPanelSection.Systems);          // a Systems-section op...
        Assert.Equal(EditorRightTab.Systems, shell.ActiveRightTab); // ...activates the Systems tab

        panel.ToggleSection(EditorPanelSection.Inspector);        // Inspector lives in the Scene tab
        Assert.Equal(EditorRightTab.Scene, shell.ActiveRightTab);

        panel.SetRightTab(EditorRightTab.Project);
        Assert.Equal(EditorRightTab.Project, shell.ActiveRightTab);
    }

    // ---- Live splitter drag through the shell system -----------------------

    private static (EditorShellSystem shell, ViewportManager vm, EditorShellStateComponent state, Entity cursor)
        MakeShell(World world)
    {
        var vm = new ViewportManager(null, 800, 600) { ScreenWidth = 1600, ScreenHeight = 900 };
        var chrome = new EditorChromeBuilder(world, Measure);
        chrome.Build(1600, 900);
        var state = new EditorShellStateComponent();
        var shell = new EditorShellSystem(world, vm, chrome, null, state);
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent());
        return (shell, vm, state, cursor);
    }

    [Fact]
    public void RightSplitterDrag_GrowsTheStrip_ThenReleasesTheToken()
    {
        using var world = new World();
        var (shell, _, state, cursor) = MakeShell(world);
        using var _1 = shell;

        var zone = EditorChromeLayout.RightSplitter(1600, 900, 1f, state.RightWidthPt, state.BottomHeightPt);
        ref var input = ref cursor.Get<CursorInputComponent>();

        // Press on the splitter's left edge.
        input.ScreenPosition = new Vector2(zone.Center.X, zone.Center.Y);
        input.LeftButton = true;
        input.LeftButtonPressed = true;
        shell.Update(Edit());
        Assert.Equal(ShellDragKind.RightSplitter, state.ActiveDrag);

        // Drag 100px to the LEFT → the strip grows by 100pt (DPR 1).
        input.LeftButtonPressed = false;
        input.ScreenPosition = new Vector2(zone.Center.X - 100, zone.Center.Y);
        shell.Update(Edit());
        Assert.Equal(EditorShellStateComponent.DefaultRightWidthPt + 100, state.RightWidthPt);

        // Release: the final width holds; the token is still owned this frame (holds through release).
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        shell.Update(Edit());
        Assert.Equal(EditorShellStateComponent.DefaultRightWidthPt + 100, state.RightWidthPt);
        Assert.Equal(ShellDragKind.RightSplitter, state.ActiveDrag);

        // Next frame (button fully up) → the token clears.
        input.LeftButtonReleased = false;
        shell.Update(Edit());
        Assert.Equal(ShellDragKind.None, state.ActiveDrag);
    }

    [Fact]
    public void SplitterDrag_ClampsAtTheMaxWidth()
    {
        using var world = new World();
        var (shell, _, state, cursor) = MakeShell(world);
        using var _1 = shell;

        var zone = EditorChromeLayout.RightSplitter(1600, 900, 1f, state.RightWidthPt, state.BottomHeightPt);
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(zone.Center.X, zone.Center.Y);
        input.LeftButton = true;
        input.LeftButtonPressed = true;
        shell.Update(Edit());

        input.LeftButtonPressed = false;
        input.ScreenPosition = new Vector2(zone.Center.X - 5000, zone.Center.Y); // way past max
        shell.Update(Edit());
        Assert.Equal(EditorShellStateComponent.MaxRightWidthPt, state.RightWidthPt);
    }

    // ---- Drag exclusion: a foreign drag mutes panel clicks -----------------

    [Fact]
    public void PanelClick_IsMuted_WhileAForeignDragOwnsThePointer()
    {
        using var world = new World();
        var vm = new ViewportManager(null, 800, 600) { ScreenWidth = 1600, ScreenHeight = 900 };
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent());
        var shell = new EditorShellStateComponent();
        using var panel = new EditorPanelSystem(world, vm, font: null,
            () => ((MonoDreams.LevelEditor.Composition.EditorPipelineRegistrar?)null, null), shell);
        panel.SetRightTab(EditorRightTab.Scene);

        panel.Update(Edit());
        var sceneHeaderIdx = -1;
        for (var i = 0; i < panel.Rows.Count; i++)
            if (panel.Rows[i].Kind == PanelRowKind.SectionHeader && panel.Rows[i].Section == EditorPanelSection.Scene)
                sceneHeaderIdx = i;
        Assert.True(sceneHeaderIdx >= 0);

        // Simulate the bottom-shelf scrollbar drag owning the pointer.
        shell.ActiveDrag = ShellDragKind.BottomScrollbar;

        var body = EditorChromeLayout.RegionBody(EditorChromeLayout.RightPanel(1600, 900, 1f,
            shell.RightWidthPt, shell.BottomHeightPt));
        var line = SystemsPanelLayout.LineRect(body, sceneHeaderIdx - panel.ScrollOffset);
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(line.Center.X, line.Center.Y);
        input.LeftButtonReleased = true;
        panel.Update(Edit());

        // The foreign drag muted the click — the Scene section did not collapse.
        Assert.False(panel.State.SceneCollapsed);
    }

    // ---- Panel scrollbar-thumb drag scrolls the rows -----------------------

    [Fact]
    public void PanelScrollbarThumbDrag_ScrollsTheRows()
    {
        using var world = new World();
        var vm = new ViewportManager(null, 800, 600) { ScreenWidth = 1600, ScreenHeight = 300 };
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent());
        var shell = new EditorShellStateComponent();
        var update = new MonoDreams.LevelEditor.Composition.EditorPipelineRegistrar();
        for (var i = 0; i < 30; i++) update.Add($"s{i}", new NoopSystem(), EditTimeBehavior.RunNormally);
        update.Build();
        using var panel = new EditorPanelSystem(world, vm, font: null, () => (update, null), shell);
        panel.SetRightTab(EditorRightTab.Systems);

        panel.Update(Edit());
        Assert.Equal(0, panel.ScrollOffset);

        var body = EditorChromeLayout.RegionBody(EditorChromeLayout.RightPanel(1600, 300, 1f,
            shell.RightWidthPt, shell.BottomHeightPt));
        var track = EditorScrollbar.Track(body);
        var thumb = EditorScrollbar.Thumb(track, panel.Rows.Count,
            SystemsPanelLayout.VisibleLineCount(body), 0);

        ref var input = ref cursor.Get<CursorInputComponent>();
        // Press on the thumb.
        input.ScreenPosition = new Vector2(thumb.Center.X, thumb.Center.Y);
        input.LeftButton = true;
        input.LeftButtonPressed = true;
        panel.Update(Edit());
        Assert.Equal(ShellDragKind.RightScrollbar, shell.ActiveDrag);

        // Drag to the track bottom → scroll jumps to max.
        input.LeftButtonPressed = false;
        input.ScreenPosition = new Vector2(thumb.Center.X, track.Bottom);
        panel.Update(Edit());
        Assert.Equal(SystemsPanelLayout.MaxScroll(panel.Rows.Count, body), panel.ScrollOffset);
    }

    // ---- Headless ops reach the named dispatch -----------------------------

    [Fact]
    public void HeadlessShellOps_ReachTheNamedDispatch()
    {
        using var world = new World();
        var cursor = world.CreateEntity();
        cursor.Set(new CursorInputComponent());

        var received = new List<string>();
        var plan = new EditorOpPlan
        {
            Ops = new List<EditorOp>
            {
                new() { Frame = 0, Kind = EditorOpKind.ToolbarAction, Action = "panel:tab systems" },
                new() { Frame = 1, Kind = EditorOpKind.ToolbarAction, Action = "shell:right 400" },
                new() { Frame = 2, Kind = EditorOpKind.ToolbarAction, Action = "shell:bottom 200" },
            },
        };
        using var driver = new EditorOpReplaySystem(world, plan,
            dispatch: null, requestExit: null, transport: null,
            dispatchNamed: (name, _) => received.Add(name));

        var state = Edit();
        for (var i = 0; i < 4; i++) driver.Update(state);

        Assert.Equal(new[] { "panel:tab systems", "shell:right 400", "shell:bottom 200" }, received);
    }

    private sealed class NoopSystem : DefaultEcs.System.ISystem<GameState>
    {
        public bool IsEnabled { get; set; } = true;
        public void Update(GameState state) { }
        public void Dispose() { }
    }
}
