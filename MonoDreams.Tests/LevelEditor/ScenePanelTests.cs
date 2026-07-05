#nullable enable
using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.UI;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.System;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the right-column panel's new sections (this wave): the three collapsible sections
/// (SYSTEMS/SCENE/INSPECTOR), the SYSTEMS group collapse, the SCENE entity tree with two-way
/// selection (<c>SelectedComponent</c>), and the INSPECTOR component list + member-value display.
/// Pure logic — a null font (layout-only) + a hand-built <see cref="ViewportManager"/>.
/// </summary>
public class ScenePanelTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };

    private static ViewportManager Vm(int width = 1600, int height = 1200) =>
        new(null, 800, 600) { ScreenWidth = width, ScreenHeight = height };

    /// <summary>A cursor tagged editor-infrastructure (like the real overlay-provided cursor) so it
    /// is readable for hit-testing but stays out of the SCENE tree.</summary>
    private static Entity MakeCursor(World world)
    {
        var cursor = world.CreateEntity();
        cursor.Set(new EditorInfrastructureComponent());
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent());
        return cursor;
    }

    private static SystemsPanelSystem MakePanel(World world, ViewportManager vm,
        EditorPipelineRegistrar? update = null, EditorPipelineRegistrar? draw = null) =>
        new(world, vm, font: null, () => (update, draw));

    private static Rectangle Panel(ViewportManager vm) =>
        EditorChromeLayout.RightPanel(vm.ScreenWidth, vm.ScreenHeight);

    /// <summary>The label texts currently laid out inside the strip (not parked).</summary>
    private static List<string> VisibleLabels(World world, Rectangle panelRect)
    {
        var texts = new List<string>();
        using var set = world.GetEntities().With<DynamicTextComponent>().AsSet();
        foreach (var e in set.GetEntities())
        {
            var pos = e.Get<TransformComponent>().Position;
            if (panelRect.Contains(new Point((int)pos.X, (int)pos.Y)))
                texts.Add(e.Get<DynamicTextComponent>().TextContent);
        }
        return texts;
    }

    private static Color LabelColor(World world, string text)
    {
        using var set = world.GetEntities().With<DynamicTextComponent>().AsSet();
        foreach (var e in set.GetEntities())
        {
            ref readonly var t = ref e.Get<DynamicTextComponent>();
            if (t.TextContent == text) return t.Color;
        }
        throw new KeyNotFoundException($"no label '{text}'");
    }

    // ---- SCENE tree ----

    [Fact]
    public void SceneSection_ListsGameEntities_HidesInfrastructure()
    {
        using var world = new World();
        MakeCursor(world);
        var player = world.CreateEntity();
        player.Set(new EntityInfoComponent("Player"));
        var infra = world.CreateEntity();
        infra.Set(new EntityInfoComponent("Chrome"));
        infra.Set(new EditorInfrastructureComponent());
        using var panel = MakePanel(world, Vm());

        panel.Update(Edit());

        var labels = VisibleLabels(world, Panel(Vm()));
        Assert.Contains("SCENE", labels);
        Assert.Contains("Player", labels);
        Assert.DoesNotContain("Chrome", labels);
    }

    [Fact]
    public void Section_Collapse_HidesTheBody_ButNotTheHeader()
    {
        using var world = new World();
        MakeCursor(world);
        var player = world.CreateEntity();
        player.Set(new EntityInfoComponent("Player"));
        using var panel = MakePanel(world, Vm());

        panel.Update(Edit());
        Assert.Contains("Player", VisibleLabels(world, Panel(Vm())));
        var expanded = panel.DisplayedLineCount;

        panel.ToggleSection(PanelSection.Scene); // collapse SCENE
        panel.Update(Edit());

        var labels = VisibleLabels(world, Panel(Vm()));
        Assert.Contains("SCENE", labels);            // header stays
        Assert.DoesNotContain("Player", labels);     // body hidden
        Assert.True(panel.DisplayedLineCount < expanded);
    }

    // ---- Two-way selection ----

    [Fact]
    public void TreeRowClick_SelectsTheEntity_SettingSelectedComponent()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var player = world.CreateEntity();
        player.Set(new EntityInfoComponent("Player"));
        using var panel = MakePanel(world, vm);

        panel.Update(Edit()); // build + lay out: SYSTEMS(0), SCENE(1), Player(2), INSPECTOR(3), (no selection)(4)

        var row = SystemsPanelLayout.LineRect(Panel(vm), 2);
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(row.Center.X, row.Center.Y);
        input.LeftButtonReleased = true;
        panel.Update(Edit());

        Assert.True(player.Has<SelectedComponent>());
    }

    [Fact]
    public void ExternalSelection_HighlightsTheTreeRow()
    {
        using var world = new World();
        MakeCursor(world);
        var player = world.CreateEntity();
        player.Set(new EntityInfoComponent("Player"));
        using var panel = MakePanel(world, Vm());

        panel.Update(Edit());
        Assert.Equal(EditorChromeBuilder.LabelColor, LabelColor(world, "Player"));

        // A viewport pick (SelectionSystem) sets SelectedComponent externally → the tree highlights it.
        player.Set(new SelectedComponent());
        panel.Update(Edit());

        Assert.Equal(EditorChromeBuilder.SelectedLabelColor, LabelColor(world, "Player"));
    }

    [Fact]
    public void SelectEntityByLabel_SetsSelection_SingleSelect()
    {
        using var world = new World();
        var a = world.CreateEntity();
        var b = world.CreateEntity();
        a.Set(new EntityInfoComponent("A"));
        b.Set(new EntityInfoComponent("B"));
        using var panel = MakePanel(world, Vm());
        panel.Update(Edit());

        Assert.True(panel.SelectEntityByLabel("A"));
        Assert.True(a.Has<SelectedComponent>());

        // Single-select: selecting B clears A.
        Assert.True(panel.SelectEntityByLabel("B"));
        Assert.True(b.Has<SelectedComponent>());
        Assert.False(a.Has<SelectedComponent>());

        Assert.False(panel.SelectEntityByLabel("nope")); // unknown label → no-op
    }

    // ---- INSPECTOR: component list + member values ----

    [Fact]
    public void Inspector_ShowsSelectedEntityComponentTypeNames()
    {
        using var world = new World();
        var widget = world.CreateEntity();
        widget.Set(new EntityInfoComponent("Widget"));
        widget.Set(new MixedInspectorComponent { Health = 7 });
        using var panel = MakePanel(world, Vm());

        panel.Update(Edit());
        panel.SelectEntityByLabel("Widget");
        panel.Update(Edit());

        var labels = VisibleLabels(world, Panel(Vm()));
        Assert.Contains("INSPECTOR", labels);
        Assert.Contains(nameof(MixedInspectorComponent), labels);
        Assert.Contains(nameof(EntityInfoComponent), labels);
        // Collapsed by default: no member row yet.
        Assert.DoesNotContain("Health: 7", labels);
    }

    [Fact]
    public void Inspector_ExpandComponent_ShowsMemberValues()
    {
        using var world = new World();
        var widget = world.CreateEntity();
        widget.Set(new EntityInfoComponent("Widget"));
        widget.Set(new MixedInspectorComponent { Health = 7, Label = "hi" });
        using var panel = MakePanel(world, Vm());

        panel.Update(Edit());
        panel.SelectEntityByLabel("Widget");
        panel.Update(Edit());

        panel.ToggleComponent(typeof(MixedInspectorComponent).FullName!);
        panel.Update(Edit());

        var labels = VisibleLabels(world, Panel(Vm()));
        Assert.Contains("Health: 7", labels);
        Assert.Contains("Label: hi", labels);
    }

    // ---- SYSTEMS: group collapse ----

    private static (EditorPipelineRegistrar update, EditorPipelineRegistrar draw, CountingSystem a, CountingSystem b)
        TreeRegistrars()
    {
        var update = new EditorPipelineRegistrar();
        var draw = new EditorPipelineRegistrar();
        var a = new CountingSystem();
        var b = new CountingSystem();
        update.AddGroup("logic", EditTimeBehavior.Freeze, g => { g.Add("a", a); g.Add("b", b); });
        update.Build();
        draw.Add("renderMain", new CountingSystem(), EditTimeBehavior.RunNormally);
        draw.Build();
        return (update, draw, a, b);
    }

    [Fact]
    public void SystemsGroup_CollapseViaHeadlessToggle_HidesChildren()
    {
        using var world = new World();
        MakeCursor(world);
        var (update, draw, _, _) = TreeRegistrars();
        using var panel = MakePanel(world, Vm(), update, draw);

        panel.Update(Edit());
        var labels = VisibleLabels(world, Panel(Vm()));
        Assert.Contains("logic [freeze]", labels);
        Assert.Contains("a", labels);
        Assert.Contains("b", labels);

        panel.ToggleGroup("logic");
        panel.Update(Edit());

        labels = VisibleLabels(world, Panel(Vm()));
        Assert.Contains("logic [freeze]", labels); // the group row stays
        Assert.DoesNotContain("a", labels);         // children hidden
        Assert.DoesNotContain("b", labels);
    }

    [Fact]
    public void SystemsGroup_CaretClick_CollapsesChildren_WithoutTogglingEnable()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var (update, draw, _, _) = TreeRegistrars();
        using var panel = MakePanel(world, vm, update, draw);

        panel.Update(Edit()); // SYSTEMS(0), UPDATE(1), logic(2), a(3), b(4), DRAW(5), renderMain(6), SCENE...

        // Click the group row's caret (left column) — collapse, not the enable-toggle.
        var groupRow = SystemsPanelLayout.LineRect(Panel(vm), 2);
        var caret = SystemsPanelLayout.CaretRect(groupRow, 0);
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(caret.Center.X, caret.Center.Y);
        input.LeftButtonReleased = true;
        panel.Update(Edit());

        var labels = VisibleLabels(world, Panel(vm));
        Assert.DoesNotContain("a", labels);
        Assert.DoesNotContain("b", labels);
        // Collapsing never disables systems: the group stays enabled.
        Assert.True(update.IsEnabled("logic.a"));
        Assert.True(update.IsEnabled("logic.b"));
    }

    private sealed class CountingSystem : DefaultEcs.System.ISystem<GameState>
    {
        public bool IsEnabled { get; set; } = true;
        public void Update(GameState state) { }
        public void Dispose() { }
    }
}
