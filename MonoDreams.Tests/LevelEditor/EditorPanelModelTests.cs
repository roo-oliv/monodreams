#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Inspector;
using MonoDreams.LevelEditor.UI;
using MonoDreams.State;
using MonoDreams.System;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the pure right-panel row assembler (<see cref="EditorPanelModel"/>): the three
/// collapsible sections (Systems/Scene/Inspector), per-section collapse, per-pipeline-group
/// collapse, the scene tree rows (indent + selection highlight + subtree collapse), and the
/// Inspector component/member rows. No world I/O, no GraphicsDevice — the model is fed pre-built
/// inputs, so these tests pin the exact row output.
/// </summary>
public class EditorPanelModelTests
{
    private sealed class Noop : ISystem<GameState>
    {
        public bool IsEnabled { get; set; } = true;
        public void Update(GameState state) { }
        public void Dispose() { }
    }

    private static (EditorPipelineRegistrar update, EditorPipelineRegistrar draw) FlatPipelines()
    {
        var update = new EditorPipelineRegistrar();
        update.Add("logic", new Noop(), EditTimeBehavior.Freeze);
        update.Build();
        var draw = new EditorPipelineRegistrar();
        draw.Add("renderMain", new Noop(), EditTimeBehavior.RunNormally);
        draw.Build();
        return (update, draw);
    }

    private static EditorPipelineRegistrar GroupPipeline()
    {
        var update = new EditorPipelineRegistrar();
        update.AddGroup("logic", EditTimeBehavior.Freeze, g =>
        {
            g.Add("a", new Noop());
            g.Add("b", new Noop());
        });
        update.Build();
        return update;
    }

    private static List<PanelRow> Build(
        EditorPanelStateComponent state,
        EditorPipelineRegistrar? update = null,
        EditorPipelineRegistrar? draw = null,
        IReadOnlyList<EntitySceneTree.Node>? nodes = null,
        Func<Entity, string>? label = null,
        Entity selected = default,
        IReadOnlyList<ComponentInspector.ComponentInfo>? inspector = null,
        string? inspectorTitle = null,
        EditorRightTab activeTab = EditorRightTab.Scene,
        EditorProjectInfo project = default)
        => EditorPanelModel.Build(state, activeTab, update, draw,
            nodes ?? Array.Empty<EntitySceneTree.Node>(), label ?? (_ => ""),
            selected, inspector, inspectorTitle, project);

    // ---- Section collapse -------------------------------------------------

    [Fact]
    public void SystemsSectionCollapse_HidesPipelineRows_KeepsHeader()
    {
        var state = new EditorPanelStateComponent();
        var (update, draw) = FlatPipelines();

        var rows = Build(state, update, draw, activeTab: EditorRightTab.Systems);
        Assert.Contains(rows, r => r.Kind == PanelRowKind.SectionHeader && r.Section == EditorPanelSection.Systems);
        Assert.Contains(rows, r => r.Kind == PanelRowKind.PipelineEntry && r.Label == "logic [freeze]");
        Assert.Contains(rows, r => r.Kind == PanelRowKind.PipelineEntry && r.Label == "renderMain");

        state.SystemsCollapsed = true;
        rows = Build(state, update, draw, activeTab: EditorRightTab.Systems);
        Assert.Contains(rows, r => r.Kind == PanelRowKind.SectionHeader && r.Section == EditorPanelSection.Systems);
        Assert.DoesNotContain(rows, r => r.Kind == PanelRowKind.PipelineEntry);
        Assert.DoesNotContain(rows, r => r.Kind == PanelRowKind.PipelineSubheader);
    }

    // ---- Tab filtering (UX-B) ---------------------------------------------

    [Fact]
    public void SceneTab_ShowsSceneAndInspector_NotSystems()
    {
        var rows = Build(new EditorPanelStateComponent(), activeTab: EditorRightTab.Scene);
        Assert.Contains(rows, r => r.Kind == PanelRowKind.SectionHeader && r.Section == EditorPanelSection.Scene);
        Assert.Contains(rows, r => r.Kind == PanelRowKind.SectionHeader && r.Section == EditorPanelSection.Inspector);
        Assert.DoesNotContain(rows, r => r.Kind == PanelRowKind.SectionHeader && r.Section == EditorPanelSection.Systems);
    }

