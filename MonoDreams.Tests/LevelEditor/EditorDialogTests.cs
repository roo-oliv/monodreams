#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.UI;
using MonoDreams.Platform;
using MonoDreams.Renderer;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the editor's Save / Load dialogs (FW2): the pure name-field model + sanitizer, the Save
/// flow (open → name → confirm writes the sanitized id through the guarded save; Cancel writes
/// nothing; the FW1 gate still blocks Playing / unresolved-root), the Load flow (lists the scenes,
/// selecting one fires the load; unresolved root shows an actionable message and never crashes), the
/// modal capture (an open dialog consumes the cursor so a viewport click never selects; Escape
/// closes), and keyboard field editing.
///
/// <para>All in-process, no GraphicsDevice: <see cref="EditorDialogSystem"/> is built with a null
/// font (layout-only) + injected save/load callbacks + an injected scene listing + an injected
/// keyboard, exactly the seams that let the whole flow run headless.</para>
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

    // ─── Save flow ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SaveDialog_OpenNameConfirm_FiresSanitizedIdAndCloses()
    {
        string? savedId = null;
        var dialog = NewDialog(onSaveConfirmed: (id, _) => savedId = id);

        dialog.OpenSave("island");
        Assert.Equal(EditorDialogMode.Save, dialog.Mode);
        Assert.Equal("island", dialog.NameValue); // prefilled with the current scene id

        dialog.SetName("My New Level!");
        dialog.Confirm(EditState());

        Assert.Equal("MyNewLevel", savedId); // sanitized on confirm
        Assert.False(dialog.IsOpen);          // confirm closes
    }

    [Fact]
    public void SaveDialog_Cancel_WritesNothingAndCloses()
    {
        var confirmed = 0;
        var dialog = NewDialog(onSaveConfirmed: (_, _) => confirmed++);

        dialog.OpenSave("island");
        dialog.SetName("changed");
        dialog.Cancel();

        Assert.Equal(0, confirmed);
        Assert.False(dialog.IsOpen);
    }

    [Fact]
    public void SaveDialog_EmptyAfterSanitize_KeepsDialogOpenAndDoesNotSave()
    {
        var confirmed = 0;
        var dialog = NewDialog(onSaveConfirmed: (_, _) => confirmed++);

        dialog.OpenSave("island");
        dialog.SetName("!!!///"); // sanitizes to empty
        dialog.Confirm(EditState());

        Assert.Equal(0, confirmed);
        Assert.True(dialog.IsOpen); // stays open so the user can retype
    }

    /// <summary>
    /// The Save dialog's confirm routes through the SAME guarded save the toolbar used
    /// (<c>EditorOverlay.IsSaveBlocked</c> → <see cref="SceneWriter"/>): a confirm while Playing or
    /// with no project root writes nothing (the FW1 gate); a confirm while Paused with a resolved
    /// project writes <c>&lt;LevelsPath&gt;/&lt;id&gt;.mdscene</c>. This mirrors the overlay's
    /// <c>onSaveConfirmed</c> wiring exactly.
    /// </summary>
    [Fact]
    public void SaveDialog_ConfirmRespectsTheSaveGuard_AndWritesToLevelsPath()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            using var world = new World();
            var serializer = NewSerializer();
            var ctx = ResolvedContext();
            MakeSaveRoot(world);

            // The overlay's onSaveConfirmed shape: guard, then write to LevelsPath/<id>.mdscene.
            void OnSaveConfirmed(string id, GameState state)
            {
                if (EditorOverlay.IsSaveBlocked(state, ctx)) return;
                new SceneWriter(serializer).Save(world, EditorOverlay.SceneFilePath(ctx, id), camera: null, layers: null);
            }

            var dialog = NewDialog(onSaveConfirmed: OnSaveConfirmed);

            // Playing → blocked (nothing written), dialog still closes on confirm attempt? No — confirm
            // fires the callback which no-ops on the guard; the dialog closes regardless of the write.
            dialog.OpenSave("island");
            dialog.SetName("arena");
            dialog.Confirm(PlayState());
            Assert.Equal(0, fake.WriteCount);

            // Unresolved project → blocked.
            void OnSaveUnresolved(string id, GameState state)
            {
                if (EditorOverlay.IsSaveBlocked(state, EditorProjectContext.Unresolved)) return;
                new SceneWriter(serializer).Save(world, EditorOverlay.SceneFilePath(EditorProjectContext.Unresolved, id), camera: null, layers: null);
            }
            var dialog2 = NewDialog(onSaveConfirmed: OnSaveUnresolved);
            dialog2.OpenSave("island");
            dialog2.SetName("arena");
            dialog2.Confirm(EditState());
            Assert.Equal(0, fake.WriteCount);

            // Paused + resolved → writes to LevelsPath/arena.mdscene.
            dialog.OpenSave("island");
            dialog.SetName("arena");
            dialog.Confirm(EditState());
            Assert.Equal(1, fake.WriteCount);
            var expectedPath = EditorOverlay.SceneFilePath(ctx, "arena");
            Assert.NotNull(expectedPath);
            Assert.True(fake.Files.ContainsKey(expectedPath!));
        });
    }

    [Fact]
    public void SaveDialog_KeyboardTypingBackspaceEnter()
    {
        string? savedId = null;
        var kb = new[] { new KeyboardState() };
        var dialog = NewDialog(onSaveConfirmed: (id, _) => savedId = id, getKeyboardState: () => kb[0]);

        dialog.OpenSave(string.Empty); // start empty

        Type(dialog, kb, Keys.M);
        Type(dialog, kb, Keys.A);
        Type(dialog, kb, Keys.P);
        Assert.Equal("map", dialog.NameValue);

        Type(dialog, kb, Keys.Back);
        Assert.Equal("ma", dialog.NameValue);

        Type(dialog, kb, Keys.D2); // a digit is a valid file char
        Assert.Equal("ma2", dialog.NameValue);

        Type(dialog, kb, Keys.Enter); // confirm
        Assert.Equal("ma2", savedId);
        Assert.False(dialog.IsOpen);
    }

    [Fact]
    public void SaveDialog_EscapeCloses()
    {
        var confirmed = 0;
        var kb = new[] { new KeyboardState() };
        var dialog = NewDialog(onSaveConfirmed: (_, _) => confirmed++, getKeyboardState: () => kb[0]);

        dialog.OpenSave("island");
        Assert.True(dialog.IsOpen);

        kb[0] = new KeyboardState(Keys.Escape);
        dialog.Update(EditState());

        Assert.False(dialog.IsOpen);
        Assert.Equal(0, confirmed);
    }

    // ─── Load flow ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LoadDialog_ListsScenes_AndSelectingOneFiresLoad()
    {
        string? loadedId = null;
        var listing = new SceneListing(true, new[] { "arena", "island", "menu" }, null);
        var dialog = NewDialog(onLoadSelected: (id, _) => loadedId = id, listing: listing);

        dialog.OpenLoad();
        Assert.Equal(EditorDialogMode.Load, dialog.Mode);
        Assert.Equal(new[] { "arena", "island", "menu" }, dialog.ListedSceneIds);

        dialog.SelectLoad("island", EditState());
        Assert.Equal("island", loadedId);
        Assert.False(dialog.IsOpen);
    }

    [Fact]
    public void LoadDialog_UnresolvedRoot_ShowsMessageAndDoesNotCrash()
    {
        var listing = new SceneListing(false, Array.Empty<string>(),
            "No project root resolved. Set MONODREAMS_PROJECT_ROOT.");
        var dialog = NewDialog(listing: listing);

        dialog.OpenLoad();
        Assert.Equal(EditorDialogMode.Load, dialog.Mode);
        Assert.Empty(dialog.ListedSceneIds);

        // Rendering the message path (layout with the unresolved listing) must not throw.
        dialog.Update(EditState());
        Assert.True(dialog.IsOpen);
    }

    // ─── modality ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OpenDialog_ConsumesTheCursor_SoAViewportClickDoesNotSelect()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600) { Zoom = 1f, Position = Vector2.Zero };
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
        Action<string, GameState>? onSaveConfirmed = null,
        Action<string, GameState>? onLoadSelected = null,
        SceneListing? listing = null,
        Func<KeyboardState>? getKeyboardState = null)
    {
        world ??= new World();
        var vm = NewViewport();
        var list = listing ?? new SceneListing(true, Array.Empty<string>(), null);
        return new EditorDialogSystem(
            world, vm, font: null,
            onSaveConfirmed: onSaveConfirmed ?? ((_, _) => { }),
            onLoadSelected: onLoadSelected ?? ((_, _) => { }),
            listScenes: () => list,
            getKeyboardState: getKeyboardState ?? (() => new KeyboardState()));
    }

    private static ViewportManager NewViewport() =>
        new(null!, 800, 600) { ScreenWidth = 800, ScreenHeight = 600, DevicePixelRatio = 1f };

    private static GameState EditState() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState PlayState() => new(new GameTime()) { RunMode = RunMode.Play };

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

    private static void MakeSaveRoot(World world)
    {
        var root = world.CreateEntity();
        root.Set(new SceneObjectComponent());
        root.Set(new EntityInfoComponent("Player", "Hero"));
        root.Set(new TransformComponent(new Vector2(1, 2)));
    }

    private static SceneSerializer NewSerializer()
    {
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        return new SceneSerializer(registry);
    }

    /// <summary>A resolved project context (env var → an in-memory manifest), matching ToolbarTests.</summary>
    private static EditorProjectContext ResolvedContext()
    {
        const string root = "/proj";
        var manifestPath = Path.Combine(root, "Content", GameProject.FileName);
        var manifestJson = CanonicalJson.Serialize(new GameProject { StartScene = "island" });
        return EditorProjectContext.Resolve(
            baseDirectory: Path.Combine("/somewhere", "bin") + Path.DirectorySeparatorChar,
            getEnvironmentVariable: name => name == EditorProjectContext.ProjectRootVariable ? root : null,
            fileExists: p => p == manifestPath,
            readAllText: _ => manifestJson);
    }

    private static void WithPlatform(InMemoryPlatformServices fake, Action body)
    {
        var previous = PlatformServices.Current;
        try { PlatformServices.Current = fake; body(); }
        finally { PlatformServices.Current = previous; }
    }

    private sealed class InMemoryPlatformServices : IPlatformServices
    {
        public Dictionary<string, string> Files { get; } = new();
        public int WriteCount { get; private set; }
        public string BaseDirectory => "/scene/";
        public string GetEnvironmentVariable(string name) => null!;
        public string CombinePath(params string[] paths) => string.Join("/", paths);
        public bool FileExists(string path) => Files.ContainsKey(path);
        public string ReadAllText(string path) =>
            Files.TryGetValue(path, out var v) ? v : throw new FileNotFoundException(path);
        public void WriteAllText(string path, string contents) { Files[path] = contents; WriteCount++; }
        public void WriteAllBytes(string path, byte[] bytes) { }
        public string ExportScene(string suggestedFileName, string contents) { Files[suggestedFileName] = contents; return suggestedFileName; }
        public void CreateDirectory(string path) { }
        public TextWriter OpenLogWriter(string directory, string fileName) => TextWriter.Null;
        public void WriteLineToConsole(string line) { }
        public void RunBackground(Action work) => work();
    }
}
