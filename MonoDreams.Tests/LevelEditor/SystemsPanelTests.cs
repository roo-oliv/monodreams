#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.UI;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.UI;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the Wave-8a premise "The systems panel lists every registrar entry and toggles it
/// through the registrar" (<c>SystemsPanelTests</c>): rows mirror BOTH pipelines' entries in
/// order (name + policy tag + a checkbox reflecting the live enabled state); a click on a row
/// flips the entry via <c>SetEnabled</c> and the gated system actually stops (side-effect
/// counter); the panel is inert in Play; the wheel scrolls by whole clamped lines; and the panel
/// refuses to disable its own entry. Pure logic — no GraphicsDevice: the panel takes a null font
/// (layout-only, mirroring the chrome builder's test seam) and a hand-built
/// <see cref="ViewportManager"/>.
/// </summary>
public class SystemsPanelTests
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

    /// <summary>A panel over two small registrars (update: logic Freeze + counter RunNormally;
    /// draw: render RunNormally), pre-bound and built by one Edit frame.</summary>
    private static (SystemsPanelSystem panel, EditorPipelineRegistrar update, EditorPipelineRegistrar draw,
        CountingSystem logic, CountingSystem render, SequentialSystem<GameState> updatePipeline)
        MakePanel(World world, ViewportManager vm)
    {
        var update = new EditorPipelineRegistrar();
        var draw = new EditorPipelineRegistrar();
        var logic = new CountingSystem();
        var render = new CountingSystem();
        update.Add("logic", logic, EditTimeBehavior.Freeze);
        var updatePipeline = update.Build(); // entries are fixed after Build, like a real screen
        draw.Add("renderMain", render, EditTimeBehavior.RunNormally);
        draw.Build();

        var panel = new SystemsPanelSystem(world, vm, font: null, () => (update, draw));
        return (panel, update, draw, logic, render, updatePipeline);
    }

    private static List<string> LabelTexts(World world)
    {
        var texts = new List<string>();
        using var set = world.GetEntities().With<DynamicTextComponent>().AsSet();
        foreach (var e in set.GetEntities())
            texts.Add(e.Get<DynamicTextComponent>().TextContent);
        return texts;
    }

    // ---- Rows reflect the registrar entries (both pipelines, order, policy tag, enabled state) ----

    [Fact]
    public void SystemsPanel_RowsMirrorBothPipelinesEntries_WithPolicyTags()
    {
        using var world = new World();
        MakeCursor(world);
        var (panel, _, _, _, _, _) = MakePanel(world, Vm());
        using var _1 = panel;

        panel.Update(Edit());

        var labels = LabelTexts(world);
        // The three collapsible section headers, the pipeline sub-headers, and every entry of both
        // pipelines, with the policy tag on Freeze.
        Assert.Contains("SYSTEMS", labels);
        Assert.Contains("SCENE", labels);
        Assert.Contains("INSPECTOR", labels);
        Assert.Contains("UPDATE", labels);
        Assert.Contains("DRAW", labels);
        Assert.Contains("logic [freeze]", labels);
        Assert.Contains("renderMain", labels);
    }

    [Fact]
    public void SystemsPanel_CheckboxesReflectTheLiveEnabledState()
    {
        using var world = new World();
        MakeCursor(world);
        var (panel, update, _, _, _, _) = MakePanel(world, Vm());
        using var _1 = panel;

        panel.Update(Edit());

        // Both checkboxes start enabled (filled with the on-color).
        using var boxes = world.GetEntities().With<SimpleButtonComponent>().AsSet();
        var fills = new List<Color>();
        foreach (var e in boxes.GetEntities()) fills.Add(e.Get<SimpleButtonComponent>().FillColor);
        Assert.Equal(2, fills.Count);
        Assert.All(fills, f => Assert.Equal(EditorChromeBuilder.CheckboxOnFill, f));

        // Disable one through the registrar (as any tooling might) → its checkbox empties next frame.
        update.SetEnabled("logic", false);
        panel.Update(Edit());
        var offCount = 0;
        foreach (var e in boxes.GetEntities())
            if (e.Get<SimpleButtonComponent>().FillColor.A == 0) offCount++;
        Assert.Equal(1, offCount);
    }

    // ---- A toggle click calls SetEnabled and the gated system actually stops ----

    [Fact]
    public void SystemsPanel_ClickOnARow_TogglesTheEntry_AndTheSystemStops()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var (panel, update, _, logic, _, updatePipeline) = MakePanel(world, Vm());
        using var _1 = panel;

        panel.Update(Edit()); // builds + lays out the rows

        // Lines: SYSTEMS(0), UPDATE(1), logic(2). Click inside the "logic" row (away from its left
        // caret column so it is an enable-toggle, not a collapse).
        var panelRect = EditorChromeLayout.RightPanel(vm.ScreenWidth, vm.ScreenHeight);
        var row = SystemsPanelLayout.LineRect(panelRect, 2);
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(row.Center.X, row.Center.Y);
        input.LeftButtonReleased = true;
        panel.Update(Edit());

        Assert.False(update.IsEnabled("logic"));

        // The gate is a master switch: the child no longer runs in EITHER mode.
        var before = logic.Updates;
        updatePipeline.Update(Play());
        updatePipeline.Update(Edit());
        Assert.Equal(before, logic.Updates);

        // A second click re-enables (and the Freeze policy resumes: runs in Play only).
        input.LeftButtonReleased = true;
        panel.Update(Edit());
        Assert.True(update.IsEnabled("logic"));
        updatePipeline.Update(Play());
        Assert.Equal(before + 1, logic.Updates);
    }

    [Fact]
    public void SystemsPanel_RefusesToDisableItsOwnEntry()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();

        // A registrar whose second update entry IS the panel itself (as the real screens weave it).
        var update = new EditorPipelineRegistrar();
        var draw = new EditorPipelineRegistrar();
        SystemsPanelSystem panel = null!;
        panel = new SystemsPanelSystem(world, vm, font: null, () => (update, draw));
        update.Add("logic", new CountingSystem(), EditTimeBehavior.Freeze);
        update.Add("editor.systemsPanel", panel, EditTimeBehavior.RunNormally);
        update.Build();
        draw.Add("renderMain", new CountingSystem(), EditTimeBehavior.RunNormally);
        draw.Build();
        using var _1 = panel;

        panel.Update(Edit());

        // Click the panel's own row (line 3: SYSTEMS(0), UPDATE(1), logic(2), panel(3)).
        var panelRect = EditorChromeLayout.RightPanel(vm.ScreenWidth, vm.ScreenHeight);
        var row = SystemsPanelLayout.LineRect(panelRect, 3);
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(row.Center.X, row.Center.Y);
        input.LeftButtonReleased = true;
        panel.Update(Edit());

        // Still enabled — disabling itself would leave no UI path back.
        Assert.True(update.IsEnabled("editor.systemsPanel"));
    }

    // ---- Live in BOTH transport states (the panel is how you inspect the running pipeline) ----

    [Fact]
    public void SystemsPanel_WhilePlaying_StaysInteractive()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var (panel, update, _, _, _, _) = MakePanel(world, Vm());
        using var _1 = panel;

        panel.Update(Edit()); // build once so the rows exist

        var panelRect = EditorChromeLayout.RightPanel(vm.ScreenWidth, vm.ScreenHeight);
        var row = SystemsPanelLayout.LineRect(panelRect, 2); // SYSTEMS(0), UPDATE(1), logic(2)
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(row.Center.X, row.Center.Y);
        input.LeftButtonReleased = true;
        panel.Update(Play()); // transport model: toggling while the game runs is the point

        Assert.False(update.IsEnabled("logic"));
    }

    // ---- Scroll: whole clamped lines via the wheel over the panel ----

    [Fact]
    public void SystemsPanel_WheelOverThePanel_ScrollsByClampedLines()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        // A short panel (200px tall window) so few lines fit and scrolling engages.
        var vm = Vm(1600, 200);

        var update = new EditorPipelineRegistrar();
        var draw = new EditorPipelineRegistrar();
        for (var i = 0; i < 12; i++)
            update.Add($"system{i}", new CountingSystem(), EditTimeBehavior.RunNormally);
        update.Build();
        draw.Add("renderMain", new CountingSystem(), EditTimeBehavior.RunNormally);
        draw.Build();
        using var panel = new SystemsPanelSystem(world, vm, font: null, () => (update, draw));

        panel.Update(Edit());
        Assert.Equal(0, panel.ScrollOffset);
        // The whole right column (all three sections, everything expanded) is the scroll extent.
        var totalLines = panel.DisplayedLineCount;

        var panelRect = EditorChromeLayout.RightPanel(vm.ScreenWidth, vm.ScreenHeight);
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(panelRect.Center.X, panelRect.Center.Y);

        // One notch down (wheel -120) = LinesPerNotch lines.
        input.ScrollWheelDelta = -120;
        panel.Update(Edit());
        Assert.Equal(SystemsPanelLayout.LinesPerNotch, panel.ScrollOffset);

        // A huge scroll clamps to MaxScroll.
        input.ScrollWheelDelta = -120 * 50;
        panel.Update(Edit());
        Assert.Equal(SystemsPanelLayout.MaxScroll(totalLines, panelRect), panel.ScrollOffset);

        // Scrolling back up clamps at 0.
        input.ScrollWheelDelta = 120 * 50;
        panel.Update(Edit());
        Assert.Equal(0, panel.ScrollOffset);

        // Wheel outside the panel does nothing.
        input.ScreenPosition = new Vector2(10, 100); // over the game viewport, not the strip
        input.ScrollWheelDelta = -120;
        panel.Update(Edit());
        Assert.Equal(0, panel.ScrollOffset);
    }

    // ---- The tree: groups render above their children, indented, with tri-state checkboxes ----

    /// <summary>Update registrar = a Freeze group with two leaves; draw registrar = one leaf.
    /// Lines: SYSTEMS(0), UPDATE(1), logic(2), a(3), b(4), DRAW(5), renderMain(6).</summary>
    private static (SystemsPanelSystem panel, EditorPipelineRegistrar update,
        CountingSystem a, CountingSystem b)
        MakeTreePanel(World world, ViewportManager vm)
    {
        var update = new EditorPipelineRegistrar();
        var draw = new EditorPipelineRegistrar();
        var a = new CountingSystem();
        var b = new CountingSystem();
        update.AddGroup("logic", EditTimeBehavior.Freeze, g =>
        {
            g.Add("a", a);
            g.Add("b", b);
        });
        update.Build();
        draw.Add("renderMain", new CountingSystem(), EditTimeBehavior.RunNormally);
        draw.Build();

        var panel = new SystemsPanelSystem(world, vm, font: null, () => (update, draw));
        return (panel, update, a, b);
    }

    [Fact]
    public void SystemsPanel_TreeRows_RenderGroupsBeforeChildren_IndentedByDepth()
    {
        using var world = new World();
        MakeCursor(world);
        var vm = Vm();
        var (panel, _, _, _) = MakeTreePanel(world, vm);
        using var _1 = panel;

        panel.Update(Edit());

        // Labels: the group shows its policy tag; children show their LOCAL name (indentation
        // conveys the hierarchy) and no tag (they inherit the group's visual context).
        var labels = LabelTexts(world);
        Assert.Contains("logic [freeze]", labels);
        Assert.Contains("a", labels);
        Assert.Contains("b", labels);
        Assert.DoesNotContain("logic.a", labels);

        // Checkbox indentation: after the reserved left caret column, the group's checkbox sits at
        // the content edge and the children's one indent step to the right.
        var panelRect = EditorChromeLayout.RightPanel(vm.ScreenWidth, vm.ScreenHeight);
        var content = SystemsPanelLayout.ContentArea(panelRect);
        var caret = SystemsPanelLayout.CaretColumn;
        var xs = new List<int>();
        using var boxes = world.GetEntities().With<SimpleButtonComponent>().AsSet();
        foreach (var e in boxes.GetEntities())
        {
            ref readonly var box = ref e.Get<SimpleButtonComponent>();
            if ((int)box.Size.X != SystemsPanelLayout.CheckboxSize) continue; // skip the minus bars
            var pos = e.Get<TransformComponent>().Position;
            if (pos == SystemsPanelLayout.ParkedPosition) continue; // skip scrolled-out/parked
            xs.Add((int)pos.X);
        }
        Assert.Equal(2, xs.Count(x => x == content.X + SystemsPanelLayout.IndentPerDepth + caret)); // a, b
        Assert.Contains(content.X + caret, xs); // the group row (and the flat draw entry)
    }

    [Fact]
    public void SystemsPanel_GroupCheckbox_ShowsTriState_MixedRendersTheMinusBar()
    {
        using var world = new World();
        MakeCursor(world);
        var vm = Vm();
        var (panel, update, _, _) = MakeTreePanel(world, vm);
        using var _1 = panel;

        panel.Update(Edit());

        // All children enabled → the group box is filled and the minus bar is hidden.
        Assert.Equal(EditorChromeBuilder.CheckboxOnFill, GroupCheckboxFill(world));
        Assert.Equal(0, MinusBarFill(world).A);

        // One child off → Mixed: still filled, with the dark minus bar visible (Gmail/Material).
        update.SetEnabled("logic.a", false);
        panel.Update(Edit());
        Assert.Equal(EditorChromeBuilder.CheckboxOnFill, GroupCheckboxFill(world));
        Assert.Equal(EditorChromeBuilder.CheckboxMixedMark, MinusBarFill(world));

        // Every child off → empty box, no bar.
        update.SetEnabled("logic.b", false);
        panel.Update(Edit());
        Assert.Equal(0, GroupCheckboxFill(world).A);
        Assert.Equal(0, MinusBarFill(world).A);
    }

    /// <summary>The group row's checkbox fill (the un-indented checkbox in the UPDATE section).</summary>
    private static Color GroupCheckboxFill(World world)
    {
        using var boxes = world.GetEntities().With<SimpleButtonComponent>().AsSet();
        foreach (var e in boxes.GetEntities())
        {
            ref readonly var box = ref e.Get<SimpleButtonComponent>();
            if ((int)box.Size.X != SystemsPanelLayout.CheckboxSize) continue;
            // The group's box is the topmost checkbox (line 1).
            return box.FillColor;
        }
        throw new InvalidOperationException("no checkbox found");
    }

    /// <summary>The (single) minus-bar mark's fill.</summary>
    private static Color MinusBarFill(World world)
    {
        using var boxes = world.GetEntities().With<SimpleButtonComponent>().AsSet();
        foreach (var e in boxes.GetEntities())
        {
            ref readonly var box = ref e.Get<SimpleButtonComponent>();
            if ((int)box.Size.X == SystemsPanelLayout.MinusBarWidth &&
                (int)box.Size.Y == SystemsPanelLayout.MinusBarHeight)
                return box.FillColor;
        }
        throw new InvalidOperationException("no minus bar found");
    }

    [Fact]
    public void SystemsPanel_GroupClick_GmailSemantics_MixedOrOnTurnsAllOff_OffTurnsAllOn()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var (panel, update, _, _) = MakeTreePanel(world, vm);
        using var _1 = panel;

        panel.Update(Edit());

        // Make the group Mixed first.
        update.SetEnabled("logic.a", false);
        Assert.Equal(PipelineEnabledState.Mixed, update.GetEnabledState("logic"));

        // Click the GROUP row (line 2) at its center (away from the left caret column, so it is the
        // Gmail enable-cascade, not a collapse): Mixed → everything off.
        var panelRect = EditorChromeLayout.RightPanel(vm.ScreenWidth, vm.ScreenHeight);
        var row = SystemsPanelLayout.LineRect(panelRect, 2);
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(row.Center.X, row.Center.Y);
        input.LeftButtonReleased = true;
        panel.Update(Edit());
        Assert.Equal(PipelineEnabledState.Off, update.GetEnabledState("logic"));
        Assert.False(update.IsEnabled("logic.a"));
        Assert.False(update.IsEnabled("logic.b"));

        // Click again: Off → everything on.
        input.LeftButtonReleased = true;
        panel.Update(Edit());
        Assert.Equal(PipelineEnabledState.On, update.GetEnabledState("logic"));

        // Click once more: On → everything off (checked behaves like indeterminate).
        input.LeftButtonReleased = true;
        panel.Update(Edit());
        Assert.Equal(PipelineEnabledState.Off, update.GetEnabledState("logic"));
    }

    [Fact]
    public void SystemsPanel_LeafClickInsideAGroup_TogglesOnlyThatLeaf()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var (panel, update, _, _) = MakeTreePanel(world, vm);
        using var _1 = panel;

        panel.Update(Edit());

        // Click the "logic.a" row (line 3: SYSTEMS(0), UPDATE(1), logic(2), a(3)).
        var panelRect = EditorChromeLayout.RightPanel(vm.ScreenWidth, vm.ScreenHeight);
        var row = SystemsPanelLayout.LineRect(panelRect, 3);
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(row.Center.X, row.Center.Y);
        input.LeftButtonReleased = true;
        panel.Update(Edit());

        Assert.False(update.IsEnabled("logic.a"));
        Assert.True(update.IsEnabled("logic.b"));
        Assert.Equal(PipelineEnabledState.Mixed, update.GetEnabledState("logic"));
    }

    [Fact]
    public void SystemsPanel_RefusesToDisableAnAncestorGroupOfItsOwnEntry()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();

        // The panel's entry nested INSIDE a group: clicking the group must refuse (the cascade
        // would disable the panel itself — no UI path back).
        var update = new EditorPipelineRegistrar();
        var draw = new EditorPipelineRegistrar();
        SystemsPanelSystem panel = null!;
        panel = new SystemsPanelSystem(world, vm, font: null, () => (update, draw));
        var sibling = new CountingSystem();
        update.AddGroup("editor", EditTimeBehavior.RunNormally, g =>
        {
            g.Add("systemsPanel", panel);
            g.Add("cameraNav", sibling);
        });
        update.Build();
        draw.Add("renderMain", new CountingSystem(), EditTimeBehavior.RunNormally);
        draw.Build();
        using var _1 = panel;

        panel.Update(Edit());

        // Click the GROUP row (line 2: SYSTEMS(0), UPDATE(1), editor(2), editor.systemsPanel(3),
        // editor.cameraNav(4)) at its center (the enable region, not the caret).
        var panelRect = EditorChromeLayout.RightPanel(vm.ScreenWidth, vm.ScreenHeight);
        var row = SystemsPanelLayout.LineRect(panelRect, 2);
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(row.Center.X, row.Center.Y);
        input.LeftButtonReleased = true;
        panel.Update(Edit());

        // Refused: nothing under the group was disabled.
        Assert.True(update.IsEnabled("editor.systemsPanel"));
        Assert.True(update.IsEnabled("editor.cameraNav"));

        // The sibling leaf itself stays individually toggleable (line 4).
        row = SystemsPanelLayout.LineRect(panelRect, 4);
        input.ScreenPosition = new Vector2(row.Center.X, row.Center.Y);
        input.LeftButtonReleased = true;
        panel.Update(Edit());
        Assert.False(update.IsEnabled("editor.cameraNav"));
    }

    // ---- Layout math: scrolled-out lines are parked, visible ones sit in the strip ----

    [Fact]
    public void SystemsPanel_ScrolledOutRows_AreParkedOffscreen()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        // Tall enough that the right panel has real estate after the top bar + the raised bottom bar
        // (FW3 grew BottomBarHeight 104 → 168 for the asset cards); still overflows with 12 systems.
        var vm = Vm(1600, 300);

        var update = new EditorPipelineRegistrar();
        var draw = new EditorPipelineRegistrar();
        for (var i = 0; i < 12; i++)
            update.Add($"system{i}", new CountingSystem(), EditTimeBehavior.RunNormally);
        update.Build();
        draw.Add("renderMain", new CountingSystem(), EditTimeBehavior.RunNormally);
        draw.Build();
        using var panel = new SystemsPanelSystem(world, vm, font: null, () => (update, draw));

        panel.Update(Edit());

        var panelRect = EditorChromeLayout.RightPanel(vm.ScreenWidth, vm.ScreenHeight);
        var visible = SystemsPanelLayout.VisibleLineCount(panelRect);
        Assert.True(visible < panel.DisplayedLineCount, "test needs an overflowing panel");

        // The premise: scrolled-out rows are parked far off-screen (GPU-clipped); every visible
        // chrome text entity sits inside the strip, and the overflow is parked — nothing bleeds
        // over the top/bottom bars. (Each row may own >1 text entity — a caret + a label — so we
        // assert the parked/in-strip partition, not a per-line count.)
        var inStrip = 0;
        var parked = 0;
        using var labels = world.GetEntities().With<DynamicTextComponent>().AsSet();
        foreach (var e in labels.GetEntities())
        {
            var pos = e.Get<TransformComponent>().Position;
            if (pos == SystemsPanelLayout.ParkedPosition) parked++;
            else
            {
                Assert.True(panelRect.Contains(new Point((int)pos.X, (int)pos.Y)),
                    "a non-parked chrome text entity must sit inside the right strip");
                inStrip++;
            }
        }
        Assert.True(parked > 0, "an overflowing panel must park the scrolled-out rows");
        Assert.True(inStrip > 0, "some rows must be visible in the strip");
    }
}
