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
        // Section headers + every entry of both pipelines, with the policy tag on Freeze.
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

        // The "logic" entry is line 1 (line 0 = the UPDATE header). Click inside its row.
        var panelRect = EditorChromeLayout.RightPanel(vm.ScreenWidth, vm.ScreenHeight);
        var row = SystemsPanelLayout.LineRect(panelRect, 1);
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

        // Click the panel's own row (line 2: header, logic, panel).
        var panelRect = EditorChromeLayout.RightPanel(vm.ScreenWidth, vm.ScreenHeight);
        var row = SystemsPanelLayout.LineRect(panelRect, 2);
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(row.Center.X, row.Center.Y);
        input.LeftButtonReleased = true;
        panel.Update(Edit());

        // Still enabled — disabling itself would leave no UI path back.
        Assert.True(update.IsEnabled("editor.systemsPanel"));
    }

    // ---- Inert in Play ----

    [Fact]
    public void SystemsPanel_InPlay_IsInert()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var vm = Vm();
        var (panel, update, _, _, _, _) = MakePanel(world, Vm());
        using var _1 = panel;

        panel.Update(Edit()); // build once so the rows exist

        var panelRect = EditorChromeLayout.RightPanel(vm.ScreenWidth, vm.ScreenHeight);
        var row = SystemsPanelLayout.LineRect(panelRect, 1);
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(row.Center.X, row.Center.Y);
        input.LeftButtonReleased = true;
        panel.Update(Play()); // Edit-guarded: the click must not toggle

        Assert.True(update.IsEnabled("logic"));
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
        var totalLines = 2 /* headers */ + update.Entries.Count + draw.Entries.Count;
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

    // ---- Layout math: scrolled-out lines are parked, visible ones sit in the strip ----

    [Fact]
    public void SystemsPanel_ScrolledOutRows_AreParkedOffscreen()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
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

        var panelRect = EditorChromeLayout.RightPanel(vm.ScreenWidth, vm.ScreenHeight);
        var visible = SystemsPanelLayout.VisibleLineCount(panelRect);
        var totalLines = 2 + update.Entries.Count + draw.Entries.Count;
        Assert.True(visible < totalLines, "test needs an overflowing panel");

        // Some labels sit inside the strip; the overflow is parked far off-screen.
        var inStrip = 0;
        var parked = 0;
        using var labels = world.GetEntities().With<DynamicTextComponent>().AsSet();
        foreach (var e in labels.GetEntities())
        {
            var pos = e.Get<TransformComponent>().Position;
            if (pos == SystemsPanelLayout.ParkedPosition) parked++;
            else if (panelRect.Contains(new Point((int)pos.X, (int)pos.Y))) inStrip++;
        }
        Assert.Equal(visible, inStrip);
        Assert.Equal(totalLines - visible, parked);
    }
}
