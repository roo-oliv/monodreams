#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Channel;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Platform;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Cursor;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the Wave 5 headless editor-op channel (contract item 15) and closes §6 #12 — the headless
/// editor-op integration test. It drives <b>select → gizmo-drag (move) → undo → save</b> with
/// <b>no real mouse</b>, through the SAME real editor systems the screen wires (<see cref="SelectionSystem"/>,
/// <see cref="GizmoSystem"/>, <see cref="HierarchySystem"/>, <see cref="ToolbarSystem"/>-style dispatch
/// + <see cref="SceneWriter"/>, the shared <see cref="EditorHistory"/>), composed in-process over a real
/// <c>World</c>. The cursor is driven entirely by the <see cref="EditorOpReplaySystem"/> editor-op
/// channel, which injects <see cref="CursorInputComponent"/> state (<see cref="CursorInputSystem"/> is
/// run with <c>SkipHardwareRead = true</c> so the injected state survives), drives the transport
/// (Pause = Edit), and fires toolbar actions.
///
/// <para>This is the in-process form of the <c>GameTestRunner</c>-style headless run: the Examples
/// headless host early-returns from <c>Draw</c> (where <see cref="SelectionSystem"/> is ordered), so the
/// select→render path can only be exercised by composing the real systems directly — exactly per the
/// "the editor is the game; tests exercise the REAL editor systems headless" directive. The screen wires
/// the SAME channel + seam into the headless host (<c>LevelEditorScreen</c>), proving the
/// <c>SkipHardwareRead</c> seam + the session-hold-open against the input-replay auto-exit.</para>
///
/// <para>The asserted invariants: the entity <b>moves</b> by the dragged delta (one undo step), <b>undo
/// reverts</b> it to the pre-drag transform, and the <b>saved scene matches expected</b> — the membership
/// closure (the tagged root) with the reverted transform — captured through a fake
/// <see cref="IPlatformServices"/> (no disk).</para>
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class HeadlessEditorOpTests
{
    private const string SceneFileName = "headless-editor-op.scene.json";

    /// <summary>In-memory platform capturing the Save write (PS3 writes via WriteAllText) so the Save
    /// assertion needs no disk.</summary>
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
        public string ExportScene(string suggestedFileName, string contents)
        {
            Files[suggestedFileName] = contents;
            return suggestedFileName;
        }
        public void CreateDirectory(string path) { }
        public TextWriter OpenLogWriter(string directory, string fileName) => TextWriter.Null;
        public void WriteLineToConsole(string line) { }
        public void RunBackground(Action work) => work();
    }

    private static void WithPlatform(InMemoryPlatformServices fake, Action body)
    {
        var previous = PlatformServices.Current;
        try { PlatformServices.Current = fake; body(); }
        finally { PlatformServices.Current = previous; }
    }

    /// <summary>A 10×10 sprite tagged as a save-root, origin top-left, at <paramref name="position"/>.</summary>
    private static Entity MakeSpriteRoot(World world, Vector2 position)
    {
        var e = world.CreateEntity();
        e.Set(new EntityInfoComponent("Player", "Hero"));
        e.Set(new TransformComponent(position));
        e.Set(new SpriteInfoComponent
        {
            Source = new Rectangle(0, 0, 10, 10),
            Size = new Vector2(10, 10),
            Origin = Vector2.Zero,
            AssetKey = "Atlas/TX Player",
            Target = RenderTargetID.Main,
        });
        // The "final" post-YSort depth selection reads (set directly — no GraphicsDevice / YSortSystem here).
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

    [Fact]
    public void HeadlessEditorOpTest()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            using var world = new World();
            var camera = new GameCamera(800, 600) { Zoom = 1f, Position = Vector2.Zero };

            // The shared editor infrastructure (the SAME instances the screen wires).
            var registry = new ComponentSerializerRegistry();
            registry.RegisterEngineComponents();
            var serializer = new SceneSerializer(registry);
            var history = new EditorHistory(world);

            // The single shared gizmo-state entity (Move tool, snap off by default).
            var gizmoState = world.CreateEntity();
            gizmoState.Set(GizmoStateComponent.Default);

            // The scene: one selectable sprite root at the world origin (pivot == its top-left).
            var startPos = new Vector2(0f, 0f);
            var entity = MakeSpriteRoot(world, startPos);
            MakeCursor(world);

            // Capture any LoadSceneRequest (the channel could fire Load; here we only Save/Undo).
            var loadRequests = new List<LoadSceneRequest>();
            world.Subscribe((in LoadSceneRequest r) => loadRequests.Add(r));

            // The toolbar-action dispatch closure — same shape as EditorOverlay.DispatchToolbarAction.
            Action<EditorToolbarAction, GameState> dispatch = (action, _) =>
            {
                switch (action)
                {
                    case EditorToolbarAction.Save:
                        new SceneWriter(serializer).Save(world, SceneFileName, camera, layers: null);
                        break;
                    case EditorToolbarAction.Load:
                        world.Publish(new LoadSceneRequest(SceneFileName, fromContent: false));
                        break;
                    case EditorToolbarAction.Undo: history.Undo(); break;
                    case EditorToolbarAction.Redo: history.Redo(); break;
                    case EditorToolbarAction.ToolMove:
                    case EditorToolbarAction.ToolRotate:
                    case EditorToolbarAction.ToolScale:
                    case EditorToolbarAction.ToggleSnap:
                        break;
                }
            };

            // ---- The scripted editor-op plan: pause the transport (enter Edit), click the sprite
            //      (selects + grabs the move handle at the pivot), drag it +40,+24 over three
            //      frames, release, then undo, then save. ----
            var dragEnd = new Vector2(40f, 24f);
            var exitCount = 0;
            var plan = new EditorOpPlan
            {
                Description = "headless select → move-drag → undo → save",
                TailFrames = 1,
                Ops = new List<EditorOp>
                {
                    new() { Frame = 0, Kind = EditorOpKind.Pause },                    // transport: Paused (Edit)
                    new() { Frame = 0, Kind = EditorOpKind.MoveCursor, X = 0f, Y = 0f },// hover the pivot
                    new() { Frame = 1, Kind = EditorOpKind.LeftDown, X = 0f, Y = 0f },  // press: select + grab handle
                    new() { Frame = 2, Kind = EditorOpKind.LeftDown, X = 16f, Y = 10f },// drag
                    new() { Frame = 3, Kind = EditorOpKind.LeftDown, X = 30f, Y = 18f },// drag
                    new() { Frame = 4, Kind = EditorOpKind.LeftDown, X = dragEnd.X, Y = dragEnd.Y }, // drag to final
                    new() { Frame = 5, Kind = EditorOpKind.LeftUp,   X = dragEnd.X, Y = dragEnd.Y }, // release → commit
                    new() { Frame = 6, Kind = EditorOpKind.ToolbarAction, Action = "Undo" }, // undo the drag
                    new() { Frame = 7, Kind = EditorOpKind.ToolbarAction, Action = "Save" }, // save the reverted scene
                },
            };

            var driver = new EditorOpReplaySystem(world, plan, dispatch, requestExit: () => exitCount++);

            // The real editor systems, composed exactly in the screen's order: the channel injects the
            // cursor first, selection picks on the press edge, the gizmo drives the drag → one undo step,
            // and HierarchySystem (live in Edit) propagates the edit each frame.
            var cursorInput = new CursorInputSystem(world) { SkipHardwareRead = true };
            using var selection = new SelectionSystem(world);
            using var gizmo = new GizmoSystem(world, camera, history);
            using var hierarchy = new HierarchySystem(world);

            using var pipeline = new SequentialSystem<GameState>(
                cursorInput,  // SkipHardwareRead → does NOT clobber the injected cursor
                driver,       // injects the scripted cursor + toggles mode + fires toolbar actions
                selection,    // picks on the press edge (Edit-guarded)
                gizmo,        // drag → one undo step (Edit-guarded), BEFORE hierarchy
                hierarchy);   // live in Edit: propagates the gizmo edit this frame

            var state = new GameState(new GameTime());
            Assert.Equal(RunMode.Play, state.RunMode);

            // Run frames until the driver holds-open-then-drains and requests exit (capped for safety).
            var moved = false;
            float midDragX = 0f;
            var frame = 0;
            for (; frame < 32 && exitCount == 0; frame++)
            {
                pipeline.Update(state);

                // After the press frame the entity is selected; mid-drag it has moved off the start.
                if (frame == 3) midDragX = entity.Get<TransformComponent>().Position.X;
                if (frame == 5)
                {
                    // After release+commit the entity sits at the drag end and there is exactly one undo step.
                    moved = entity.Get<TransformComponent>().Position == startPos + dragEnd;
                }
            }

            // ---- The run entered Edit, selected, dragged, and the driver requested exit exactly once. ----
            Assert.Equal(RunMode.Edit, state.RunMode);
            Assert.True(entity.Has<SelectedComponent>(), "the scripted click should have selected the sprite");
            Assert.True(midDragX > startPos.X, "the entity should have moved during the drag");
            Assert.True(moved, "after release the entity should sit at the drag-end position");
            Assert.Equal(1, exitCount); // requestExit fired exactly once (session held open then drained)

            // ---- Undo reverted the move to the pre-drag transform (one undo step for the whole drag). ----
            Assert.Equal(startPos, entity.Get<TransformComponent>().Position);
            Assert.Equal(0, history.Count);
            Assert.Equal(1, history.RedoCount);

            // ---- Save wrote the scene through IPlatformServices (the reverted transform is persisted). ----
            Assert.Equal(1, fake.WriteCount);
            Assert.True(fake.Files.TryGetValue(SceneFileName, out var json));

            var scene = JsonSerializer.Deserialize<SceneData>(json!,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            // Expected: exactly the tagged root (membership closure of one), camera state, reverted position.
            Assert.Single(scene.Entities);
            var savedTransform = scene.Entities[0].Components["core.Transform"];
            var px = savedTransform.GetProperty("position")[0].GetSingle();
            var py = savedTransform.GetProperty("position")[1].GetSingle();
            Assert.Equal(startPos.X, px, 3);
            Assert.Equal(startPos.Y, py, 3);

            // The AssetKey (SOURCE field, never the live texture) round-trips on the sprite.
            var savedSprite = scene.Entities[0].Components["core.SpriteInfo"];
            Assert.Equal("Atlas/TX Player", savedSprite.GetProperty("assetKey").GetString());

            // Camera state captured at save.
            Assert.NotNull(scene.Camera);
            Assert.Equal(1f, scene.Camera!.Zoom, 3);
        });
    }
}
