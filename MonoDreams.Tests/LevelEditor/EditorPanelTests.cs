#nullable enable
using System.Collections.Generic;
using System.Linq;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Physics;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
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
        // shell's runtime region sizes (UX2-B — the tab group moved to the left strip). On the
        // Entities tab the body starts one band lower: the HP panel toolbar (+ Add / focus) sits
        // between the tab strip and the tree.
        var panelRect = EditorChromeLayout.LeftPanel(vm.ScreenWidth, vm.ScreenHeight, vm.DevicePixelRatio,
            panel.ShellState.LeftWidthPt, panel.ShellState.BottomHeightPt);
        var body = EditorChromeLayout.RegionBody(panelRect, vm.DevicePixelRatio);
        if (panel.ShowsPanelToolbar)
        {
            var band = EditorChromeLayout.Px(EditorChromeLayout.TabStripHeight, vm.DevicePixelRatio);
            body = new Rectangle(body.X, body.Y + band, body.Width, body.Height - band);
        }
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
        // PF-A added two pooled entities per slot: a second label (the member value / inline-edit text)
        // and a delete-× mesh. The field-background box is a SimpleButtonComponent (counted in neither
        // set). Created for both roles (parked on the left panel's rows), so the count stays a function of
        // the visible window + a fixed overhead, never the row count.
        // HP added three more pooled meshes per slot (a layer row's active-radio / eye / padlock
        // glyphs, emptied on every non-layer row) and four persistent chrome meshes (the Entities
        // panel toolbar's + Add and focus buttons — a fill + an icon glyph each). Both are
        // row-count-independent, so the bound still holds.
        const int labelsPerRow = 2;      // row label + value label
        const int meshesPerRow = 7;      // arrow + row-fill + accent-bar + delete-glyph + radio/eye/lock
        const int tabCount = 3;          // Entities / Systems / Scenes
        const int tabLabels = tabCount;  // one label each
        const int tabMeshes = tabCount * 2; // fill + underline each
        const int panelToolbarMeshes = 4;   // + Add fill/glyph + focus fill/glyph (HP)
        const int scrollbarMeshes = 2;   // track + thumb
        int labelEntities;
        using (var set = world.GetEntities().With<DynamicTextComponent>().AsSet())
            labelEntities = set.GetEntities().Length;
        int meshEntities;
        using (var set = world.GetEntities().With<DrawComponent>().AsSet())
            meshEntities = set.GetEntities().Length;
        Assert.Equal(visible * labelsPerRow + tabLabels, labelEntities);
        Assert.Equal(visible * meshesPerRow + tabMeshes + panelToolbarMeshes + scrollbarMeshes, meshEntities);
        Assert.True(visible < panel.Rows.Count, "the window is smaller than the content → pooling is active");
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

    [Fact]
    public void SceneTree_IncludesTheCameraEntity_LabeledCamera_AndSelectsIt()
    {
        // CM: the camera is an ordinary scene entity now (EntityInfoComponent("Camera") + Transform +
        // CameraComponent, SceneObjectComponent-tagged, NOT infra), so it appears in the tree naturally as
        // a "Camera" row — no special fold. Editor infrastructure still stays hidden.
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        MakeSceneEntity(world, "Hero");
        // The camera entity: a normal scene root labelled by its EntityInfoComponent.
        var camera = world.CreateEntity();
        camera.Set(new SceneObjectComponent());
        camera.Set(new EntityInfoComponent("Camera"));
        camera.Set(new TransformComponent(Vector2.Zero));
        camera.Set(new CameraComponent { Zoom = 1f });
        // A second, ordinary infra entity that must STAY hidden (proves infra is still filtered).
        var otherInfra = MakeSceneEntity(world, "ChromeThing");
        otherInfra.Set(new EditorInfrastructureComponent());
        using var panel = new EditorPanelSystem(world, vm, font: null,
            () => ((EditorPipelineRegistrar?)null, (EditorPipelineRegistrar?)null));

        panel.Update(Edit());

        var idx = RowIndex(panel, r => r.Kind == PanelRowKind.SceneEntity && r.Label == "Camera");
        Assert.True(idx >= 0, "the camera entity should appear as a 'Camera' row in the Entities tree");
        Assert.Equal(camera, panel.Rows[idx].Entity);
        Assert.DoesNotContain(panel.Rows, r => r.Kind == PanelRowKind.SceneEntity && r.Label == "ChromeThing");

        // Clicking the row selects the camera exactly like any entity (two-way selection).
        ClickBody(panel, vm, cursor, idx);
        panel.Update(Edit());
        Assert.True(camera.Has<SelectedComponent>());
    }

    // ---- The layers panel (HP): layer rows, the glyph slots, and the bake-product filter ----

    /// <summary>A scene LAYER entity: an ordinary entity carrying a <c>SceneLayerComponent</c>. Its
    /// name is its <c>EntityInfoComponent.Name</c> (the level-loading layers premise).</summary>
    private static Entity MakeLayer(World world, string name, int order,
        bool locked = false, bool screenSpace = false, bool visible = true)
    {
        var layer = world.CreateEntity();
        layer.Set(new TransformComponent(Vector2.Zero));
        layer.Set(new EntityInfoComponent("Layer", name));
        layer.Set(new MonoDreams.Component.Level.SceneLayerComponent
        {
            Order = order, Locked = locked, ScreenSpace = screenSpace, Visible = visible,
        });
        return layer;
    }

    /// <summary>Clicks the given LAYER glyph slot (0 = the ACTIVE radio, 1 = the eye, 2 = the
    /// padlock) of a row, rather than its body.</summary>
    private void ClickLayerSlot(EditorPanelSystem panel, ViewportManager vm, Entity cursor, int rowIndex, int slot)
    {
        var line = LineFor(panel, vm, rowIndex);
        var rect = SystemsPanelLayout.LayerToggleRect(line, slot, vm.DevicePixelRatio, panel.Rows[rowIndex].Depth);
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(rect.Center.X, rect.Center.Y);
        input.LeftButtonReleased = true;
    }

    [Fact]
    public void LayerRows_ListFirst_FrontMostOnTop_AndTheTopWorldLayerIsActiveByDefault()
    {
        using var world = new World();
        MakeCursor(world);
        var vm = Vm();
        var back = MakeLayer(world, "Background", 0);
        var front = MakeLayer(world, "Props", 1);
        var hud = MakeLayer(world, "HUD", 5, screenSpace: true);
        MakeSceneEntity(world, "Loose"); // an entity on no layer still shows, after the layers
        var shell = new EditorShellStateComponent();
        using var panel = new EditorPanelSystem(world, vm, font: null,
            () => ((EditorPipelineRegistrar?)null, (EditorPipelineRegistrar?)null), shellState: shell);

        panel.Update(Edit());

        var layerRows = panel.Rows
            .Where(r => r.Kind == PanelRowKind.SceneEntity && r.Entity.Has<MonoDreams.Component.Level.SceneLayerComponent>())
            .Select(r => r.Label).ToList();
        // Top of the list = front of the draw (the Figma/Aseprite convention), and a screen-space
        // grouping reads as "(hud)".
        Assert.Equal(new[] { "HUD (hud)", "Props", "Background" }, layerRows);
        // The layers precede everything else in the tree.
        var looseIndex = RowIndex(panel, r => r.Kind == PanelRowKind.SceneEntity && r.Label == "Loose");
        var backIndex = RowIndex(panel, r => r.Kind == PanelRowKind.SceneEntity && r.Entity == back);
        Assert.True(backIndex < looseIndex);

        // Active-layer healing defaults to the top WORLD layer — never the HUD grouping.
        Assert.Equal(front, shell.ActiveLayer);
        Assert.NotEqual(hud, shell.ActiveLayer);
    }

    [Fact]
    public void LayerRowBodyClick_SelectsAndActivates_WhileTheRadioSlotActivatesWithoutSelecting()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var back = MakeLayer(world, "Background", 0);
        var front = MakeLayer(world, "Props", 1);
        var shell = new EditorShellStateComponent();
        using var panel = new EditorPanelSystem(world, vm, font: null,
            () => ((EditorPipelineRegistrar?)null, (EditorPipelineRegistrar?)null), shellState: shell);

        panel.Update(Edit());
        Assert.Equal(front, shell.ActiveLayer); // the healed default (front-most world layer)

        // BODY click on the unlocked world layer: selects AND activates ("click the layer, then place").
        var backIndex = RowIndex(panel, r => r.Entity == back);
        ClickBody(panel, vm, cursor, backIndex);
        panel.Update(Edit());
        Assert.True(back.Has<SelectedComponent>());
        Assert.Equal(back, shell.ActiveLayer);

        // RADIO slot click on the OTHER layer: activates it without touching the selection — the
        // explicit activate-without-reselecting verb.
        var frontIndex = RowIndex(panel, r => r.Entity == front);
        ClickLayerSlot(panel, vm, cursor, frontIndex, slot: 0);
        panel.Update(Edit());
        Assert.Equal(front, shell.ActiveLayer);
        Assert.True(back.Has<SelectedComponent>());   // selection unchanged
        Assert.False(front.Has<SelectedComponent>());
    }

    [Fact]
    public void LockedOrScreenSpaceLayerBodyClick_Selects_ButNeverActivates()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var world1 = MakeLayer(world, "Props", 1);
        var locked = MakeLayer(world, "Terrain", 0, locked: true);
        var hud = MakeLayer(world, "HUD", 9, screenSpace: true);
        var shell = new EditorShellStateComponent();
        using var panel = new EditorPanelSystem(world, vm, font: null,
            () => ((EditorPipelineRegistrar?)null, (EditorPipelineRegistrar?)null), shellState: shell);

        panel.Update(Edit());
        Assert.Equal(world1, shell.ActiveLayer);

        // A LOCKED layer selects (so its Inspector settings — including the padlock — are editable)
        // but is never a placement target.
        ClickBody(panel, vm, cursor, RowIndex(panel, r => r.Entity == locked));
        panel.Update(Edit());
        Assert.True(locked.Has<SelectedComponent>());
        Assert.Equal(world1, shell.ActiveLayer);

        // Same for a SCREEN-SPACE (HUD) grouping: organizational only.
        ClickBody(panel, vm, cursor, RowIndex(panel, r => r.Entity == hud));
        panel.Update(Edit());
        Assert.True(hud.Has<SelectedComponent>());
        Assert.Equal(world1, shell.ActiveLayer);
    }

    [Fact]
    public void EyeAndPadlockSlots_ToggleVisibleAndLocked_AndMarkTheHistoryDirty()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var layer = MakeLayer(world, "Props", 0);
        var history = new EditorHistory(world);
        using var panel = new EditorPanelSystem(world, vm, font: null,
            () => ((EditorPipelineRegistrar?)null, (EditorPipelineRegistrar?)null), history: history);

        panel.Update(Edit());
        var idx = RowIndex(panel, r => r.Entity == layer);
        var data = layer.Get<MonoDreams.Component.Level.SceneLayerComponent>();
        Assert.True(data.Visible);
        Assert.False(data.Locked);

        ClickLayerSlot(panel, vm, cursor, idx, slot: 1); // the eye
        panel.Update(Edit());
        Assert.False(layer.Get<MonoDreams.Component.Level.SceneLayerComponent>().Visible);
        Assert.True(history.IsDirty);
        // A visibility toggle is not a selection change.
        Assert.False(layer.Has<SelectedComponent>());

        ClickLayerSlot(panel, vm, cursor, idx, slot: 2); // the padlock
        panel.Update(Edit());
        Assert.True(layer.Get<MonoDreams.Component.Level.SceneLayerComponent>().Locked);
        Assert.False(layer.Has<SelectedComponent>());
    }

    [Fact]
    public void Layers_SeedCollapsedOnFirstSighting_AndADesignersExpandSticks()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var layer = MakeLayer(world, "Props", 0);
        var member = MakeSceneEntity(world, "Tree");
        member.Set(new ChildOfComponent(layer));
        using var panel = new EditorPanelSystem(world, vm, font: null,
            () => ((EditorPipelineRegistrar?)null, (EditorPipelineRegistrar?)null));

        // First sighting: the layer's subtree is seeded collapsed, so the tree opens on the LAYER
        // LIST instead of one layer's members pushing every other layer below the fold.
        panel.Update(Edit());
        Assert.DoesNotContain(panel.Rows, r => r.Kind == PanelRowKind.SceneEntity && r.Entity == member);

        // The designer expands it via the disclosure arrow…
        ClickArrow(panel, vm, cursor, RowIndex(panel, r => r.Entity == layer));
        panel.Update(Edit());
        Assert.Contains(panel.Rows, r => r.Kind == PanelRowKind.SceneEntity && r.Entity == member);

        // …and it STAYS expanded on later frames (seeding is once-per-layer, the toggle owns it after).
        panel.Update(Edit());
        panel.Update(Edit());
        Assert.Contains(panel.Rows, r => r.Kind == PanelRowKind.SceneEntity && r.Entity == member);
    }

    [Fact]
    public void SceneTree_HidesBakeProducts()
    {
        using var world = new World();
        MakeCursor(world);
        var vm = Vm();
        MakeSceneEntity(world, "Boundary");
        // Bake products (boundary segment colliders, …) are derived children that never serialize —
        // showing them would swamp the tree by the hundreds.
        var baked = MakeSceneEntity(world, "BoundarySegment");
        baked.Set(new BakedProductComponent());
        using var panel = new EditorPanelSystem(world, vm, font: null,
            () => ((EditorPipelineRegistrar?)null, (EditorPipelineRegistrar?)null));

        panel.Update(Edit());

        Assert.Contains(panel.Rows, r => r.Kind == PanelRowKind.SceneEntity && r.Label == "Boundary");
        Assert.DoesNotContain(panel.Rows, r => r.Kind == PanelRowKind.SceneEntity && r.Label == "BoundarySegment");
    }

    // ---- The panel toolbar (HP): + Add / focus, LeftTabs + Entities tab only ----

    [Fact]
    public void PanelToolbarButtons_RaiseTheAddMenuAndFocusRequests_AndNeverFallThroughToRows()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var hero = MakeSceneEntity(world, "Hero");
        using var panel = new EditorPanelSystem(world, vm, font: null,
            () => ((EditorPipelineRegistrar?)null, (EditorPipelineRegistrar?)null));
        Rectangle? addAnchor = null;
        var focusRequests = 0;
        panel.AddMenuRequested = (_, anchor) => addAnchor = anchor;
        panel.FocusSelectionRequested = () => focusRequests++;

        panel.Update(Edit());
        Assert.True(panel.ShowsPanelToolbar); // LeftTabs + the Entities tab

        // The toolbar band sits between the tab strip and the tree.
        var panelRect = EditorChromeLayout.LeftPanel(vm.ScreenWidth, vm.ScreenHeight, vm.DevicePixelRatio,
            panel.ShellState.LeftWidthPt, panel.ShellState.BottomHeightPt);
        var header = EditorChromeLayout.TabStrip(panelRect, vm.DevicePixelRatio);
        var band = EditorChromeLayout.Px(EditorChromeLayout.TabStripHeight, vm.DevicePixelRatio);
        var toolbar = new Rectangle(panelRect.X, header.Bottom, panelRect.Width, band);

        // The + Add button (left-anchored) raises the request with its own bounds as the anchor.
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(toolbar.X + band / 2f, toolbar.Center.Y);
        input.LeftButtonReleased = true;
        panel.Update(Edit());
        Assert.NotNull(addAnchor);
        Assert.True(toolbar.Contains(addAnchor!.Value.Center));

        // The focus button (right-anchored, clear of the scrollbar gutter).
        input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(
            toolbar.Right - EditorChromeLayout.Px(12, vm.DevicePixelRatio) - EditorChromeLayout.Px(10, vm.DevicePixelRatio),
            toolbar.Center.Y);
        input.LeftButtonReleased = true;
        panel.Update(Edit());
        Assert.Equal(1, focusRequests);

        // A click in the band but on NEITHER button is consumed — it never falls through to a row.
        Assert.False(hero.Has<SelectedComponent>());
        input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(toolbar.Center.X, toolbar.Center.Y);
        input.LeftButtonReleased = true;
        panel.Update(Edit());
        Assert.False(hero.Has<SelectedComponent>());
    }

    [Fact]
    public void PanelToolbar_IsEntitiesTabOnly()
    {
        using var world = new World();
        MakeCursor(world);
        var vm = Vm();
        using var left = new EditorPanelSystem(world, vm, font: null,
            () => ((EditorPipelineRegistrar?)null, (EditorPipelineRegistrar?)null));
        Assert.True(left.ShowsPanelToolbar);
        left.SetActiveTab(EditorPanelTab.Systems);
        Assert.False(left.ShowsPanelToolbar);
        left.SetActiveTab(EditorPanelTab.Scenes);
        Assert.False(left.ShowsPanelToolbar);

        using var inspector = new EditorPanelSystem(world, vm, font: null, role: EditorPanelRole.RightInspector);
        Assert.False(inspector.ShowsPanelToolbar);
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

        // Expand EntityInfoComponent (item 4): its member values appear (PF-A splits name/value).
        panel.ToggleInspectorComponentKey(nameof(EntityInfoComponent));
        panel.Update(Edit());
        Assert.Contains(panel.Rows, r => r.Kind == PanelRowKind.InspectorMember && r.MemberName == "Name" && r.MemberValue == "Hero");
        Assert.Contains(panel.Rows, r => r.Kind == PanelRowKind.InspectorMember && r.MemberName == "Type" && r.MemberValue == "Hero");
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

    // ---- UX2-D: the left panel's right-click raises the context-menu request + maps the row entity ----

    [Fact]
    public void RightClickInThePanel_RaisesTheContextMenuRequest_AndMapsTheRowEntity()
    {
        using var world = new World();
        var vm = Vm();
        var cursor = MakeCursor(world);
        var entity = MakeSceneEntity(world, "Tree");
        using var panel = new EditorPanelSystem(world, vm, font: null);
        panel.SetActiveTab(EditorPanelTab.Entities);

        var requests = 0;
        panel.ContextMenuRequested = _ => requests++;

        panel.Update(Edit()); // build the rows so we can find the entity's row
        var rowIndex = RowIndex(panel, r => r.Kind == PanelRowKind.SceneEntity && r.Entity == entity);
        Assert.True(rowIndex >= 0);
        var line = LineFor(panel, vm, rowIndex);

        // A right-press over the row fires the request; EntityAtPoint maps the row's screen point back
        // to the entity (the overlay uses it to build the Entities menu for that row).
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(line.Center.X, line.Center.Y);
        input.RightButtonPressed = input.RightButton = true;
        panel.Update(Edit());

        Assert.Equal(1, requests);
        Assert.Equal(entity, panel.EntityAtPoint(new Point(line.Center.X, line.Center.Y)));
        // The right-press was consumed (so it does not also reach the palette's disarm downstream).
        Assert.False(cursor.Get<CursorInputComponent>().RightButtonPressed);
    }

    // ---- PF-A: the editable Inspector (value edits, add/remove, filter, keyboard ownership) ----------

    private struct Widget { public bool On; public int Count; public WidgetMode Mode; }
    private enum WidgetMode { A, B, C }

    private static (EditorPanelSystem panel, EditorHistory history, ComponentSerializerRegistry registry)
        EditableInspector(World world, ViewportManager vm, KeyboardState[] kb)
    {
        var history = new EditorHistory(world);
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        var panel = new EditorPanelSystem(world, vm, font: null, role: EditorPanelRole.RightInspector,
            history: history, registry: registry, getKeyboardState: () => kb[0]);
        return (panel, history, registry);
    }

    private static Rectangle RightLine(EditorPanelSystem panel, ViewportManager vm, int rowIndex)
    {
        var rect = EditorChromeLayout.RightPanel(vm.ScreenWidth, vm.ScreenHeight, vm.DevicePixelRatio,
            panel.ShellState.RightWidthPt, panel.ShellState.BottomHeightPt);
        var body = EditorChromeLayout.RegionBody(rect, vm.DevicePixelRatio);
        return SystemsPanelLayout.LineRect(body, rowIndex - panel.ScrollOffset, vm.DevicePixelRatio);
    }

    private static void ClickRight(EditorPanelSystem panel, ViewportManager vm, Entity cursor, int rowIndex, Point? at = null)
    {
        var line = RightLine(panel, vm, rowIndex);
        var p = at ?? new Point(line.Center.X, line.Center.Y);
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(p.X, p.Y);
        input.LeftButtonReleased = true;
    }

    private static Entity SelectedWidget(World world, Widget widget)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(Vector2.Zero));
        e.Set(widget);
        e.Set(new SelectedComponent());
        return e;
    }

    [Fact]
    public void Inspector_ClickBoolMember_TogglesImmediately_Undoable_NoField()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var kb = new[] { new KeyboardState() };
        var hero = SelectedWidget(world, new Widget { On = false });
        var (panel, history, _) = EditableInspector(world, vm, kb);
        using var _1 = panel;

        panel.Update(Edit());
        panel.ToggleInspectorComponentKey(nameof(Widget));
        panel.Update(Edit());

        var idx = RowIndex(panel, r => r.Kind == PanelRowKind.InspectorMember && r.MemberName == nameof(Widget.On));
        Assert.True(idx >= 0);
        ClickRight(panel, vm, cursor, idx);
        panel.Update(Edit());

        Assert.True(hero.Get<Widget>().On);   // a bool click toggles immediately
        Assert.True(history.IsDirty);         // one undoable command
        Assert.False(panel.IsEditingMember);  // no inline field opened
        history.Undo();
        Assert.False(hero.Get<Widget>().On);
    }

    [Fact]
    public void Inspector_ClickEnumMember_CyclesToNext_Undoable()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var kb = new[] { new KeyboardState() };
        var hero = SelectedWidget(world, new Widget { Mode = WidgetMode.A });
        var (panel, history, _) = EditableInspector(world, vm, kb);
        using var _1 = panel;

        panel.Update(Edit());
        panel.ToggleInspectorComponentKey(nameof(Widget));
        panel.Update(Edit());

        var idx = RowIndex(panel, r => r.Kind == PanelRowKind.InspectorMember && r.MemberName == nameof(Widget.Mode));
        ClickRight(panel, vm, cursor, idx);
        panel.Update(Edit());

        Assert.Equal(WidgetMode.B, hero.Get<Widget>().Mode); // cycled to the next member
        Assert.True(history.IsDirty);
        Assert.False(panel.IsEditingMember);
    }

    [Fact]
    public void Inspector_ClickIntMember_OpensField_EnterCommits_Dirty()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var kb = new[] { new KeyboardState() };
        var hero = SelectedWidget(world, new Widget { Count = 3 });
        var (panel, history, _) = EditableInspector(world, vm, kb);
        using var _1 = panel;

        panel.Update(Edit());
        panel.ToggleInspectorComponentKey(nameof(Widget));
        panel.Update(Edit());

        var idx = RowIndex(panel, r => r.Kind == PanelRowKind.InspectorMember && r.MemberName == nameof(Widget.Count));
        ClickRight(panel, vm, cursor, idx);
        panel.Update(Edit());
        Assert.True(panel.IsEditingMember);           // click a value → an inline field opens
        Assert.False(history.IsDirty);                // nothing committed yet

        cursor.Get<CursorInputComponent>().LeftButtonReleased = false; // clear the stale click edge
        kb[0] = new KeyboardState(Keys.Enter);
        panel.Update(Edit());
        Assert.False(panel.IsEditingMember);           // Enter commits + closes
        Assert.True(history.IsDirty);                  // one undoable command
    }

    [Fact]
    public void Inspector_ClickValue_Escape_Cancels_NoCommand()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var kb = new[] { new KeyboardState() };
        SelectedWidget(world, new Widget { Count = 3 });
        var (panel, history, _) = EditableInspector(world, vm, kb);
        using var _1 = panel;

        panel.Update(Edit());
        panel.ToggleInspectorComponentKey(nameof(Widget));
        panel.Update(Edit());

        var idx = RowIndex(panel, r => r.Kind == PanelRowKind.InspectorMember && r.MemberName == nameof(Widget.Count));
        ClickRight(panel, vm, cursor, idx);
        panel.Update(Edit());
        Assert.True(panel.IsEditingMember);

        cursor.Get<CursorInputComponent>().LeftButtonReleased = false;
        kb[0] = new KeyboardState(Keys.Escape);
        panel.Update(Edit());
        Assert.False(panel.IsEditingMember);  // cancelled
        Assert.False(history.IsDirty);        // no command pushed
    }

    [Fact]
    public void Inspector_FilterField_FocusType_EscClearsAndUnfocuses()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var kb = new[] { new KeyboardState() };
        SelectedWidget(world, new Widget());
        var (panel, _, _2) = EditableInspector(world, vm, kb);
        using var _1 = panel;

        panel.Update(Edit());
        var fidx = RowIndex(panel, r => r.Kind == PanelRowKind.InspectorFilter);
        Assert.True(fidx >= 0);
        ClickRight(panel, vm, cursor, fidx);
        panel.Update(Edit());
        Assert.True(panel.OwnsKeyboard); // the filter field owns the keyboard

        cursor.Get<CursorInputComponent>().LeftButtonReleased = false;
        kb[0] = new KeyboardState(Keys.A);
        panel.Update(Edit());
        Assert.Equal("a", panel.State.InspectorFilter);

        kb[0] = new KeyboardState();          // release
        panel.Update(Edit());
        kb[0] = new KeyboardState(Keys.Escape);
        panel.Update(Edit());
        Assert.Equal(string.Empty, panel.State.InspectorFilter); // Esc clears
        Assert.False(panel.OwnsKeyboard);                        // + unfocuses
    }

    [Fact]
    public void Inspector_DeleteTransformRefused_DeleteRigidBodyRemoves_Undoable()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var kb = new[] { new KeyboardState() };
        var hero = world.CreateEntity();
        hero.Set(new TransformComponent(Vector2.Zero));
        hero.Set(new RigidBodyComponent());
        hero.Set(new SelectedComponent());
        var (panel, history, _) = EditableInspector(world, vm, kb);
        using var _1 = panel;

        panel.Update(Edit());

        // Transform's × is Guarded → clicking it refuses (Transform stays, no edit).
        var tIdx = RowIndex(panel, r => r.Kind == PanelRowKind.InspectorComponent && r.Label == nameof(TransformComponent));
        Assert.Equal(InspectorDeleteAffordance.Guarded, panel.Rows[tIdx].DeleteAffordance);
        var tDelete = SystemsPanelLayout.DeleteRect(RightLine(panel, vm, tIdx));
        ClickRight(panel, vm, cursor, tIdx, new Point(tDelete.Center.X, tDelete.Center.Y));
        panel.Update(Edit());
        Assert.True(hero.Has<TransformComponent>());
        Assert.False(history.IsDirty);

        // RigidBody's × is Deletable → clicking it removes it (undoable).
        var rIdx = RowIndex(panel, r => r.Kind == PanelRowKind.InspectorComponent && r.Label == nameof(RigidBodyComponent));
        Assert.Equal(InspectorDeleteAffordance.Deletable, panel.Rows[rIdx].DeleteAffordance);
        var rDelete = SystemsPanelLayout.DeleteRect(RightLine(panel, vm, rIdx));
        ClickRight(panel, vm, cursor, rIdx, new Point(rDelete.Center.X, rDelete.Center.Y));
        panel.Update(Edit());
        Assert.False(hero.Has<RigidBodyComponent>());
        Assert.True(history.IsDirty);
        history.Undo();
        Assert.True(hero.Has<RigidBodyComponent>());
    }

    [Fact]
    public void Inspector_AddRowClick_RaisesRequest_AndCandidatesExcludePresentAndStructural()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var kb = new[] { new KeyboardState() };
        var hero = world.CreateEntity();
        hero.Set(new TransformComponent(Vector2.Zero));
        hero.Set(new RigidBodyComponent());
        hero.Set(new SelectedComponent());
        var (panel, _, _2) = EditableInspector(world, vm, kb);
        using var _1 = panel;

        var raised = 0;
        panel.AddComponentRequested = _ => raised++;

        panel.Update(Edit());
        var idx = RowIndex(panel, r => r.Kind == PanelRowKind.InspectorAddComponent);
        Assert.True(idx >= 0);
        ClickRight(panel, vm, cursor, idx);
        panel.Update(Edit());
        Assert.Equal(1, raised);

        var types = panel.AddComponentCandidates().Select(c => c.Type).ToHashSet();
        Assert.DoesNotContain(typeof(TransformComponent), types); // present
        Assert.DoesNotContain(typeof(RigidBodyComponent), types); // present
        Assert.Contains(typeof(VelocityComponent), types);        // registered, not present, addable
    }
}
