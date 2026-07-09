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
/// Protects the pure panel row assemblers (<see cref="EditorPanelModel"/>): the left tabbed panel's
/// <c>Build</c> (Entities / Systems / Scenes tabs, per-section collapse, per-pipeline-group collapse,
/// the scene tree rows with indent + selection highlight + subtree collapse) and the dedicated right
/// Inspector panel's <c>BuildInspector</c> (component/member rows, no section header). No world I/O,
/// no GraphicsDevice — the models are fed pre-built inputs, so these tests pin the exact row output.
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
        EditorPanelTab activeTab = EditorPanelTab.Entities,
        EditorProjectInfo project = default,
        IReadOnlyList<SceneCatalogEntry>? sceneCatalog = null,
        bool isDirty = false)
        => EditorPanelModel.Build(state, activeTab, update, draw,
            nodes ?? Array.Empty<EntitySceneTree.Node>(), label ?? (_ => ""),
            selected, project, sceneCatalog, isDirty);

    // ---- Section collapse -------------------------------------------------

    [Fact]
    public void SystemsSectionCollapse_HidesPipelineRows_KeepsHeader()
    {
        var state = new EditorPanelStateComponent();
        var (update, draw) = FlatPipelines();

        var rows = Build(state, update, draw, activeTab: EditorPanelTab.Systems);
        Assert.Contains(rows, r => r.Kind == PanelRowKind.SectionHeader && r.Section == EditorPanelSection.Systems);
        Assert.Contains(rows, r => r.Kind == PanelRowKind.PipelineEntry && r.Label == "logic [freeze]");
        Assert.Contains(rows, r => r.Kind == PanelRowKind.PipelineEntry && r.Label == "renderMain");

        state.SystemsCollapsed = true;
        rows = Build(state, update, draw, activeTab: EditorPanelTab.Systems);
        Assert.Contains(rows, r => r.Kind == PanelRowKind.SectionHeader && r.Section == EditorPanelSection.Systems);
        Assert.DoesNotContain(rows, r => r.Kind == PanelRowKind.PipelineEntry);
        Assert.DoesNotContain(rows, r => r.Kind == PanelRowKind.PipelineSubheader);
    }

    // ---- Tab filtering (UX2-B: Entities / Systems / Scenes) ----------------

    [Fact]
    public void EntitiesTab_ShowsTheTree_NotInspectorNotSystems()
    {
        // The Entities tab is the tree ALONE — the Inspector left for its dedicated right panel.
        var rows = Build(new EditorPanelStateComponent(), activeTab: EditorPanelTab.Entities);
        Assert.Contains(rows, r => r.Kind == PanelRowKind.SectionHeader && r.Section == EditorPanelSection.Entities);
        Assert.DoesNotContain(rows, r => r.Kind == PanelRowKind.SectionHeader && r.Section == EditorPanelSection.Systems);
        // No Inspector rows on the Entities tab (Inspector is BuildInspector, the right panel).
        Assert.DoesNotContain(rows, r => r.Kind == PanelRowKind.InspectorComponent);
    }

    [Fact]
    public void SystemsTab_ShowsOnlySystems()
    {
        var (update, draw) = FlatPipelines();
        var rows = Build(new EditorPanelStateComponent(), update, draw, activeTab: EditorPanelTab.Systems);
        Assert.Contains(rows, r => r.Kind == PanelRowKind.SectionHeader && r.Section == EditorPanelSection.Systems);
        Assert.DoesNotContain(rows, r => r.Kind == PanelRowKind.SectionHeader && r.Section == EditorPanelSection.Entities);
    }

    [Fact]
    public void ScenesTab_ShowsProjectInfo_AndTheScenesList()
    {
        var catalog = new[]
        {
            new SceneCatalogEntry("level_selection", "Level Selection", "LevelSelection", "level_selection", IsCurrent: false),
            new SceneCatalogEntry("island", "island", "Game", "island", IsCurrent: true),
        };
        var rows = Build(new EditorPanelStateComponent(), activeTab: EditorPanelTab.Scenes,
            project: new EditorProjectInfo("/games/isle", "Levels", "island"), sceneCatalog: catalog);

        Assert.Contains(rows, r => r.Kind == PanelRowKind.Info && r.Label.Contains("/games/isle"));
        Assert.Contains(rows, r => r.Kind == PanelRowKind.Info && r.Label.Contains("Levels"));
        Assert.Contains(rows, r => r.Kind == PanelRowKind.Info && r.Label == EditorPanelModel.ScenesTitle);
        // Both catalog entries render as selectable scene rows; the current one is highlighted.
        Assert.Contains(rows, r => r.Kind == PanelRowKind.SceneCatalogEntry && r.Label == "Level Selection" && !r.Selected);
        Assert.Contains(rows, r => r.Kind == PanelRowKind.SceneCatalogEntry && r.Label == "island" && r.Selected);
        // No collapsible sections in the Scenes tab.
        Assert.DoesNotContain(rows, r => r.Kind == PanelRowKind.SectionHeader);
    }

    [Fact]
    public void ScenesTab_CurrentEntry_ShowsDirtyMarker_WhenDirty()
    {
        var catalog = new[]
        {
            new SceneCatalogEntry("island", "island", "Game", "island", IsCurrent: true),
            new SceneCatalogEntry("cove", "cove", "Game", "cove", IsCurrent: false),
        };
        var clean = Build(new EditorPanelStateComponent(), activeTab: EditorPanelTab.Scenes, sceneCatalog: catalog, isDirty: false);
        Assert.DoesNotContain(clean, r => r.Kind == PanelRowKind.SceneCatalogEntry && r.DirtyMarker);

        var dirty = Build(new EditorPanelStateComponent(), activeTab: EditorPanelTab.Scenes, sceneCatalog: catalog, isDirty: true);
        var current = dirty.Single(r => r.Kind == PanelRowKind.SceneCatalogEntry && r.Selected);
        Assert.True(current.DirtyMarker);
        Assert.StartsWith("●", current.Label); // the Warning ● prefix
        // Only the current entry carries the marker — a non-current entry never does, even while dirty.
        Assert.DoesNotContain(dirty, r => r.Kind == PanelRowKind.SceneCatalogEntry && !r.Selected && r.DirtyMarker);
    }

    [Fact]
    public void ScenesTab_NoCatalog_ShowsNoScenes()
    {
        var rows = Build(new EditorPanelStateComponent(), activeTab: EditorPanelTab.Scenes,
            project: new EditorProjectInfo(null, null, null));
        Assert.Contains(rows, r => r.Kind == PanelRowKind.Info && r.Label.Contains("(unresolved)"));
        Assert.Contains(rows, r => r.Kind == PanelRowKind.Info && r.Label == "(no scenes)");
        Assert.DoesNotContain(rows, r => r.Kind == PanelRowKind.SceneCatalogEntry);
    }

    [Fact]
    public void ScenesTab_UnresolvedRoot_ShowsUnresolved()
    {
        var rows = Build(new EditorPanelStateComponent(), activeTab: EditorPanelTab.Scenes,
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
        var state = new EditorPanelStateComponent { EntitiesCollapsed = true };
        var rows = Build(state, activeTab: EditorPanelTab.Entities);

        var entities = rows.Single(r => r.Section == EditorPanelSection.Entities && r.Kind == PanelRowKind.SectionHeader);
        Assert.True(entities.Collapsible && !entities.Expanded); // collapsed → arrow shows collapsed
    }

    [Fact]
    public void HostTab_MapsSectionToItsTab()
    {
        Assert.Equal(EditorPanelTab.Systems, EditorPanelModel.HostTab(EditorPanelSection.Systems));
        Assert.Equal(EditorPanelTab.Entities, EditorPanelModel.HostTab(EditorPanelSection.Entities));
    }

    // ---- Group collapse ---------------------------------------------------

    [Fact]
    public void PipelineGroupCollapse_HidesChildren_KeepsGroupRow()
    {
        var state = new EditorPanelStateComponent();
        var update = GroupPipeline();

        var rows = Build(state, update, activeTab: EditorPanelTab.Systems);
        Assert.Contains(rows, r => r.Kind == PanelRowKind.PipelineEntry && r.Label == "logic [freeze]");
        Assert.Contains(rows, r => r.Kind == PanelRowKind.PipelineEntry && r.Label == "a");
        Assert.Contains(rows, r => r.Kind == PanelRowKind.PipelineEntry && r.Label == "b");
        var group = rows.Single(r => r.Label == "logic [freeze]");
        Assert.True(group.Collapsible && group.Expanded);

        state.CollapsedGroups.Add("logic");
        rows = Build(state, update, activeTab: EditorPanelTab.Systems);
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

        var group = () => Build(state, update, activeTab: EditorPanelTab.Systems).Single(r => r.Label == "logic [freeze]");
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

    // ---- Inspector (the dedicated right panel: BuildInspector) -------------

    [Fact]
    public void BuildInspector_NoSelection_ShowsPlaceholder()
    {
        var rows = EditorPanelModel.BuildInspector(new EditorPanelStateComponent(), inspectorComponents: null, inspectorTitle: null);
        Assert.Contains(rows, r => r.Kind == PanelRowKind.Info && r.Label == "(no selection)");
    }

    [Fact]
    public void BuildInspector_ListsComponents_AndExpandsToMemberValues_NoSectionHeader()
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

        var rows = EditorPanelModel.BuildInspector(state, comps, "Player");
        Assert.Contains(rows, r => r.Kind == PanelRowKind.Info && r.Label == "Player"); // body title row
        // No section header — the region's slim header IS the title now (standalone Inspector panel).
        Assert.DoesNotContain(rows, r => r.Kind == PanelRowKind.SectionHeader);
        var comp = rows.Single(r => r.Kind == PanelRowKind.InspectorComponent && r.Label == "Foo");
        Assert.True(comp.Collapsible);   // has members → expandable
        Assert.False(comp.Expanded);
        Assert.DoesNotContain(rows, r => r.Kind == PanelRowKind.InspectorMember); // members hidden by default

        state.ExpandedInspectorComponents.Add("N.Foo");
        rows = EditorPanelModel.BuildInspector(state, comps, "Player");
        Assert.True(rows.Single(r => r.Kind == PanelRowKind.InspectorComponent).Expanded);
        // PF-A: the member row splits into the name part (Label "X:") + the type-colored value.
        Assert.Contains(rows, r => r.Kind == PanelRowKind.InspectorMember && r.MemberName == "X" && r.MemberValue == "1");
    }

    // ---- Inspector: the editable model (PF-A §3) --------------------------

    private static ComponentInspector.ComponentInfo Comp(string name, string fullName, Type? type,
        params ComponentInspector.Member[] members) => new()
    {
        TypeName = name,
        FullTypeName = fullName,
        Type = type,
        Members = members,
    };

    private static List<PanelRow> Inspector(EditorPanelStateComponent state,
        IReadOnlyList<ComponentInspector.ComponentInfo>? comps, string? title = "E", string? filter = null,
        Func<ComponentInspector.ComponentInfo, InspectorDeleteAffordance>? del = null, bool addRow = false)
        => EditorPanelModel.BuildInspector(state, comps, title, filter, del, addRow);

    [Fact]
    public void BuildInspector_ShowsFilterRow_AndOptionalAddRow()
    {
        var comps = new List<ComponentInspector.ComponentInfo> { Comp("Foo", "N.Foo", typeof(int)) };
        var rows = Inspector(new EditorPanelStateComponent(), comps, filter: "hi", addRow: true);

        var filterRow = rows.Single(r => r.Kind == PanelRowKind.InspectorFilter);
        Assert.Equal("hi", filterRow.Label); // the filter row carries the current filter text
        Assert.Equal(PanelRowKind.InspectorFilter, rows[0].Kind); // it leads the body
        Assert.Contains(rows, r => r.Kind == PanelRowKind.InspectorAddComponent && r.Label == "+ Add component");

        // No add row when not requested (a no-registry unit view).
        var noAdd = Inspector(new EditorPanelStateComponent(), comps, addRow: false);
        Assert.DoesNotContain(noAdd, r => r.Kind == PanelRowKind.InspectorAddComponent);
    }

    [Fact]
    public void BuildInspector_Filter_NarrowsComponents_ByNameOrMember_CaseInsensitive()
    {
        var state = new EditorPanelStateComponent();
        state.ExpandedInspectorComponents.Add("N.Transform");
        state.ExpandedInspectorComponents.Add("N.RigidBody");
        var comps = new List<ComponentInspector.ComponentInfo>
        {
            Comp("Transform", "N.Transform", typeof(int),
                new ComponentInspector.Member("Position", "0, 0", typeof(Microsoft.Xna.Framework.Vector2), true, InspectorValueRole.Number)),
            Comp("RigidBody", "N.RigidBody", typeof(int),
                new ComponentInspector.Member("Mass", "1", typeof(float), true, InspectorValueRole.Number)),
        };

        // A component-NAME match (case-insensitive) keeps only that component.
        var byName = Inspector(state, comps, filter: "rigid");
        Assert.Contains(byName, r => r.Kind == PanelRowKind.InspectorComponent && r.Label == "RigidBody");
        Assert.DoesNotContain(byName, r => r.Kind == PanelRowKind.InspectorComponent && r.Label == "Transform");

        // A member-NAME match keeps the component even when its type name doesn't match.
        var byMember = Inspector(state, comps, filter: "mass");
        Assert.Contains(byMember, r => r.Kind == PanelRowKind.InspectorComponent && r.Label == "RigidBody");
        Assert.DoesNotContain(byMember, r => r.Kind == PanelRowKind.InspectorComponent && r.Label == "Transform");

        // A member-VALUE match keeps the component.
        var byValue = Inspector(state, comps, filter: "0, 0");
        Assert.Contains(byValue, r => r.Kind == PanelRowKind.InspectorComponent && r.Label == "Transform");
    }

    [Fact]
    public void BuildInspector_MemberRows_CarryTypeColorAndEditability()
    {
        var state = new EditorPanelStateComponent();
        state.ExpandedInspectorComponents.Add("N.C");
        var comps = new List<ComponentInspector.ComponentInfo>
        {
            Comp("C", "N.C", typeof(int),
                new ComponentInspector.Member("N", "3", typeof(int), true, InspectorValueRole.Number),
                new ComponentInspector.Member("Ro", "readonly", typeof(object), false, InspectorValueRole.Muted)),
        };
        var rows = Inspector(state, comps);

        var editable = rows.Single(r => r.Kind == PanelRowKind.InspectorMember && r.MemberName == "N");
        Assert.True(editable.MemberEditable);
        Assert.Equal(InspectorValueRole.Number, editable.ValueRole);

        var ro = rows.Single(r => r.Kind == PanelRowKind.InspectorMember && r.MemberName == "Ro");
        Assert.False(ro.MemberEditable);
        Assert.Equal(InspectorValueRole.Muted, ro.ValueRole);
    }

    [Fact]
    public void BuildInspector_DeleteAffordance_IsPerComponent()
    {
        var comps = new List<ComponentInspector.ComponentInfo>
        {
            Comp("Transform", "N.Transform", typeof(int)),
            Comp("SceneEntityId", "N.SceneEntityId", typeof(long)),
            Comp("RigidBody", "N.RigidBody", typeof(short)),
        };
        InspectorDeleteAffordance Del(ComponentInspector.ComponentInfo c) => c.TypeName switch
        {
            "Transform" => InspectorDeleteAffordance.Guarded,
            "SceneEntityId" => InspectorDeleteAffordance.None,
            _ => InspectorDeleteAffordance.Deletable,
        };
        var rows = Inspector(new EditorPanelStateComponent(), comps, del: Del);

        Assert.Equal(InspectorDeleteAffordance.Guarded, Row(rows, "Transform").DeleteAffordance);
        Assert.Equal(InspectorDeleteAffordance.None, Row(rows, "SceneEntityId").DeleteAffordance);
        Assert.Equal(InspectorDeleteAffordance.Deletable, Row(rows, "RigidBody").DeleteAffordance);
    }

    private static PanelRow Row(List<PanelRow> rows, string label) =>
        rows.Single(r => r.Kind == PanelRowKind.InspectorComponent && r.Label == label);
}
