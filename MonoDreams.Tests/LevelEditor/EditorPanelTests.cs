#nullable enable
using System.Collections.Generic;
using System.Linq;
using DefaultEcs;
using DefaultEcs.System;
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
/// Protects the <see cref="EditorPanelSystem"/> in BOTH roles (UX2-B): the LEFT tabbed panel
/// (Entities / Systems / Scenes) and the dedicated RIGHT Inspector panel. Covers: the Systems tab
/// mirrors both pipelines and toggles an entry (the gated system actually stops); the panel refuses
/// to disable itself; section + group collapse; the wheel scrolls whole clamped lines; the Entities
/// tree selects an entity both ways (<c>SelectedComponent</c>) — and a LEFT-panel tree click updates
/// the RIGHT Inspector panel; and the Inspector panel lists the selection's components and expands to
/// member values. Pure logic — a null font (layout-only) + a hand-built <see cref="ViewportManager"/>.
/// </summary>
public class EditorPanelTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    private sealed class CountingSystem : ISystem<GameState>
    {
        public int Updates { get; private set; }
        public bool IsEnabled { get; set; } = true;
        public void Update(GameState state) => Updates++;
        public void Dispose() { }
    }

    private static Entity MakeCursor(World world)
    {
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent());
        return cursor;
    }

    private static ViewportManager Vm(int width = 1600, int height = 900) =>
        new(null, 800, 600) { ScreenWidth = width, ScreenHeight = height };

    /// <summary>A scene entity (Transform + optional EntityInfo name) — appears in the Scene tree.</summary>
    private static Entity MakeSceneEntity(World world, string? name = null)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(Vector2.Zero));
        if (name != null) e.Set(new EntityInfoComponent(name, name));
        return e;
    }

    // ---- Interaction helpers (find a row, then click its body or arrow) ----

    private int RowIndex(EditorPanelSystem panel, global::System.Func<PanelRow, bool> pred)
    {
        for (var i = 0; i < panel.Rows.Count; i++)
            if (pred(panel.Rows[i])) return i;
        return -1;
    }

    private Rectangle LineFor(EditorPanelSystem panel, ViewportManager vm, int rowIndex)
    {
        // The left tabbed panel's rows live in the LEFT region BODY (below the tab strip) at the
        // shell's runtime region sizes (UX2-B — the tab group moved to the left strip).
        var panelRect = EditorChromeLayout.LeftPanel(vm.ScreenWidth, vm.ScreenHeight, vm.DevicePixelRatio,
            panel.ShellState.LeftWidthPt, panel.ShellState.BottomHeightPt);
        var body = EditorChromeLayout.RegionBody(panelRect, vm.DevicePixelRatio);
        return SystemsPanelLayout.LineRect(body, rowIndex - panel.ScrollOffset);
    }

    private void ClickBody(EditorPanelSystem panel, ViewportManager vm, Entity cursor, int rowIndex)
    {
        var line = LineFor(panel, vm, rowIndex);
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(line.Center.X, line.Center.Y); // center is past the arrow gutter
        input.LeftButtonReleased = true;
    }

    private void ClickArrow(EditorPanelSystem panel, ViewportManager vm, Entity cursor, int rowIndex)
    {
        var line = LineFor(panel, vm, rowIndex);
        var arrow = SystemsPanelLayout.ArrowRect(line, panel.Rows[rowIndex].Depth);
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(arrow.Center.X, arrow.Center.Y);
        input.LeftButtonReleased = true;
    }

    private static List<string> VisibleLabels(World world)
    {
        var texts = new List<string>();
        using var set = world.GetEntities().With<DynamicTextComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.Get<TransformComponent>().Position != SystemsPanelLayout.ParkedPosition)
                texts.Add(e.Get<DynamicTextComponent>().TextContent);
        return texts;
    }

    private static (EditorPanelSystem panel, EditorPipelineRegistrar update, CountingSystem logic,
        SequentialSystem<GameState> updatePipeline) MakeFlatPanel(World world, ViewportManager vm)
    {
        var update = new EditorPipelineRegistrar();
        var draw = new EditorPipelineRegistrar();
        var logic = new CountingSystem();
        update.Add("logic", logic, EditTimeBehavior.Freeze);
        var updatePipeline = update.Build();
        draw.Add("renderMain", new CountingSystem(), EditTimeBehavior.RunNormally);
        draw.Build();
        var panel = new EditorPanelSystem(world, vm, font: null, () => (update, draw));
        panel.SetActiveTab(EditorPanelTab.Systems); // these tests exercise the Systems tab
        return (panel, update, logic, updatePipeline);
    }

    private static (EditorPanelSystem panel, EditorPipelineRegistrar update) MakeGroupPanel(World world, ViewportManager vm)
    {
        var update = new EditorPipelineRegistrar();
        var draw = new EditorPipelineRegistrar();
        update.AddGroup("logic", EditTimeBehavior.Freeze, g =>
        {
            g.Add("a", new CountingSystem());
            g.Add("b", new CountingSystem());
        });
        update.Build();
        draw.Add("renderMain", new CountingSystem(), EditTimeBehavior.RunNormally);
        draw.Build();
        var panel = new EditorPanelSystem(world, vm, font: null, () => (update, draw));
        panel.SetActiveTab(EditorPanelTab.Systems); // these tests exercise the Systems tab
        return (panel, update);
    }

    // ---- Systems section: mirrors the pipelines, toggles an entry ----------

    [Fact]
    public void SystemsSection_MirrorsBothPipelines_WithPolicyTags()
    {
        using var world = new World();
        MakeCursor(world);
        var (panel, _, _, _) = MakeFlatPanel(world, Vm());
        using var _1 = panel;

        panel.Update(Edit());

        var labels = VisibleLabels(world);
        Assert.Contains(EditorPanelModel.SystemsTitle, labels);
        Assert.Contains("UPDATE", labels);
        Assert.Contains("DRAW", labels);
        Assert.Contains("logic [freeze]", labels);
        Assert.Contains("renderMain", labels);
        // The Systems tab shows only the Systems section — the Entities tree lives on the Entities tab.
        Assert.DoesNotContain(EditorPanelModel.EntitiesTitle, labels);
        // The tab bar labels are always present (persistent widgets).
        Assert.Contains("Entities", labels);
        Assert.Contains("Systems", labels);
        Assert.Contains("Scenes", labels);
    }

    [Fact]
    public void ClickPipelineRow_TogglesTheEntry_AndTheSystemStops()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var (panel, update, logic, updatePipeline) = MakeFlatPanel(world, vm);
        using var _1 = panel;

        panel.Update(Edit());
        var idx = RowIndex(panel, r => r.Kind == PanelRowKind.PipelineEntry && r.Label == "logic [freeze]");
        Assert.True(idx >= 0);

        ClickBody(panel, vm, cursor, idx);
        panel.Update(Edit());
        Assert.False(update.IsEnabled("logic"));

        // Master switch: the child no longer runs in EITHER mode.
        var before = logic.Updates;
        updatePipeline.Update(Play());
        updatePipeline.Update(Edit());
        Assert.Equal(before, logic.Updates);

        // Re-enable via a second click; the Freeze policy resumes (runs in Play only).
        cursor.Get<CursorInputComponent>().LeftButtonReleased = true;
        panel.Update(Edit());
        Assert.True(update.IsEnabled("logic"));
        updatePipeline.Update(Play());
        Assert.Equal(before + 1, logic.Updates);
    }

    [Fact]
    public void RefusesToDisableItsOwnEntry()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var update = new EditorPipelineRegistrar();
        var draw = new EditorPipelineRegistrar();
        EditorPanelSystem panel = null!;
        panel = new EditorPanelSystem(world, vm, font: null, () => (update, draw));
        update.Add("editor.systemsPanel", panel, EditTimeBehavior.RunNormally);
        update.Build();
        draw.Add("renderMain", new CountingSystem(), EditTimeBehavior.RunNormally);
        draw.Build();
        using var _1 = panel;

        panel.Update(Edit());
        var idx = RowIndex(panel, r => r.Entry?.Name == "editor.systemsPanel");
        ClickBody(panel, vm, cursor, idx);
        panel.Update(Edit());

        Assert.True(update.IsEnabled("editor.systemsPanel")); // never disables itself
    }

    [Fact]
    public void WhilePlaying_StaysInteractive()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var (panel, update, _, _) = MakeFlatPanel(world, vm);
        using var _1 = panel;

        panel.Update(Edit());
        var idx = RowIndex(panel, r => r.Label == "logic [freeze]");
        ClickBody(panel, vm, cursor, idx);
        panel.Update(Play()); // transport model: toggling while the game runs is the point

        Assert.False(update.IsEnabled("logic"));
    }

    // ---- Section collapse (click the SYSTEMS header) -----------------------

    [Fact]
    public void SectionHeaderClick_CollapsesTheSection()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var (panel, _, _, _) = MakeFlatPanel(world, vm);
        using var _1 = panel;

        panel.Update(Edit());
        Assert.Contains(panel.Rows, r => r.Kind == PanelRowKind.PipelineEntry);

        var idx = RowIndex(panel, r => r.Kind == PanelRowKind.SectionHeader && r.Section == EditorPanelSection.Systems);
        ClickBody(panel, vm, cursor, idx);
        panel.Update(Edit());

        Assert.True(panel.State.SystemsCollapsed);
        Assert.DoesNotContain(panel.Rows, r => r.Kind == PanelRowKind.PipelineEntry);
        Assert.Contains(panel.Rows, r => r.Kind == PanelRowKind.SectionHeader && r.Section == EditorPanelSection.Systems);
    }

    // ---- Group collapse (click a group row's arrow) ------------------------

    [Fact]
    public void GroupArrowClick_CollapsesAndExpandsChildren()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var (panel, _) = MakeGroupPanel(world, vm);
        using var _1 = panel;

        panel.Update(Edit());
        Assert.Contains(panel.Rows, r => r.Label == "a");
        Assert.Contains(panel.Rows, r => r.Label == "b");

        var groupIdx = RowIndex(panel, r => r.Label == "logic [freeze]");
        ClickArrow(panel, vm, cursor, groupIdx);
        panel.Update(Edit());

        Assert.Contains("logic", panel.State.CollapsedGroups);
        Assert.Contains(panel.Rows, r => r.Label == "logic [freeze]"); // group row stays
        Assert.DoesNotContain(panel.Rows, r => r.Label == "a");        // children hidden

        // Arrow click again re-expands.
        groupIdx = RowIndex(panel, r => r.Label == "logic [freeze]");
        ClickArrow(panel, vm, cursor, groupIdx);
        panel.Update(Edit());
        Assert.Contains(panel.Rows, r => r.Label == "a");
    }

    [Fact]
    public void GroupBodyClick_CascadesEnabled_Gmail()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var (panel, update) = MakeGroupPanel(world, vm);
        using var _1 = panel;

        panel.Update(Edit());
        // Make it mixed, then a body click on the group turns everything off (Gmail).
        update.SetEnabled("logic.a", false);
        Assert.Equal(PipelineEnabledState.Mixed, update.GetEnabledState("logic"));

        panel.Update(Edit());
        var groupIdx = RowIndex(panel, r => r.Label == "logic [freeze]");
        ClickBody(panel, vm, cursor, groupIdx);
        panel.Update(Edit());
        Assert.Equal(PipelineEnabledState.Off, update.GetEnabledState("logic"));
    }

    // ---- Scroll ------------------------------------------------------------

    [Fact]
    public void Wheel_ScrollsByClampedLines()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm(1600, 300); // short window so the list overflows

        var update = new EditorPipelineRegistrar();
        var draw = new EditorPipelineRegistrar();
        for (var i = 0; i < 12; i++) update.Add($"system{i}", new CountingSystem(), EditTimeBehavior.RunNormally);
        update.Build();
        draw.Add("renderMain", new CountingSystem(), EditTimeBehavior.RunNormally);
        draw.Build();
        using var panel = new EditorPanelSystem(world, vm, font: null, () => (update, draw));
        panel.SetActiveTab(EditorPanelTab.Systems);

        panel.Update(Edit());
        Assert.Equal(0, panel.ScrollOffset);

        // Rows scroll within the region BODY (below the tab strip).
        var panelRect = EditorChromeLayout.LeftPanel(vm.ScreenWidth, vm.ScreenHeight);
        var body = EditorChromeLayout.RegionBody(panelRect);
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(body.Center.X, body.Center.Y);

        input.ScrollWheelDelta = -120; // one notch down
        panel.Update(Edit());
        Assert.Equal(SystemsPanelLayout.LinesPerNotch, panel.ScrollOffset);

        input.ScrollWheelDelta = -120 * 100; // clamps to max
        panel.Update(Edit());
        Assert.Equal(SystemsPanelLayout.MaxScroll(panel.Rows.Count, body), panel.ScrollOffset);

        input.ScrollWheelDelta = 120 * 100; // clamps to 0
        panel.Update(Edit());
        Assert.Equal(0, panel.ScrollOffset);

        // Wheel outside the (left) panel does nothing — the game-viewport centre is clear of it.
        input.ScreenPosition = new Vector2(vm.ScreenWidth / 2f, vm.ScreenHeight / 2f);
        input.ScrollWheelDelta = -120;
        panel.Update(Edit());
        Assert.Equal(0, panel.ScrollOffset);
    }

    [Fact]
    public void PooledVisuals_AreBoundedByTheVisibleWindow()
    {
        using var world = new World();
        MakeCursor(world);
        var vm = Vm(1600, 300);
        var update = new EditorPipelineRegistrar();
        var draw = new EditorPipelineRegistrar();
        for (var i = 0; i < 20; i++) update.Add($"system{i}", new CountingSystem(), EditTimeBehavior.RunNormally);
        update.Build();
        draw.Add("renderMain", new CountingSystem(), EditTimeBehavior.RunNormally);
        draw.Build();
        using var panel = new EditorPanelSystem(world, vm, font: null, () => (update, draw));
        panel.SetActiveTab(EditorPanelTab.Systems);

        panel.Update(Edit());

        var panelRect = EditorChromeLayout.LeftPanel(vm.ScreenWidth, vm.ScreenHeight);
        var body = EditorChromeLayout.RegionBody(panelRect);
        var visible = SystemsPanelLayout.VisibleLineCount(body);
        Assert.True(panel.Rows.Count > visible, "test needs an overflowing panel");

        // Pooling: the panel creates a fixed pool sized to the visible window (one label + three
        // screen-baked meshes per slot — the disclosure arrow, the row background fill, and the
        // selected-row accent bar), NOT one entity per row. Persistent chrome adds a fixed,
        // row-count-independent overhead: the 3 tab widgets (each a fill mesh + label + underline
        // mesh) and the 2 scrollbar meshes (track + thumb). So the entity count is bounded by the
        // window + a constant, never by the row count.
        const int meshesPerRow = 3;      // arrow + row-fill + accent-bar
        const int tabCount = 3;          // Scene / Systems / Project
        const int tabLabels = tabCount;  // one label each
        const int tabMeshes = tabCount * 2; // fill + underline each
        const int scrollbarMeshes = 2;   // track + thumb
        int labelEntities;
        using (var set = world.GetEntities().With<DynamicTextComponent>().AsSet())
            labelEntities = set.GetEntities().Length;
        int meshEntities;
        using (var set = world.GetEntities().With<DrawComponent>().AsSet())
            meshEntities = set.GetEntities().Length;
        Assert.Equal(visible + tabLabels, labelEntities);
        Assert.Equal(visible * meshesPerRow + tabMeshes + scrollbarMeshes, meshEntities);
        Assert.True(labelEntities < panel.Rows.Count, "pooling should bound entities below the row count");
    }

    // ---- Disclosure arrows are triangle MESHES, not ASCII glyphs -----------

    [Fact]
    public void ArrowTriangle_PointsRightWhenCollapsed_DownWhenExpanded()
    {
        var rect = new Rectangle(10, 20, 12, 12);

        // Collapsed ▸ : the base is the left edge (two points share X), the apex is at the right.
        var collapsed = SystemsPanelLayout.ArrowTriangle(rect, expanded: false);
        Assert.Equal(collapsed[0].X, collapsed[1].X, 3);              // base points share the left X
        Assert.True(collapsed[2].X > collapsed[0].X);                 // apex is to the right of the base

        // Expanded ▾ : the base is the top edge (two points share Y), the apex is at the bottom.
        var expanded = SystemsPanelLayout.ArrowTriangle(rect, expanded: true);
        Assert.Equal(expanded[0].Y, expanded[1].Y, 3);               // base points share the top Y
        Assert.True(expanded[2].Y > expanded[0].Y);                  // apex is below the base
    }

    [Fact]
    public void DisclosureArrow_IsAMesh_NotATextGlyph()
    {
        using var world = new World();
        MakeCursor(world);
        var vm = Vm();
        var (panel, _, _, _) = MakeFlatPanel(world, vm);
        using var _1 = panel;

        panel.Update(Edit());

        // The section headers are collapsible → at least one disclosure arrow is drawn, as a filled
        // triangle mesh (DrawComponent, Type=Mesh, ≥3 vertices) — never a font glyph.
        var arrows = ArrowMeshes(world);
        Assert.NotEmpty(arrows);

        // And NO label carries the retired ASCII disclosure glyphs.
        var labels = VisibleLabels(world);
        Assert.DoesNotContain("v", labels);
        Assert.DoesNotContain(">", labels);
    }

    [Fact]
    public void GroupArrowMesh_OrientationTracksTheExpandedState()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var (panel, _) = MakeGroupPanel(world, vm);
        using var _1 = panel;

        panel.Update(Edit());
        var groupIdx = RowIndex(panel, r => r.Label == "logic [freeze]");
        var arrowRect = SystemsPanelLayout.ArrowRect(LineFor(panel, vm, groupIdx), panel.Rows[groupIdx].Depth);

        // Expanded by default → a down-pointing ▾ triangle mesh exists at the group's arrow rect.
        var expandedTri = SystemsPanelLayout.ArrowTriangle(arrowRect, expanded: true);
        Assert.Contains(ArrowMeshes(world), t => TriEquals(t, expandedTri));

        // Collapse the group → the same slot now holds a right-pointing ▸ triangle mesh.
        ClickArrow(panel, vm, cursor, groupIdx);
        panel.Update(Edit());
        var collapsedRect = SystemsPanelLayout.ArrowRect(LineFor(panel, vm, groupIdx), panel.Rows[groupIdx].Depth);
        var collapsedTri = SystemsPanelLayout.ArrowTriangle(collapsedRect, expanded: false);
        Assert.Contains(ArrowMeshes(world), t => TriEquals(t, collapsedTri));
    }

    /// <summary>Every arrow disclosure mesh's three points (a <c>FilledTriangleMeshGenerator</c> emits
    /// its A/B/C as the first three vertices).</summary>
    private static List<Vector2[]> ArrowMeshes(World world)
    {
        var result = new List<Vector2[]>();
        using var set = world.GetEntities().With<DrawComponent>().AsSet();
        foreach (var e in set.GetEntities())
        {
            var dc = e.Get<DrawComponent>();
            if (dc.Type != DrawElementType.Mesh || dc.Vertices is not { Length: >= 3 }) continue;
            result.Add(new[]
            {
                new Vector2(dc.Vertices[0].Position.X, dc.Vertices[0].Position.Y),
                new Vector2(dc.Vertices[1].Position.X, dc.Vertices[1].Position.Y),
                new Vector2(dc.Vertices[2].Position.X, dc.Vertices[2].Position.Y),
            });
        }
        return result;
    }

    private static bool TriEquals(Vector2[] a, Vector2[] b)
    {
        for (var i = 0; i < 3; i++)
            if (global::System.MathF.Abs(a[i].X - b[i].X) > 0.5f || global::System.MathF.Abs(a[i].Y - b[i].Y) > 0.5f)
                return false;
        return true;
    }

    // ---- Scene tree: two-way selection -------------------------------------

    [Fact]
    public void SceneRowClick_SelectsTheEntity()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var hero = MakeSceneEntity(world, "Hero");
        using var panel = new EditorPanelSystem(world, vm, font: null,
            () => ((EditorPipelineRegistrar?)null, (EditorPipelineRegistrar?)null));

        panel.Update(Edit());
        var idx = RowIndex(panel, r => r.Kind == PanelRowKind.SceneEntity && r.Label == "Hero");
        Assert.True(idx >= 0);
        ClickBody(panel, vm, cursor, idx);
        panel.Update(Edit());

        Assert.True(hero.Has<SelectedComponent>());
    }

    [Fact]
    public void SelectEntityByName_SelectsFromTheHeadlessChannel()
    {
        using var world = new World();
        MakeCursor(world);
        var hero = MakeSceneEntity(world, "Hero");
        using var panel = new EditorPanelSystem(world, Vm(), font: null,
            () => ((EditorPipelineRegistrar?)null, (EditorPipelineRegistrar?)null));

        Assert.True(panel.SelectEntityByName("Hero")); // the panel:select <name> op path
        Assert.True(hero.Has<SelectedComponent>());
        Assert.False(panel.SelectEntityByName("Nobody"));
    }

    [Fact]
    public void ScenesTab_SceneCatalogRowClick_ForwardsTheEntryToTheSelectCallback()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var catalog = new[]
        {
            new SceneCatalogEntry("island", "island", "Game", "island", IsCurrent: false),
            new SceneCatalogEntry("cove", "cove", "Game", "cove", IsCurrent: true),
        };
        SceneCatalogEntry? picked = null;
        using var panel = new EditorPanelSystem(world, vm, font: null,
            () => ((EditorPipelineRegistrar?)null, (EditorPipelineRegistrar?)null),
            sceneCatalog: () => catalog,
            selectScene: (e, _) => picked = e);
        panel.SetActiveTab(EditorPanelTab.Scenes);

        panel.Update(Edit());
        var idx = RowIndex(panel, r => r.Kind == PanelRowKind.SceneCatalogEntry && r.Label == "island");
        Assert.True(idx >= 0);
        ClickBody(panel, vm, cursor, idx);
        panel.Update(Edit());

        // The panel just forwards the row's entry — the dirty gate + confirm live behind the callback.
        Assert.NotNull(picked);
        Assert.Equal("island", picked!.Value.Key);
    }

    [Fact]
    public void ExternalSelection_IsHighlightedInTheTree()
    {
        using var world = new World();
        MakeCursor(world);
        var vm = Vm();
        var hero = MakeSceneEntity(world, "Hero");
        var villain = MakeSceneEntity(world, "Villain");
        using var panel = new EditorPanelSystem(world, vm, font: null,
            () => ((EditorPipelineRegistrar?)null, (EditorPipelineRegistrar?)null));

        // Selection made elsewhere (e.g. SelectionSystem from a viewport click).
        villain.Set(new SelectedComponent());
        panel.Update(Edit());

        var heroRow = panel.Rows.Single(r => r.Kind == PanelRowKind.SceneEntity && r.Label == "Hero");
        var villainRow = panel.Rows.Single(r => r.Kind == PanelRowKind.SceneEntity && r.Label == "Villain");
        Assert.True(villainRow.Selected);
        Assert.False(heroRow.Selected);
    }

    [Fact]
    public void SceneTree_HidesEditorInfrastructureEntities()
    {
        using var world = new World();
        MakeCursor(world);
        var vm = Vm();
        MakeSceneEntity(world, "Hero");
        var infra = MakeSceneEntity(world, "ChromeThing");
        infra.Set(new EditorInfrastructureComponent());
        using var panel = new EditorPanelSystem(world, vm, font: null,
            () => ((EditorPipelineRegistrar?)null, (EditorPipelineRegistrar?)null));

        panel.Update(Edit());

        Assert.Contains(panel.Rows, r => r.Kind == PanelRowKind.SceneEntity && r.Label == "Hero");
        Assert.DoesNotContain(panel.Rows, r => r.Kind == PanelRowKind.SceneEntity && r.Label == "ChromeThing");
    }

    // ---- Inspector panel (RightInspector role): component list + members ---

    /// <summary>A dedicated Inspector-role panel (the right strip). Its body is the selection's
    /// components — no tabs, no pipelines.</summary>
    private static EditorPanelSystem MakeInspectorPanel(World world, ViewportManager vm) =>
        new(world, vm, font: null, role: EditorPanelRole.RightInspector);

    [Fact]
    public void InspectorPanel_ListsSelectedEntityComponents_AndExpandsMembers()
    {
        using var world = new World();
        MakeCursor(world);
        var vm = Vm();
        var hero = MakeSceneEntity(world, "Hero"); // Transform + EntityInfo
        hero.Set(new SelectedComponent());
        using var panel = MakeInspectorPanel(world, vm);

        panel.Update(Edit());

        // Component list (item 3): the selection's component type names appear as Inspector rows.
        Assert.Contains(panel.Rows, r => r.Kind == PanelRowKind.InspectorComponent && r.Label == nameof(EntityInfoComponent));
        Assert.Contains(panel.Rows, r => r.Kind == PanelRowKind.InspectorComponent && r.Label == nameof(TransformComponent));
        // Members hidden until expanded.
        Assert.DoesNotContain(panel.Rows, r => r.Kind == PanelRowKind.InspectorMember);

        // Expand EntityInfoComponent (item 4): its member values appear.
        panel.ToggleInspectorComponentKey(nameof(EntityInfoComponent));
        panel.Update(Edit());
        Assert.Contains(panel.Rows, r => r.Kind == PanelRowKind.InspectorMember && r.Label == "Name: Hero");
        Assert.Contains(panel.Rows, r => r.Kind == PanelRowKind.InspectorMember && r.Label == "Type: Hero");
    }

    [Fact]
    public void InspectorPanel_NoSelection_ShowsPlaceholder()
    {
        using var world = new World();
        MakeCursor(world);
        var vm = Vm();
        using var panel = MakeInspectorPanel(world, vm);

        panel.Update(Edit());
        Assert.Contains(panel.Rows, r => r.Kind == PanelRowKind.Info && r.Label == "(no selection)");
    }

    // ---- Two-way selection ACROSS the two panels (UX2-B) -------------------

    [Fact]
    public void LeftTreeClick_UpdatesTheRightInspectorPanel()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var hero = MakeSceneEntity(world, "Hero"); // Transform + EntityInfo

        // The two real panels the overlay composes, sharing ONE panel state — the left tabbed panel
        // (Entities tab) and the dedicated right Inspector panel.
        var shell = new EditorShellStateComponent();
        var panelState = new EditorPanelStateComponent();
        using var left = new EditorPanelSystem(world, vm, font: null,
            () => ((EditorPipelineRegistrar?)null, (EditorPipelineRegistrar?)null),
            shellState: shell, role: EditorPanelRole.LeftTabs, panelState: panelState);
        using var inspector = new EditorPanelSystem(world, vm, font: null,
            shellState: shell, role: EditorPanelRole.RightInspector, panelState: panelState);

        left.Update(Edit());
        inspector.Update(Edit());
        // Nothing selected yet → the Inspector shows the placeholder.
        Assert.Contains(inspector.Rows, r => r.Kind == PanelRowKind.Info && r.Label == "(no selection)");

        // Click the Hero row in the LEFT panel's Entities tree.
        var idx = RowIndex(left, r => r.Kind == PanelRowKind.SceneEntity && r.Label == "Hero");
        Assert.True(idx >= 0);
        ClickBody(left, vm, cursor, idx);
        left.Update(Edit());
        Assert.True(hero.Has<SelectedComponent>());

        // Next frame the RIGHT Inspector panel binds to that same SelectedComponent — two-way across panels.
        inspector.Update(Edit());
        Assert.Contains(inspector.Rows, r => r.Kind == PanelRowKind.InspectorComponent && r.Label == nameof(EntityInfoComponent));
        Assert.DoesNotContain(inspector.Rows, r => r.Kind == PanelRowKind.Info && r.Label == "(no selection)");
    }
}
