using System;
using System.Collections.Generic;
using System.IO;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Platform;
using MonoDreams.State;
using MonoDreams.System.Draw;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// UX3-A integration repro (the deliverable), carried into CM: the user-reported "launch the editor,
/// switch to Game mode, the entire scene disappears; returning to Scene mode doesn't help" bug.
///
/// <para><b>What the repro pins under CM.</b> A fresh boot loads a native scene that carries NO camera
/// entity (a legacy scene, or any scene authored before the camera-as-entity model). The reader
/// auto-frames the free VIEW on the off-origin content (scene visible in Scene mode) AND <b>ensures a
/// camera ENTITY positioned on the content's AABB centre</b> (the CM one-camera rule + the UX3-A
/// sane-default lesson). Entering Game mode snaps the view onto that camera entity — which sits ON the
/// content, so the scene stays visible. If the reader instead ensured a camera at the origin, the view
/// would snap to empty world and the content would cull away ("disappears"); the on-content ensure is the
/// fix. And because the ensured camera is <c>SceneObjectComponent</c>-tagged, it rides the Game-mode
/// snapshot, so a round-trip keeps it on the content — "returning" stays visible too.</para>
///
/// <para>Mirrors a fresh editor boot as <c>LoadLevelExampleGameScreen</c> wires it: the disk reload is
/// wired as <see cref="EditorTransport.ReloadSceneContent"/> (NOT the deprecated combined
/// <c>Reload</c> setter — that routes the disk load through <c>RebuildCodeContent</c>, which a Game-tab
/// exit invokes, double-loading the scene on top of the snapshot restore). The ONE ensure-one-camera
/// <see cref="SceneReaderSystem"/> and the transport's Game-mode seams are wired EXACTLY as
/// <see cref="EditorOverlay"/> wires them, with <c>SnapViewToCameraEntity</c> reading the scene camera
/// entity. Uses the REAL <see cref="CullingSystem"/> to prove visibility.</para>
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class GameModeBlankSceneReproTests
{
    private const string SceneFileName = "fresh-boot.mdscene";

    // Off-origin content, mirroring Blender_Level at ~(1275,-530): the exact case where a camera at
    // (0,0) renders blank while the content is elsewhere.
    private static readonly Vector2 OffOrigin = new(1275, -530);

    private sealed class InMemoryPlatformServices : IPlatformServices
    {
        public Dictionary<string, string> Files { get; } = new();
        public string BaseDirectory => "/scene/";
        public string GetEnvironmentVariable(string name) => null;
        public string CombinePath(params string[] paths) => string.Join("/", paths);
        public bool FileExists(string path) => Files.ContainsKey(path);
        public string ReadAllText(string path) =>
            Files.TryGetValue(path, out var v) ? v : throw new FileNotFoundException(path);
        public void WriteAllText(string path, string contents) => Files[path] = contents;
        public void WriteAllBytes(string path, byte[] bytes) { }
        public string ExportScene(string suggestedFileName, string contents) { Files[suggestedFileName] = contents; return suggestedFileName; }
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

    private static ComponentSerializerRegistry NewEngineRegistry()
    {
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        return registry;
    }

    private static Texture2D StubTexture(string _) => null;

    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };

    /// <summary>Writes a native scene with one tagged sprite root at <paramref name="pos"/> and NO camera
    /// entity (a legacy scene — the reader ensures a camera on load).</summary>
    private static void WriteCameraLessScene(InMemoryPlatformServices fake, Vector2 pos)
    {
        using var world = new World();
        var root = world.CreateEntity();
        root.Set(new SceneObjectComponent());
        root.Set(new EntityInfoComponent("Prop", "Tree"));
        root.Set(new TransformComponent(pos));
        root.Set(new SpriteInfoComponent
        {
            AssetKey = "Atlas/TX Tree",
            Source = new Rectangle(0, 0, 16, 16),
            Size = new Vector2(16, 16),
            Color = Color.White,
            Target = RenderTargetID.Main,
            LayerDepth = 0.5f,
        });
        new SceneWriter(new SceneSerializer(NewEngineRegistry())).Save(world, SceneFileName, layers: null);
        Assert.True(fake.Files.ContainsKey(SceneFileName), "the camera-less scene must be written");
        Assert.DoesNotContain(EngineComponentSerializers.CameraKey, fake.Files[SceneFileName]); // no camera entity in the file
    }

    /// <summary>The editor stack a fresh boot builds, wired EXACTLY as <see cref="EditorOverlay"/> wires
    /// the transport's Game-mode seams (the SINGLE ensure-one-camera reader — no double subscribe).</summary>
    private sealed class Boot : IDisposable
    {
        public readonly World World;
        public readonly GameCamera View;      // the free editor VIEW (starts at origin, like a fresh boot)
        public readonly CameraEntityOverlay CameraOverlay;
        public readonly EditorHistory History;
        public readonly EditorTransport Transport;
        public readonly SceneReaderSystem Reader;

        public Boot()
        {
            World = new World();
            var serializer = new SceneSerializer(NewEngineRegistry());
            View = new GameCamera(800, 600); // (0,0), zoom 1 — the pre-load view
            CameraOverlay = new CameraEntityOverlay(World, View);
            Reader = new SceneReaderSystem(World, serializer, content: null,
                loadTexture: StubTexture, camera: View, ensureSingleCamera: true);
            History = new EditorHistory(World);
            Transport = new EditorTransport(World, History);
            // The disk reload is the SCENE-content half (NOT the combined Reload setter): a Game-tab exit
            // must restore the scene from the in-memory snapshot ONLY, never re-load from disk on top of it.
            Transport.ReloadSceneContent = () => World.Publish(new LoadSceneRequest(SceneFileName, fromContent: false));
            Transport.CaptureSnapshot = () => new SceneWriter(serializer).BuildScene(World, layers: null);
            Transport.RestoreSnapshot = snapshot => World.Publish(new LoadSceneRequest(snapshot));
            Transport.CaptureView = () => new CameraViewSnapshot(View.Position, View.Zoom, View.Rotation);
            Transport.RestoreView = v => { View.Position = v.Position; View.Zoom = v.Zoom; View.Rotation = v.Rotation; };
            Transport.SnapViewToCameraEntity = CameraOverlay.SnapViewToCameraEntity;
        }

        /// <summary>The sprite scene root (the content — NOT the ensured camera entity).</summary>
        public Entity SpriteRoot()
        {
            using var set = World.GetEntities().With<SpriteInfoComponent>().With<TransformComponent>().AsSet();
            foreach (var e in set.GetEntities()) return e;
            return default;
        }

        /// <summary>The ensured camera entity.</summary>
        public Entity CameraEntity()
        {
            using var set = World.GetEntities().With<CameraComponent>().AsSet();
            foreach (var e in set.GetEntities()) return e;
            return default;
        }

        /// <summary>Runs the REAL CullingSystem and reports whether the (single) scene sprite reaches the
        /// draw path — the GraphicsDevice-free proxy for "a non-blank frame".</summary>
        public bool ContentPassesCulling()
        {
            using var culling = new CullingSystem(World, View);
            culling.Update(Edit());
            var root = SpriteRoot();
            return root.IsAlive && root.Has<VisibleComponent>() && root.Has<DrawComponent>();
        }

        public void Dispose() { Reader.Dispose(); World.Dispose(); }
    }

    // ─────────── The confirmed half: switch to Game mode → the scene must NOT disappear ───────────

    [Fact]
    public void FreshBoot_CameraLess_EnterGameMode_ContentStaysVisible()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            WriteCameraLessScene(fake, OffOrigin);
            using var b = new Boot();

            b.Transport.Reload(); // fresh boot: view auto-frames on content; reader ensures a camera on content
            Assert.True(b.ContentPassesCulling(), "boot: the scene must be visible in Scene mode");
            // The CM fix: the ensured camera sits ON the content, not at the pre-load origin.
            Assert.NotEqual(Vector2.Zero, b.CameraEntity().Get<TransformComponent>().Position);

            // The user's first action: switch to Game mode (snaps the view onto the camera entity).
            b.Transport.EnterGameMode(Edit());

            // Before the fix the view jumped to an origin camera and the content culled away ("disappears").
            Assert.True(b.ContentPassesCulling(),
                "switching to Game mode must NOT make the scene disappear (the camera starts on content)");
        });
    }

    // ─────────── "Returning doesn't help": a full round-trip must keep the world AND cure Game mode ─────

    [Fact]
    public void FreshBoot_CameraLess_EnterExitReEnter_WorldIntact_AndGameModeStaysVisible()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            WriteCameraLessScene(fake, OffOrigin);
            using var b = new Boot();
            b.Transport.Reload();

            b.Transport.EnterGameMode(Edit()); // → blank before the fix
            b.Transport.ExitToSceneMode(Edit());

            // (a) The scene entities are alive and intact (a NEW entity, restored through the reader).
            var root = b.SpriteRoot();
            Assert.True(root.IsAlive, "returning to Scene mode must restore the scene entities");
            Assert.Equal(OffOrigin, root.Get<TransformComponent>().Position);
            Assert.True(root.Has<DrawComponent>()); // the reader restored the transient DrawComponent
            // (b) The VIEW ends where the content passes culling — Scene mode shows the scene.
            Assert.True(b.ContentPassesCulling(), "after returning to Scene mode the scene must be visible");
            // Still exactly one camera entity after the round-trip (the ensured camera rode the snapshot,
            // and the Game-tab exit restored from that snapshot ONLY — no disk re-load doubling it).
            using var cams = b.World.GetEntities().With<CameraComponent>().AsSet();
            Assert.Single(cams.GetEntities().ToArray());

            // "Returning doesn't help" was: the camera stayed at origin, so re-entering Game mode blanks
            // AGAIN. Post-fix the snapshot re-persisted the on-content camera, so re-entry stays visible.
            b.Transport.EnterGameMode(Edit());
            Assert.True(b.ContentPassesCulling(),
                "re-entering Game mode after a round-trip must stay visible (the camera tracks the content)");
        });
    }
}
