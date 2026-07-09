#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Input;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Input;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Renderer;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the UX3-E editor shortcut layer: the ONE <see cref="EditorShortcuts"/> table (chord → action,
/// both platform resolutions + the over-match guard), the shared <see cref="ViewportShortcutContext"/>
/// gate, and the <see cref="EditorShortcutSystem"/> driving the SAME shared instances (undo actually
/// undoes, delete removes the selection, Shift+A opens the Add menu at the cursor). The removed bare
/// <c>z</c>/<c>y</c> undo/redo are asserted gone. Pure/logic — systems built with a headless
/// <see cref="ViewportManager"/> and an injected keyboard seam.
/// </summary>
public class EditorShortcutTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    private static KeyboardState None() => new();
    private static KeyboardState Keys_(params Keys[] keys) => new(keys);

    private static ViewportManager Vm() =>
        new(null!, 800, 600) { ScreenWidth = 800, ScreenHeight = 600, DevicePixelRatio = 1f };

    private static Entity MakeCursor(World world, bool outsideViewport = false, Point screen = default)
    {
        var c = world.CreateEntity();
        c.Set(new CursorInputComponent
        {
            OutsideViewport = outsideViewport,
            ScreenPosition = new Vector2(screen.X, screen.Y),
        });
        return c;
    }

    /// <summary>Drives one press edge through the system: prime on an empty keyboard, then the chord.</summary>
    private static void Press(EditorShortcutSystem system, KeyboardState[] kb, GameState state, KeyboardState chord)
    {
        kb[0] = None();
        system.Update(state); // prime / clear any prior edge
        kb[0] = chord;
        system.Update(state); // press edge
    }

    // ═══ The table (pure Match) ═════════════════════════════════════════════════════════════════════

    [Fact]
    public void Table_BindsEachChordToItsAction_WindowsResolution()
    {
        var s = new EditorShortcuts();
        Assert.Equal(EditorShortcutAction.Undo, s.Match(None(), Keys_(Keys.LeftControl, Keys.Z), false));
        Assert.Equal(EditorShortcutAction.Redo, s.Match(None(), Keys_(Keys.LeftControl, Keys.LeftShift, Keys.Z), false));
        Assert.Equal(EditorShortcutAction.AddMenu, s.Match(None(), Keys_(Keys.LeftShift, Keys.A), false));
        Assert.Equal(EditorShortcutAction.Delete, s.Match(None(), Keys_(Keys.Delete), false));
        Assert.Equal(EditorShortcutAction.FrameScene, s.Match(None(), Keys_(Keys.Home), false));
    }

    [Fact]
    public void Table_CmdShiftZ_ResolvesToRedo_NeverUndo_PreMortem3()
    {
        var s = new EditorShortcuts();
        // Cmd+Shift+Z must resolve to exactly Redo — the Cmd+Z Undo binding must NOT also match.
        Assert.Equal(EditorShortcutAction.Redo, s.Match(None(), Keys_(Keys.LeftControl, Keys.LeftShift, Keys.Z), false));
    }

    [Fact]
    public void Table_MacResolution_MetaFiresCommand_CtrlDoesNot()
    {
        var s = new EditorShortcuts();
        // macOS: ⌘ (Meta / the GUI key) is PlatformCommand.
        Assert.Equal(EditorShortcutAction.Undo, s.Match(None(), Keys_(Keys.LeftWindows, Keys.Z), commandIsMeta: true));
        // On macOS a raw Ctrl+Z is NOT the command chord.
        Assert.Null(s.Match(None(), Keys_(Keys.LeftControl, Keys.Z), commandIsMeta: true));
    }

    [Fact]
    public void Table_BareZ_AndBareY_HaveNoBinding_TheRemovedUndoRedo()
    {
        var s = new EditorShortcuts();
        Assert.Null(s.Match(None(), Keys_(Keys.Z), false)); // bare z no longer undoes
        Assert.Null(s.Match(None(), Keys_(Keys.Y), false)); // bare y no longer redoes
    }

    // ═══ The context gate (pure) ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Context_AllowsEditing_OnlyOverViewport_NoModal_WhilePaused()
    {
        Assert.True(new ViewportShortcutContext
            { CursorOverViewport = true, Editing = true }.AllowsEditing);

        Assert.False(new ViewportShortcutContext
            { CursorOverViewport = false, Editing = true }.AllowsEditing); // over a panel
        Assert.False(new ViewportShortcutContext
            { CursorOverViewport = true, Editing = true, DialogOpen = true }.AllowsEditing);
        Assert.False(new ViewportShortcutContext
            { CursorOverViewport = true, Editing = true, MenuOpen = true }.AllowsEditing);
        Assert.False(new ViewportShortcutContext
            { CursorOverViewport = true, Editing = false }.AllowsEditing); // Playing
    }

    // ═══ The system: the gate composed with real cursor/dialog/menu/run-mode ════════════════════════

    private static (EditorShortcutSystem system, KeyboardState[] kb, List<EditorShortcutAction> fired)
        SpySystem(World world, bool dialogOpen = false, bool menuOpen = false)
    {
        var kb = new[] { None() };
        var fired = new List<EditorShortcutAction>();
        var system = new EditorShortcutSystem(
            world, new EditorShortcuts(), (a, _) => fired.Add(a),
            dialogOpen: () => dialogOpen, menuOpen: () => menuOpen,
            commandIsMeta: false, getKeyboardState: () => kb[0]);
        return (system, kb, fired);
    }

    [Fact]
    public void System_Fires_OverViewport_Paused()
    {
        using var world = new World();
        MakeCursor(world, outsideViewport: false);
        var (system, kb, fired) = SpySystem(world);

        Press(system, kb, Edit(), Keys_(Keys.LeftControl, Keys.Z));

        Assert.Equal(new[] { EditorShortcutAction.Undo }, fired);
    }

    [Fact]
    public void System_DoesNotFire_OverAPanel()
    {
        using var world = new World();
        MakeCursor(world, outsideViewport: true); // cursor over the chrome/panel margins
        var (system, kb, fired) = SpySystem(world);

        Press(system, kb, Edit(), Keys_(Keys.LeftControl, Keys.Z));

        Assert.Empty(fired);
    }

    [Fact]
    public void System_DoesNotFire_WhileDialogOrMenuOpen()
    {
        using var world = new World();
        MakeCursor(world, outsideViewport: false);
        var (dlgSys, dlgKb, dlgFired) = SpySystem(world, dialogOpen: true);
        Press(dlgSys, dlgKb, Edit(), Keys_(Keys.LeftControl, Keys.Z));
        Assert.Empty(dlgFired);

        var (menuSys, menuKb, menuFired) = SpySystem(world, menuOpen: true);
        Press(menuSys, menuKb, Edit(), Keys_(Keys.LeftControl, Keys.Z));
        Assert.Empty(menuFired);
    }

    [Fact]
    public void System_DoesNotFire_WhilePlaying()
    {
        using var world = new World();
        MakeCursor(world, outsideViewport: false);
        var (system, kb, fired) = SpySystem(world);

        Press(system, kb, Play(), Keys_(Keys.LeftControl, Keys.Z));

        Assert.Empty(fired);
    }

    [Fact]
    public void System_BareZ_DoesNotUndo()
    {
        using var world = new World();
        MakeCursor(world, outsideViewport: false);
        var (system, kb, fired) = SpySystem(world);

        Press(system, kb, Edit(), Keys_(Keys.Z)); // bare z, over the viewport, Paused

        Assert.Empty(fired); // the removed bare-key undo is gone
    }

    // ═══ Drives the SAME shared instances (never a second path) ═════════════════════════════════════

    private sealed class FlagCommand : IEditorCommand
    {
        public bool Applied;
        public void Apply(World world) => Applied = true;
        public void Revert(World world) => Applied = false;
    }

    [Fact]
    public void Shortcut_CmdZ_DrivesTheSharedHistory_UndoActuallyUndoes()
    {
        using var world = new World();
        MakeCursor(world, outsideViewport: false);
        var history = new EditorHistory(world);
        var cmd = new FlagCommand();
        history.Push(cmd); // applied live (Applied == true), one undo entry
        Assert.True(cmd.Applied);

        var kb = new[] { None() };
        var system = new EditorShortcutSystem(
            world, new EditorShortcuts(),
            (a, _) => { if (a == EditorShortcutAction.Undo) history.Undo(); },
            dialogOpen: () => false, menuOpen: () => false,
            commandIsMeta: false, getKeyboardState: () => kb[0]);

        Press(system, kb, Edit(), Keys_(Keys.LeftControl, Keys.Z));

        Assert.False(cmd.Applied);   // the shared history reverted the command — undo actually undid
        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void Shortcut_Delete_DrivesTheSharedCommandSystem_RemovesTheSelection()
    {
        using var world = new World();
        MakeCursor(world, outsideViewport: false);

        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        var history = new EditorHistory(world);
        using var commands = new EditorCommandSystem(world, history, new SceneSerializer(registry));

        var target = world.CreateEntity();
        target.Set(new TransformComponent(new Vector2(5, 5)));
        target.Set(new EntityInfoComponent("Prop"));
        target.Set(new SceneObjectComponent());
        target.Set(new SelectedComponent());

        var kb = new[] { None() };
        var system = new EditorShortcutSystem(
            world, new EditorShortcuts(),
            (a, s) => { if (a == EditorShortcutAction.Delete) commands.DeleteSelection(s); },
            dialogOpen: () => false, menuOpen: () => false,
            commandIsMeta: false, getKeyboardState: () => kb[0]);

        Press(system, kb, Edit(), Keys_(Keys.Delete));

        Assert.False(target.IsAlive);          // the snapshotting delete disposed the selection
        Assert.Equal(1, history.Count);        // one undoable delete on the shared history
    }

    [Fact]
    public void Shortcut_ShiftA_OpensTheAddMenu_AtTheCursor()
    {
        using var world = new World();
        var cursor = MakeCursor(world, outsideViewport: false, screen: new Point(300, 200));
        using var menu = new EditorContextMenuSystem(world, Vm(), font: null, dispatch: (_, _) => { });

        var kb = new[] { None() };
        var system = new EditorShortcutSystem(
            world, new EditorShortcuts(),
            (a, _) =>
            {
                if (a != EditorShortcutAction.AddMenu) return;
                ref readonly var input = ref cursor.Get<CursorInputComponent>();
                menu.OpenAt(EditorContextMenuModel.EntitiesPanelMenu(hasRowEntity: false),
                    new Point((int)input.ScreenPosition.X, (int)input.ScreenPosition.Y));
            },
            dialogOpen: () => false, menuOpen: () => menu.IsOpen,
            commandIsMeta: false, getKeyboardState: () => kb[0]);

        Press(system, kb, Edit(), Keys_(Keys.LeftShift, Keys.A));

        Assert.True(menu.IsOpen);
        Assert.Contains(menu.Items, i => i.Path == EditorContextMenuModel.AddEmptyPath && i.Label == "Add Empty Entity");
    }
}