    [Fact]
    public void SystemsTab_ShowsOnlySystems()
    {
        var (update, draw) = FlatPipelines();
        var rows = Build(new EditorPanelStateComponent(), update, draw, activeTab: EditorRightTab.Systems);
        Assert.Contains(rows, r => r.Kind == PanelRowKind.SectionHeader && r.Section == EditorPanelSection.Systems);
        Assert.DoesNotContain(rows, r => r.Kind == PanelRowKind.SectionHeader && r.Section == EditorPanelSection.Scene);
        Assert.DoesNotContain(rows, r => r.Kind == PanelRowKind.SectionHeader && r.Section == EditorPanelSection.Inspector);
    }

    [Fact]
    public void ProjectTab_ShowsProjectInfo_AndTheUxCPlaceholder()
    {
        var rows = Build(new EditorPanelStateComponent(), activeTab: EditorRightTab.Project,
            project: new EditorProjectInfo("/games/isle", "Levels", "island"));
        Assert.Contains(rows, r => r.Kind == PanelRowKind.Info && r.Label.Contains("/games/isle"));
        Assert.Contains(rows, r => r.Kind == PanelRowKind.Info && r.Label.Contains("Levels"));
        Assert.Contains(rows, r => r.Kind == PanelRowKind.Info && r.Label.Contains("island"));
        Assert.Contains(rows, r => r.Kind == PanelRowKind.Info && r.Label == EditorPanelModel.ScenesListPlaceholder);
        // No collapsible sections in the Project tab.
        Assert.DoesNotContain(rows, r => r.Kind == PanelRowKind.SectionHeader);
    }

    [Fact]
    public void ProjectTab_UnresolvedRoot_ShowsUnresolved()
    {
        var rows = Build(new EditorPanelStateComponent(), activeTab: EditorRightTab.Project,
            project: new EditorProjectInfo(null, null, null));
        Assert.Contains(rows, r => r.Kind == PanelRowKind.Info && r.Label.Contains("(unresolved)"));
    }

    [Theory]
    [InlineData("short", 20, "short")]                       // fits → unchanged
    [InlineData("/a/very/long/path/to/scene.mdscene", 12, null)] // truncated, ellipsis in the middle
    public void MiddleEllipsis_KeepsHeadAndTail(string input, int max, string? expected)
    {
        var result = EditorPanelModel.MiddleEllipsis(input, max);
        Assert.True(result.Length <= max);
        if (expected != null) Assert.Equal(expected, result);
        else
        {
            Assert.Contains("…", result);
            Assert.StartsWith("/a", result);
            Assert.EndsWith("scene", result);
        }
    }

    [Fact]
    public void SectionHeader_DisclosureState_ReflectsCollapse()
    {
        var state = new EditorPanelStateComponent { SceneCollapsed = true };
        var rows = Build(state, activeTab: EditorRightTab.Scene);

        var scene = rows.Single(r => r.Section == EditorPanelSection.Scene && r.Kind == PanelRowKind.SectionHeader);
        var inspector = rows.Single(r => r.Section == EditorPanelSection.Inspector && r.Kind == PanelRowKind.SectionHeader);
        Assert.True(scene.Collapsible && !scene.Expanded); // collapsed → arrow shows collapsed
        Assert.True(inspector.Expanded);
    }

    [Fact]
    public void HostTab_MapsSectionToItsTab()
    {
        Assert.Equal(EditorRightTab.Systems, EditorPanelModel.HostTab(EditorPanelSection.Systems));
        Assert.Equal(EditorRightTab.Scene, EditorPanelModel.HostTab(EditorPanelSection.Scene));
        Assert.Equal(EditorRightTab.Scene, EditorPanelModel.HostTab(EditorPanelSection.Inspector));
    }

    // ---- Group collapse ---------------------------------------------------

    [Fact]
    public void PipelineGroupCollapse_HidesChildren_KeepsGroupRow()
    {
        var state = new EditorPanelStateComponent();
        var update = GroupPipeline();

        var rows = Build(state, update, activeTab: EditorRightTab.Systems);
        Assert.Contains(rows, r => r.Kind == PanelRowKind.PipelineEntry && r.Label == "logic [freeze]");
        Assert.Contains(rows, r => r.Kind == PanelRowKind.PipelineEntry && r.Label == "a");
        Assert.Contains(rows, r => r.Kind == PanelRowKind.PipelineEntry && r.Label == "b");
        var group = rows.Single(r => r.Label == "logic [freeze]");
        Assert.True(group.Collapsible && group.Expanded);

        state.CollapsedGroups.Add("logic");
        rows = Build(state, update, activeTab: EditorRightTab.Systems);
        Assert.Contains(rows, r => r.Label == "logic [freeze]"); // group row stays
        Assert.DoesNotContain(rows, r => r.Label == "a");        // children hidden
        Assert.DoesNotContain(rows, r => r.Label == "b");
        Assert.False(rows.Single(r => r.Label == "logic [freeze]").Expanded);
    }

