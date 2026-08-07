#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Level;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Assets;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.UI;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the top-level <b>WORKSPACE model</b> (WS, issue #47) — the Blender workspace-tab strip at
/// the window top bar's LEFT that switches the whole window between the Level Editor shell and the
/// Autotile Rules view:
///
/// <list type="bullet">
///   <item><b>The strip is the tab language, not a new one:</b> two full-height tabs laid at the top
///   bar's left (active = <see cref="EditorTheme.Bg1"/> fill + an <see cref="EditorTheme.Accent"/>
///   underline), left of the general Undo/Redo/Refresh buttons, which
///   <see cref="EditorChromeLayout.TopBarRightRow"/> now docks at the bar's RIGHT edge so the two
///   clusters cannot collide.</item>
///   <item><b>One switch owner.</b> A tab click dispatches through the composer-supplied switch (the
///   overlay's <c>SetActiveWorkspace</c>) — the strip itself never writes
///   <see cref="EditorShellStateComponent.ActiveWorkspace"/>. Clicking the ACTIVE tab is inert, and a
///   shell drag releasing over a tab dispatches nothing.</item>
///   <item><b>Switching preserves both views' state.</b> Selection, the ONE shared
///   <see cref="EditorHistory"/>, the active layer and the rules view's bound layer / rule set /
///   selected case all survive a round trip — a workspace switch presents different panes, it does not
///   reset an editing session.</item>
///   <item><b>Entering the rules workspace while Playing is refused</b> (it is an editing view):
///   the workspace stays on the Level Editor shell and the designer is told to pause.</item>
/// </list>
///
/// Pure logic — a headless <see cref="ViewportManager"/>, an injected label measure and
/// <c>font: null</c> (the chrome-builder seam), no <c>GraphicsDevice</c>.
///
/// Names the level-editor premises "The editor's top-level WORKSPACES are a tab strip at the window
/// top bar's left, and the Autotile Rules workspace edits rule sets LIVE" (all of the above), "The
/// editor toolbar's buttons drive the same shared editor instances; the chrome is native-resolution on
/// the Editor target …" (the right-docked general buttons) and "The editor shell's region sizes, tabs,
/// and drag ownership live in one shell-state component" (<c>ActiveWorkspace</c>'s home).
/// </summary>
public class WorkspaceTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    private static ViewportManager Vm(int w = 1600, int h = 900, float dpr = 1f) =>
        new(null!, 800, 600) { ScreenWidth = w, ScreenHeight = h, DevicePixelRatio = dpr };

    /// <summary>A deterministic label measure (already LabelScale-adjusted), the
    /// <see cref="EditorShellTests"/> convention: 12 px per character.</summary>
    private static float Measure(string label) => label.Length * 12f;

    private static Texture2D StubTexture()
    {
        var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
        GC.SuppressFinalize(texture);
        return texture;
    }

    /// <summary>
    /// The workspace switch a tab click dispatches into, wired the way <c>EditorOverlay</c> wires it —
    /// the same shape <c>EditorContextMenuTests.MenuOver</c> uses for the menu dispatch, because the
    /// overlay itself is not unit-constructible (it needs a <c>ContentManager</c>,
    /// <c>GraphicsDevice</c>, <c>SpriteBatch</c> and a real <c>BitmapFont</c>). The REAL
    /// <see cref="AutotileRuleEditorSystem"/> and <see cref="EditorNotifications"/> do the work:
    /// entering the Autotile Rules workspace is an EDITING view, refused loud while Playing; leaving
    /// lands back on the Level Editor shell.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        public readonly World World = new();
        public readonly EditorShellStateComponent Shell = new();
        public readonly EditorHistory History;
        public readonly EditorNotifications Notifications = new();
        public readonly AutotileRuleEditorSystem Rules;
        public readonly WorkspaceTabStripSystem Strip;
        public readonly Entity Cursor;
        public bool Suppressed;
        public readonly List<EditorWorkspace> Dispatched = new();

        public Harness(float dpr = 1f)
        {
            History = new EditorHistory(World);
            Cursor = World.CreateEntity();
            Cursor.Set(new CursorInputComponent());
            var textures = new FileAssetTextureLoader(
                _ => new MemoryStream(new byte[] { 0 }), _ => StubTexture(), () => null);
            Rules = new AutotileRuleEditorSystem(World, Vm(dpr: dpr), textures, font: null, Shell, History,
                notify: (message, severity) => Notifications.Notify(message, severity));
            Strip = new WorkspaceTabStripSystem(World, Vm(dpr: dpr), Measure, Shell,
                switchWorkspace: SetActiveWorkspace,
                isInputSuppressed: () => Suppressed);
        }

        /// <summary>The ONE workspace switch (mirrors <c>EditorOverlay.SetActiveWorkspace</c>).</summary>
        public void SetActiveWorkspace(EditorWorkspace workspace, GameState state)
        {
            Dispatched.Add(workspace);
            if (Shell.ActiveWorkspace == workspace) return;
            if (workspace == EditorWorkspace.AutotileRules)
            {
                if (state.RunMode == RunMode.Play)
                {
                    Notifications.Notify("Pause to edit autotile rules", EditorNotifySeverity.Warning);
                    return;
                }
                Rules.OpenWorkspace();
                return;
            }
            Rules.Close();
        }

        public void ClickAt(Point p)
        {
            ref var input = ref Cursor.Get<CursorInputComponent>();
            input.ScreenPosition = new Vector2(p.X, p.Y);
            input.LeftButtonReleased = true;
        }

        public void ReleaseCursor()
        {
            ref var input = ref Cursor.Get<CursorInputComponent>();
            input.LeftButtonReleased = false;
        }

        public void Dispose()
        {
            Rules.Dispose();
            Strip.Dispose();
            World.Dispose();
        }
    }

    // ═══ The strip: two tabs at the top bar's LEFT, active underlined, right of nothing ═════════════

    [Fact]
    public void WorkspaceTabs_AreTwoAdjacentTabsAtTheTopBarsLeft_TheActiveOneUnderlined()
    {
        using var h = new Harness();

        h.Strip.Update(Edit());

        var bar = EditorChromeLayout.TopBar(1600, 1f);
        var tabs = h.Strip.TabRects();
        Assert.Equal(2, tabs.Length);
        Assert.True(bar.Contains(tabs[0]), $"the Level Editor tab {tabs[0]} escapes the top bar {bar}");
        Assert.True(bar.Contains(tabs[1]), $"the Autotile Rules tab {tabs[1]} escapes the top bar {bar}");
        // Anchored at the bar's LEFT (its row margin), full bar height so the underline sits flush.
        Assert.Equal(bar.X + EditorChromeLayout.Px(EditorChromeLayout.RowMarginX, 1f), tabs[0].X);
        Assert.Equal(bar.Height, tabs[0].Height);
        Assert.True(tabs[1].Right < bar.Center.X, "the workspace tabs belong to the bar's LEFT half");
        Assert.Equal(tabs[0].Right, tabs[1].X);  // adjacent, no gutter
        // The wider label gets the wider tab (label-width + padding, the one tab-width formula).
        Assert.True(tabs[1].Width > tabs[0].Width);
    }

    [Fact]
    public void TopBarGeneralButtons_DockAtTheRightEdge_ClearOfTheWorkspaceTabs()
    {
        // WS moved the top bar's LEFT to the workspace tabs, so the general buttons (Undo / Redo /
        // Refresh) right-anchor. Overlap here would make one cluster silently un-clickable.
        using var h = new Harness();
        h.Strip.Update(Edit());

        var widths = new[] { 40, 40, 60 };
        var buttons = EditorChromeLayout.TopBarRightRow(1600, widths, 1f);
        var bar = EditorChromeLayout.TopBar(1600, 1f);

        Assert.Equal(3, buttons.Length);
        Assert.All(buttons, b => Assert.True(bar.Contains(b), $"button {b} escapes the top bar {bar}"));
        Assert.True(buttons[^1].Right <= bar.Right);
        Assert.True(buttons[^1].Right > bar.Center.X, "the general buttons must dock at the bar's RIGHT");
        Assert.True(buttons[0].X > h.Strip.TabRects()[^1].Right,
            "the general buttons must start after the workspace tab strip");
    }

    [Fact]
    public void ClickingTheInactiveWorkspaceTab_DispatchesTheSwitch_AndTheActiveTabIsInert()
    {
        using var h = new Harness();
        h.Strip.Update(Edit());

        h.ClickAt(h.Strip.TabRects()[1].Center); // Autotile Rules
        h.Strip.Update(Edit());

        Assert.Equal(new[] { EditorWorkspace.AutotileRules }, h.Dispatched);
        Assert.Equal(EditorWorkspace.AutotileRules, h.Shell.ActiveWorkspace);
        Assert.True(h.Rules.IsOpen); // IsOpen DERIVES from the shell state — no second flag

        // Clicking the now-ACTIVE tab dispatches nothing (a re-entry would re-run the switch's meaning).
        h.ClickAt(h.Strip.TabRects()[1].Center);
        h.Strip.Update(Edit());
        Assert.Single(h.Dispatched);

        // …and the other tab switches back.
        h.ClickAt(h.Strip.TabRects()[0].Center);
        h.Strip.Update(Edit());
        Assert.Equal(EditorWorkspace.LevelEditor, h.Shell.ActiveWorkspace);
        Assert.False(h.Rules.IsOpen);
    }

    [Fact]
    public void WorkspaceTabs_SuppressedDuringAShellDrag_DispatchNothing()
    {
        using var h = new Harness { Suppressed = true };
        h.Strip.Update(Edit());

        h.ClickAt(h.Strip.TabRects()[1].Center);
        h.Strip.Update(Edit());

        Assert.Empty(h.Dispatched); // a splitter drag releasing over a tab must not switch workspaces
        Assert.Equal(EditorWorkspace.LevelEditor, h.Shell.ActiveWorkspace);
    }

    // ═══ Refusal: the rules workspace is an EDITING view ════════════════════════════════════════════

    [Fact]
    public void EnteringAutotileRulesWhilePlaying_IsRefusedLoudly_TheWorkspaceStaysOnTheLevelEditor()
    {
        using var h = new Harness();
        h.Strip.Update(Play()); // the strip stays live in BOTH transport states

        h.ClickAt(h.Strip.TabRects()[1].Center);
        h.Strip.Update(Play());

        Assert.Equal(EditorWorkspace.LevelEditor, h.Shell.ActiveWorkspace);
        Assert.False(h.Rules.IsOpen);
        Assert.True(h.Notifications.TryGetCurrent(out var message, out var severity));
        Assert.Contains("Pause", message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EditorNotifySeverity.Warning, severity);

        // Pausing lets the same click through — the refusal is a guard, not a broken state machine.
        h.ReleaseCursor();
        h.Strip.Update(Edit());
        h.ClickAt(h.Strip.TabRects()[1].Center);
        h.Strip.Update(Edit());
        Assert.Equal(EditorWorkspace.AutotileRules, h.Shell.ActiveWorkspace);
    }

    // ═══ A switch preserves BOTH views' editing state ═══════════════════════════════════════════════

    [Fact]
    public void WorkspaceRoundTrip_PreservesSelection_History_ActiveLayer_AndTheBoundRuleSet()
    {
        using var h = new Harness();

        // A live Level-Editor session: a Paint layer with two rule sets, one selected entity, an
        // active layer and two undoable edits already on the shared history.
        var data = new TileGridComponent { CellSize = 32f };
        var rock = new TilePaintValue { Id = 1, Name = "Rock", TilesetKey = "file:a.png", TileSize = 32 };
        var sand = new TilePaintValue { Id = 2, Name = "Sand", TilesetKey = "file:b.png", TileSize = 16 };
        data.Values.Add(rock);
        var layer = h.World.CreateEntity();
        layer.Set(new TransformComponent(Vector2.Zero));
        layer.Set(new EntityInfoComponent("Layer", "Terrain"));
        layer.Set(new SceneLayerComponent { Order = 0 });
        layer.Set(data);
        h.Shell.ActiveLayer = layer;

        var selected = h.World.CreateEntity();
        selected.Set(new TransformComponent(new Vector2(12, 34)));
        selected.Set(new EntityInfoComponent("Prop", "rock"));
        selected.Set(new SceneObjectComponent());
        selected.Set(new SelectedComponent());

        h.History.Push(new AddPaintValueCommand(layer, sand));
        h.Rules.SelectValue(layer, sand.Id);
        h.Rules.SelectCase(6);
        h.History.Push(PaintValueEditCommand.Rules(layer, sand, "6:1,1 15:0,0"));
        Assert.Equal(2, h.History.Count);
        var versionBefore = h.History.EditVersion;

        // → Autotile Rules, run a frame there, → back to the Level Editor, run a frame there.
        h.Strip.Update(Edit());
        h.ClickAt(h.Strip.TabRects()[1].Center);
        h.Strip.Update(Edit());
        h.Rules.Update(Edit());
        h.ReleaseCursor();
        Assert.Equal(EditorWorkspace.AutotileRules, h.Shell.ActiveWorkspace);

        h.Strip.Update(Edit());
        h.ClickAt(h.Strip.TabRects()[0].Center);
        h.Strip.Update(Edit());
        h.Rules.Update(Edit());
        h.ReleaseCursor();
        Assert.Equal(EditorWorkspace.LevelEditor, h.Shell.ActiveWorkspace);

        // The Level-Editor side survived: selection, the ONE history (nothing pushed, nothing dropped),
        // the active layer.
        Assert.True(selected.IsAlive);
        Assert.True(selected.Has<SelectedComponent>());
        Assert.Equal(2, h.History.Count);
        Assert.Equal(versionBefore, h.History.EditVersion); // a switch is not an edit
        Assert.Equal(layer, h.Shell.ActiveLayer);

        // The rules side survived: the bound layer + rule set…
        Assert.Equal(layer, h.Rules.CurrentLayer);
        Assert.Equal((byte)2, h.Rules.CurrentValueId);

        // …and the SELECTED CASE, observed the only way it is observable — the next tile toggle still
        // lands on case 6 rather than resetting to the interior default.
        h.Rules.ToggleTile(4, 4);
        Assert.Equal(3, h.History.Count);
        var table = MonoDreams.LevelEditor.Tile.TileGridBaking.ParseRules(sand.AutotileRules);
        Assert.Equal(new[] { new Point(1, 1), new Point(4, 4) }, table[6]);
        Assert.Equal(new Point(0, 0), Assert.Single(table[15])); // the interior case untouched

        // And the whole session still undoes as one stack through the ONE shared history.
        h.History.Undo();
        h.History.Undo();
        h.History.Undo();
        Assert.Null(data.FindValue(2));
        Assert.False(h.History.CanUndo);
    }

    [Fact]
    public void LeavingTheRulesWorkspace_ParksItsChrome_AndTheLevelEditorTabIsActiveAgain()
    {
        // The workspace is a VIEW, not a modal: leaving it must take its full-screen panes off the
        // Editor target (parked far off-screen) rather than leaving them over the shell.
        using var h = new Harness();
        h.Rules.OpenWorkspace();
        h.Rules.Update(Edit());
        Assert.Contains(RulesChromePositions(h.World), p => p != SystemsPanelLayout.ParkedPosition);

        h.Rules.Close();
        h.Rules.Update(Edit());

        Assert.Equal(EditorWorkspace.LevelEditor, h.Shell.ActiveWorkspace);
        Assert.All(RulesChromePositions(h.World), p => Assert.Equal(SystemsPanelLayout.ParkedPosition, p));

        h.Strip.Update(Edit());
        var underlines = 0;
        using var meshes = h.World.GetEntities().With<DrawComponent>().AsSet();
        foreach (var e in meshes.GetEntities())
        {
            var draw = e.Get<DrawComponent>();
            if (draw.Type == DrawElementType.Mesh && draw.LayerDepth == EditorTheme.Depths.TabUnderline
                && draw.Vertices is { Length: > 0 })
                underlines++;
        }
        Assert.Equal(1, underlines); // exactly ONE workspace tab is underlined at a time
    }

    /// <summary>Every workspace-chrome entity's position — the parked ones sit at
    /// <see cref="SystemsPanelLayout.ParkedPosition"/>.</summary>
    private static List<Vector2> RulesChromePositions(World world)
    {
        var positions = new List<Vector2>();
        using var set = world.GetEntities()
            .With<EditorInfrastructureComponent>()
            .With<SimpleButtonComponent>()
            .With<TransformComponent>()
            .AsSet();
        foreach (var e in set.GetEntities()) positions.Add(e.Get<TransformComponent>().Position);
        return positions;
    }
}
