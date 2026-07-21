#nullable enable
using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.UI;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.System.Cursor;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the editor's modal dialogs (UX-D): the pure name-field model + sanitizer, the <b>three-action
/// Save dialog</b> (Save Scene / Save Project / Save Backup As…) — each action fires its game-agnostic
/// callback and closes; the backup action arms a name field, confirm passes the sanitized id and closes,
/// an empty name keeps it open; Enter picks the focused/default action (Save Scene, or the backup while
/// its field is armed); Escape/Cancel close; the modal capture consumes the cursor so a viewport click
/// never leaks — and the UX-C confirm-on-switch modal (still a live mode).
///
/// <para>All in-process, no GraphicsDevice: <see cref="EditorDialogSystem"/> is built with a null font
/// (layout-only) + injected action callbacks + an injected keyboard, exactly the seams that let the whole
/// flow run headless.</para>
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class EditorDialogTests
{
    // ─── pure field model + sanitizer ────────────────────────────────────────────────────────────

    [Fact]
    public void EditorTextField_AppendBackspaceSetClear()
    {
        var field = new EditorTextField();
        Assert.True(field.IsEmpty);
        field.Append('a'); field.Append('b'); field.Append('c');
        Assert.Equal("abc", field.Value);
        field.Backspace();
        Assert.Equal("ab", field.Value);
        field.Append("cd");
        Assert.Equal("abcd", field.Value);
        field.Set("island");
        Assert.Equal("island", field.Value);
        field.Clear();
        Assert.True(field.IsEmpty);
        field.Backspace(); // no-op when empty
        Assert.Equal(string.Empty, field.Value);
    }

    [Theory]
    [InlineData("island", "island")]
    [InlineData("My Level 2", "MyLevel2")]           // spaces stripped
    [InlineData("world/level.mdscene", "worldlevelmdscene")] // separators + dots stripped
    [InlineData("  --keep_me--  ", "keep_me")]        // trimmed of edge -/_
    [InlineData("!@#$%", "")]                          // nothing survives → empty
    [InlineData(null, "")]
    public void EditorTextField_Sanitize(string? raw, string expected) =>
        Assert.Equal(expected, EditorTextField.Sanitize(raw));

    // ─── the three-action Save dialog ────────────────────────────────────────────────────────────

    [Fact]
    public void SaveDialog_Opens_WithThreeActions_BackupDisarmed_AndPrefilledBackupName()
    {
        var dialog = NewDialog();
        dialog.OpenSave("island");

        Assert.True(dialog.IsOpen);
        Assert.Equal(EditorDialogMode.Save, dialog.Mode);
        Assert.False(dialog.IsBackupArmed);                 // the backup field starts hidden
        Assert.Equal("island-backup", dialog.NameValue);    // prefilled <sceneId>-backup, sanitized
    }

    [Fact]
    public void SaveDialog_SaveScene_InvokesTheSaveSceneCallback_AndCloses()
    {
        int scene = 0, project = 0, backup = 0;
        var dialog = NewDialog(onSaveScene: _ => scene++, onSaveProject: _ => project++, onSaveBackup: (_, _) => backup++);

        dialog.OpenSave("island");
        dialog.SaveScene(EditState());

        Assert.Equal(1, scene);
        Assert.Equal(0, project);
        Assert.Equal(0, backup);
        Assert.False(dialog.IsOpen);
    }

    [Fact]
    public void SaveDialog_SaveProject_InvokesTheSaveProjectCallback_AndCloses()
    {
        int scene = 0, project = 0;
        var dialog = NewDialog(onSaveScene: _ => scene++, onSaveProject: _ => project++);

        dialog.OpenSave("island");
        dialog.SaveProject(EditState());

        Assert.Equal(0, scene);
        Assert.Equal(1, project);
        Assert.False(dialog.IsOpen);
    }

    [Fact]
    public void SaveDialog_Backup_ArmRevealsField_ThenConfirmPassesSanitizedName_AndCloses()
    {
        string? backupName = null;
        var dialog = NewDialog(onSaveBackup: (name, _) => backupName = name);

        dialog.OpenSave("island");
        Assert.False(dialog.IsBackupArmed);

        dialog.ArmBackup();                    // clicking Save Backup As… reveals the field
        Assert.True(dialog.IsBackupArmed);
        Assert.Equal("island-backup", dialog.NameValue);

        dialog.SetName("My Snapshot 3!");      // user retypes
        dialog.ConfirmBackup(EditState());

        Assert.Equal("MySnapshot3", backupName); // sanitized on confirm
        Assert.False(dialog.IsOpen);
    }

    [Fact]
    public void SaveDialog_Backup_EmptyNameAfterSanitize_KeepsDialogOpen_AndDoesNotWrite()
    {
        var backups = 0;
        var dialog = NewDialog(onSaveBackup: (_, _) => backups++);

        dialog.OpenSave("island");
        dialog.ArmBackup();
        dialog.SetName("!!!///"); // sanitizes to empty
        dialog.ConfirmBackup(EditState());

        Assert.Equal(0, backups);
        Assert.True(dialog.IsOpen); // stays open so the user can retype
    }

    [Fact]
    public void SaveDialog_BackupOneShot_ArmsSetsAndConfirms_InOneCall()
    {
        string? backupName = null;
        var dialog = NewDialog(onSaveBackup: (name, _) => backupName = name);

        dialog.OpenSave("island");
        dialog.Backup("cove-2", EditState()); // the dialog:backup <name> op

        Assert.Equal("cove-2", backupName);
        Assert.False(dialog.IsOpen);
    }

    [Fact]
    public void SaveDialog_Cancel_InvokesNothing_AndCloses()
    {
        int scene = 0, project = 0, backup = 0;
        var dialog = NewDialog(onSaveScene: _ => scene++, onSaveProject: _ => project++, onSaveBackup: (_, _) => backup++);

        dialog.OpenSave("island");
        dialog.ArmBackup();
        dialog.SetName("changed");
        dialog.Cancel();

        Assert.Equal(0, scene + project + backup);
        Assert.False(dialog.IsOpen);
    }

    [Fact]
    public void SaveDialog_Escape_Closes()
    {
        var scene = 0;
        var kb = new[] { new KeyboardState() };
        var dialog = NewDialog(onSaveScene: _ => scene++, getKeyboardState: () => kb[0]);

        dialog.OpenSave("island");
        Assert.True(dialog.IsOpen);

        kb[0] = new KeyboardState(Keys.Escape);
        dialog.Update(EditState());

        Assert.False(dialog.IsOpen);
        Assert.Equal(0, scene);
    }

    [Fact]
    public void SaveDialog_Enter_PicksSaveScene_WhenBackupDisarmed()
    {
        int scene = 0, backup = 0;
        var kb = new[] { new KeyboardState() };
        var dialog = NewDialog(onSaveScene: _ => scene++, onSaveBackup: (_, _) => backup++, getKeyboardState: () => kb[0]);

        dialog.OpenSave("island");
        kb[0] = new KeyboardState(Keys.Enter);
        dialog.Update(EditState());

        Assert.Equal(1, scene);   // Enter = the focused/default action = Save Scene
        Assert.Equal(0, backup);
        Assert.False(dialog.IsOpen);
    }

    [Fact]
    public void SaveDialog_Enter_ConfirmsBackup_WhenBackupArmed()
    {
        int scene = 0;
        string? backupName = null;
        var kb = new[] { new KeyboardState() };
        var dialog = NewDialog(onSaveScene: _ => scene++, onSaveBackup: (name, _) => backupName = name, getKeyboardState: () => kb[0]);

        dialog.OpenSave("island");
        dialog.ArmBackup();
        kb[0] = new KeyboardState(Keys.Enter);
        dialog.Update(EditState());

        Assert.Equal(0, scene);              // Enter now confirms the backup, not Save Scene
        Assert.Equal("island-backup", backupName);
        Assert.False(dialog.IsOpen);
    }

    [Fact]
    public void SaveDialog_BackupField_KeyboardTypingBackspace()
    {
        string? backupName = null;
        var kb = new[] { new KeyboardState() };
        var dialog = NewDialog(onSaveBackup: (name, _) => backupName = name, getKeyboardState: () => kb[0]);

        dialog.OpenSave("island");
        dialog.ArmBackup();
        dialog.SetName(string.Empty); // start empty to type fresh

        Type(dialog, kb, Keys.M);
        Type(dialog, kb, Keys.A);
        Type(dialog, kb, Keys.P);
        Assert.Equal("map", dialog.NameValue);

        Type(dialog, kb, Keys.Back);
        Assert.Equal("ma", dialog.NameValue);

        Type(dialog, kb, Keys.D2); // a digit is a valid file char
        Assert.Equal("ma2", dialog.NameValue);

        Type(dialog, kb, Keys.Enter); // confirm the backup
        Assert.Equal("ma2", backupName);
        Assert.False(dialog.IsOpen);
    }

    [Fact]
    public void SaveDialog_TypingIsIgnored_WhenBackupDisarmed()
    {
        var kb = new[] { new KeyboardState() };
        var dialog = NewDialog(getKeyboardState: () => kb[0]);

        dialog.OpenSave("island");                 // backup NOT armed
        Type(dialog, kb, Keys.M);
        Type(dialog, kb, Keys.A);

        Assert.Equal("island-backup", dialog.NameValue); // unchanged — the field is only edited when armed
    }

    // ─── modality ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OpenDialog_ConsumesTheCursor_SoAViewportClickDoesNotSelect()
    {
        using var world = new World();
        var gizmoState = world.CreateEntity();
        gizmoState.Set(GizmoStateComponent.Default); // SelectTransform mode
        var sprite = MakeSelectableSprite(world);
        var cursor = MakeCursor(world);

        var dialog = NewDialog(world: world);
        using var selection = new SelectionSystem(world);
        var state = EditState();

        // A viewport press over the sprite WHILE the dialog is open.
        dialog.OpenSave("island");
        PressAt(cursor, world: new Vector2(2, 2), screen: new Vector2(2, 2));
        dialog.Update(state);      // consumes the pointer edges (modal)
        selection.Update(state);   // must see no press → no selection
        Assert.False(sprite.Has<SelectedComponent>());

        // After the dialog closes the same press selects normally (modality released).
        dialog.Cancel();
        PressAt(cursor, world: new Vector2(2, 2), screen: new Vector2(2, 2));
        dialog.Update(state);      // closed → parks, does NOT consume
        selection.Update(state);
        Assert.True(sprite.Has<SelectedComponent>());
    }

    /// <summary>
    /// Regression (EF1): drives a real press→release through the ACTUAL woven update order
    /// (<see cref="CursorInputSystem"/> then the dialog, entry <c>editor.dialog</c>) with a SCRIPTED
    /// hardware mouse over the Save Scene action row — the dialog acts on the release edge, and its modal
    /// consume clears the cursor's LeftButton LEVEL every frame; the release edge fires only because
    /// <see cref="CursorInputSystem"/> derives it from its own previous hardware state, not that level.
    /// </summary>
    [Fact]
    public void SaveDialog_ClickSaveSceneThroughRealCursorPipeline_InvokesOnRelease()
    {
        using var world = new World();
        var vm = NewViewport(); // 800×600, DPR 1 → ScreenPosition == the raw mouse position
        MakeCursor(world);

        var scene = 0;
        var dialog = new EditorDialogSystem(
            world, vm, font: null,
            onSaveScene: _ => scene++, onSaveProject: _ => { }, onSaveBackup: (_, _) => { });

        // The scripted hardware mouse: fixed over the Save Scene action row (index 0); only the button
        // state flips per frame, exactly as a real click would (up → down → up over the same pixel).
        var panel = EditorDialogLayout.SavePanel(800, 600, backupActive: false, scale: 1f);
        var over = EditorDialogLayout.SaveAction(panel, 0, 1f).Center;
        var down = false;
        var cursorInput = new CursorInputSystem(world, vm)
        {
            MouseStateProvider = () => new MouseState(
                over.X, over.Y, 0,
                down ? ButtonState.Pressed : ButtonState.Released,
                ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released),
        };

        using var pipeline = new SequentialSystem<GameState>(cursorInput, dialog);
        var state = EditState();

        dialog.OpenSave("island");

        down = false; pipeline.Update(state); // frame 1: button up over the action
        down = true;  pipeline.Update(state); // frame 2: press — the dialog acts on release, so no-op
        Assert.True(dialog.IsOpen);
        Assert.Equal(0, scene);

        down = false; pipeline.Update(state); // frame 3: release over the action → Save Scene

        Assert.Equal(1, scene);          // the click was received
        Assert.False(dialog.IsOpen);     // and the dialog closed
    }

    // ─── confirm-on-switch modal (UX-C — still live) ─────────────────────────────────────────────

    [Fact]
    public void ConfirmSwitch_Confirm_RunsSaveAndSwitch_NotDiscard_AndCloses()
    {
        int saved = 0, discarded = 0;
        var dialog = NewDialog();
        dialog.OpenConfirmSwitch("island", _ => saved++, _ => discarded++);
        Assert.Equal(EditorDialogMode.ConfirmSwitch, dialog.Mode);

        dialog.Confirm(EditState()); // the primary action = Save & Switch
        Assert.Equal(1, saved);
        Assert.Equal(0, discarded);
        Assert.False(dialog.IsOpen);
    }

    [Fact]
    public void ConfirmSwitch_Discard_SwitchesWithoutSaving_AndCloses()
    {
        int saved = 0, discarded = 0;
        var dialog = NewDialog();
        dialog.OpenConfirmSwitch("island", _ => saved++, _ => discarded++);

        dialog.Discard(EditState());
        Assert.Equal(0, saved);
        Assert.Equal(1, discarded);
        Assert.False(dialog.IsOpen);
    }

    [Fact]
    public void ConfirmSwitch_Cancel_DoesNeither_AndCloses()
    {
        int saved = 0, discarded = 0;
        var dialog = NewDialog();
        dialog.OpenConfirmSwitch("island", _ => saved++, _ => discarded++);

        dialog.Cancel();
        Assert.Equal(0, saved);
        Assert.Equal(0, discarded);
        Assert.False(dialog.IsOpen);
    }

    [Fact]
    public void ConfirmSwitch_EnterConfirms_EscapeCancels()
    {
        int saved = 0, discarded = 0;
        var kb = new[] { new KeyboardState() };
        var dialog = NewDialog(getKeyboardState: () => kb[0]);

        dialog.OpenConfirmSwitch("island", _ => saved++, _ => discarded++);
        kb[0] = new KeyboardState(Keys.Enter);
        dialog.Update(EditState());
        Assert.Equal(1, saved);
        Assert.False(dialog.IsOpen);

        kb[0] = new KeyboardState();
        dialog.OpenConfirmSwitch("cove", _ => saved++, _ => discarded++);
        kb[0] = new KeyboardState(Keys.Escape);
        dialog.Update(EditState());
        Assert.Equal(1, saved);      // no extra save
        Assert.Equal(0, discarded);  // Escape is Cancel, not Discard
        Assert.False(dialog.IsOpen);
    }

    /// <summary>The confirm-switch modal captures the pointer (like Save) and its three buttons hit-test
    /// through the REAL woven order (CursorInputSystem → dialog): a press→release over the Discard button
    /// routes to Discard &amp; Switch on the release edge.</summary>
    [Fact]
    public void ConfirmSwitch_ClickDiscardThroughRealCursorPipeline_DiscardsOnRelease()
    {
        using var world = new World();
        var vm = NewViewport();
        MakeCursor(world);

        int saved = 0, discarded = 0;
        var dialog = new EditorDialogSystem(
            world, vm, font: null,
            onSaveScene: _ => { }, onSaveProject: _ => { }, onSaveBackup: (_, _) => { });
        dialog.OpenConfirmSwitch("island", _ => saved++, _ => discarded++);

        var panel = EditorDialogLayout.ConfirmPanel(800, 600, 1f);
        var over = EditorDialogLayout.ConfirmButtons(panel, 1f)[1].Center; // [1] = Discard & Switch
        var down = false;
        var cursorInput = new CursorInputSystem(world, vm)
        {
            MouseStateProvider = () => new MouseState(
                over.X, over.Y, 0,
                down ? ButtonState.Pressed : ButtonState.Released,
                ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released),
        };
        using var pipeline = new SequentialSystem<GameState>(cursorInput, dialog);
        var state = EditState();

        down = false; pipeline.Update(state);
        down = true;  pipeline.Update(state);
        Assert.True(dialog.IsOpen);   // acts on release, not press
        down = false; pipeline.Update(state);

        Assert.Equal(1, discarded);
        Assert.Equal(0, saved);
        Assert.False(dialog.IsOpen);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static void Type(EditorDialogSystem dialog, KeyboardState[] kb, Keys key)
    {
        kb[0] = new KeyboardState(key);
        dialog.Update(EditState());
        kb[0] = new KeyboardState(); // release so the next press is a fresh edge
    }

    private static void PressAt(Entity cursor, Vector2 world, Vector2 screen)
    {
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.WorldPosition = world;
        input.VirtualPosition = world;
        input.ScreenPosition = screen;
        input.OutsideViewport = false;
        input.LeftButton = true;
        input.LeftButtonPressed = true;
        input.LeftButtonReleased = false;
        cursor.NotifyChanged<CursorInputComponent>();
    }

    private static EditorDialogSystem NewDialog(
        World? world = null,
        Action<GameState>? onSaveScene = null,
        Action<GameState>? onSaveProject = null,
        Action<string, GameState>? onSaveBackup = null,
        Func<KeyboardState>? getKeyboardState = null)
    {
        world ??= new World();
        var vm = NewViewport();
        return new EditorDialogSystem(
            world, vm, font: null,
            onSaveScene: onSaveScene ?? (_ => { }),
            onSaveProject: onSaveProject ?? (_ => { }),
            onSaveBackup: onSaveBackup ?? ((_, _) => { }),
            getKeyboardState: getKeyboardState ?? (() => new KeyboardState()));
    }

    private static ViewportManager NewViewport() =>
        new(null!, 800, 600) { ScreenWidth = 800, ScreenHeight = 600, DevicePixelRatio = 1f };

    private static GameState EditState() => new(new GameTime()) { RunMode = RunMode.Edit };

    private static Entity MakeSelectableSprite(World world)
    {
        var e = world.CreateEntity();
        e.Set(new EntityInfoComponent("Prop", "Prop"));
        e.Set(new TransformComponent(Vector2.Zero));
        e.Set(new SpriteInfoComponent
        {
            Source = new Rectangle(0, 0, 10, 10),
            Size = new Vector2(10, 10),
            Origin = Vector2.Zero,
            AssetKey = "Atlas/TX Prop",
            Target = RenderTargetID.Main,
        });
        e.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main, LayerDepth = 0.5f });
        e.Set(new VisibleComponent());
        e.Set(new SceneObjectComponent());
        return e;
    }

    private static Entity MakeCursor(World world)
    {
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent());
        return cursor;
    }
}