    [Fact]
    public void PipelineGroup_TriStateAndMinusBar_TrackTheRegistrar()
    {
        var state = new EditorPanelStateComponent();
        var update = GroupPipeline();

        var group = () => Build(state, update, activeTab: EditorRightTab.Systems).Single(r => r.Label == "logic [freeze]");
        Assert.Equal(PipelineEnabledState.On, group().CheckboxState);
        Assert.False(group().ShowMinusBar);

        update.SetEnabled("logic.a", false); // now mixed
        Assert.Equal(PipelineEnabledState.Mixed, group().CheckboxState);
        Assert.True(group().ShowMinusBar);
    }

    // ---- Scene tree -------------------------------------------------------

    [Fact]
    public void SceneRows_IndentAndHighlightTheSelection()
    {
        using var world = new World();
        var root = world.CreateEntity();
        var child = world.CreateEntity();
        var state = new EditorPanelStateComponent();
        var nodes = new List<EntitySceneTree.Node>
        {
            new(root, 0, hasChildren: true),
            new(child, 1, hasChildren: false),
        };

        var rows = Build(state, nodes: nodes,
            label: e => e == root ? "Root" : "Child", selected: root);

        var rootRow = rows.Single(r => r.Kind == PanelRowKind.SceneEntity && r.Label == "Root");
        var childRow = rows.Single(r => r.Kind == PanelRowKind.SceneEntity && r.Label == "Child");
        Assert.Equal(1, rootRow.Depth);  // section header is depth 0; tree starts at depth 1
        Assert.Equal(2, childRow.Depth);
        Assert.True(rootRow.Selected);
        Assert.False(childRow.Selected);
        Assert.True(rootRow.Collapsible); // has children → arrow
        Assert.False(childRow.Collapsible);
    }

    [Fact]
    public void SceneSubtreeCollapse_HidesDescendants()
    {
        using var world = new World();
        var root = world.CreateEntity();
        var child = world.CreateEntity();
        var state = new EditorPanelStateComponent();
        state.CollapsedTreeEntities.Add(root);
        var nodes = new List<EntitySceneTree.Node>
        {
            new(root, 0, hasChildren: true),
            new(child, 1, hasChildren: false),
        };

        var rows = Build(state, nodes: nodes, label: e => e == root ? "Root" : "Child");

        Assert.Contains(rows, r => r.Label == "Root");
        Assert.DoesNotContain(rows, r => r.Label == "Child");
        Assert.False(rows.Single(r => r.Label == "Root").Expanded);
    }

    [Fact]
    public void EmptyScene_ShowsPlaceholder()
    {
        var rows = Build(new EditorPanelStateComponent());
        Assert.Contains(rows, r => r.Kind == PanelRowKind.Info && r.Label == "(no scene entities)");
    }

    // ---- Inspector --------------------------------------------------------

    [Fact]
    public void Inspector_NoSelection_ShowsPlaceholder()
    {
        var rows = Build(new EditorPanelStateComponent(), inspector: null);
        Assert.Contains(rows, r => r.Kind == PanelRowKind.Info && r.Label == "(no selection)");
    }

    [Fact]
    public void Inspector_ListsComponents_AndExpandsToMemberValues()
    {
        var state = new EditorPanelStateComponent();
        var comps = new List<ComponentInspector.ComponentInfo>
        {
            new()
            {
                TypeName = "Foo",
                FullTypeName = "N.Foo",
                Members = new[] { new ComponentInspector.Member("X", "1") },
            },
        };

        var rows = Build(state, selected: default, inspector: comps, inspectorTitle: "Player");
        Assert.Contains(rows, r => r.Kind == PanelRowKind.Info && r.Label == "Player"); // title
        var comp = rows.Single(r => r.Kind == PanelRowKind.InspectorComponent && r.Label == "Foo");
        Assert.True(comp.Collapsible);   // has members → expandable
        Assert.False(comp.Expanded);
        Assert.DoesNotContain(rows, r => r.Kind == PanelRowKind.InspectorMember); // members hidden by default

        state.ExpandedInspectorComponents.Add("N.Foo");
        rows = Build(state, selected: default, inspector: comps, inspectorTitle: "Player");
        Assert.True(rows.Single(r => r.Kind == PanelRowKind.InspectorComponent).Expanded);
        Assert.Contains(rows, r => r.Kind == PanelRowKind.InspectorMember && r.Label == "X: 1");
    }
}
