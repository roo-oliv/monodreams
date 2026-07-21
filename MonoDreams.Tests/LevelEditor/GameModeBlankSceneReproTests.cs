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
/// UX3-A integration repro (the deliverable): the user-reported "launch the editor, switch to Game
/// mode, the entire scene disappears; returning to Scene mode doesn't help" bug.
///
/// <para><b>What the repro pinned.</b> A fresh boot loads a native scene that persists
/// <c>camera: null</c> (the UX2-E audit — every scene saved before UX2-E does). The reader auto-frames
/// the free VIEW on the off-origin content (scene visible in Scene mode) but the camera <b>rig</b>
/// stayed at its pre-load ctor default (origin, zoom 1) because <c>SyncFromScene(null)</c> left it
/// as-is. Entering Game mode snaps the view onto the rig ⇒ the view lands on empty world and the
/// content culls away ("disappears"). Returning to Scene mode fully restores the Scene view AND the
/// entities — but never cures the defect, because the Game-mode snapshot's <c>scene.camera</c> is built
/// from the origin rig, so the exit-restore re-syncs the rig to the origin AGAIN. The origin rig is
/// self-perpetuating, so <b>every</b> Game-mode entry is blank — that is why "returning doesn't help".
///
/// <para>The fix (UX3-A): when a load carries <c>camera: null</c>, the reader's rig seam re-syncs the
/// rig to the <b>post-load view</b> (after auto-framing) — "the authored camera starts on the content".
/// Then Game-mode entry snaps onto the content, and the snapshot re-persists the on-content rig, so a
/// round-trip keeps it there.</para>
///
/// <para>Mirrors a fresh editor boot as <c>LoadLevelExampleGameScreen</c> wires it: ONE rig-seam
/// <see cref="SceneReaderSystem"/> (the editor path — no double subscribe) and the transport's
/// Game-mode seams wired EXACTLY as <see cref="EditorOverlay"/> wires them. Uses the REAL
/// <see cref="CullingSystem"/> to prove visibility (the <see cref="LoadedSceneRendersTests"/>
/// technique) — reaching the draw path at the content region is the GraphicsDevice-free proof that a
/// non-blank frame would be painted.</para>
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

    /// <summary>Writes a native scene with one tagged sprite root at <paramref name="pos"/> and
    /// <c>camera: null</c> (the UX2-E audit — every existing scene persists a null camera).</summary>
    private static void WriteNullCameraScene(InMemoryPlatformServices fake, Vector2 pos)
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
        Assert.True(fake.Files.ContainsKey(SceneFileName), "the null-camera scene must be written");
        Assert.DoesNotContain("\"camera\"", fake.Files[SceneFileName]); // camera: null ⇒ field omitted (canonical)
    }

    /// <summary>The editor stack a fresh boot builds, wired EXACTLY as <see cref="EditorOverlay"/> wires
    /// the transport's Game-mode seams (the SINGLE rig-seam reader — no double subscribe).</summary>
    private sealed class Boot : IDisposable
    {
        public readonly World World;
        public readonly GameCamera View;      // the free editor VIEW (starts at origin, like a fresh boot)
        public readonly EditorCameraRig Rig;
        public readonly EditorHistory History;
        public readonly EditorTransport Transport;
        public readonly SceneReaderSystem Reader;

        public Boot()
        {
            World = new World();
            var serializer = new SceneSerializer(NewEngineRegistry());
            View = new GameCamera(800, 600); // (0,0), zoom 1 — the pre-load view
            Rig = new EditorCameraRig(World, View);
            Reader = new SceneReaderSystem(World, serializer, content: null,
                loadTexture: StubTexture, camera: View, applyCameraToRig: Rig.SyncFromScene);
            History = new EditorHistory(World);
            Transport = new EditorTransport(World, History)
            {
                Reload = () => World.Publish(new LoadSceneRequest(SceneFileName, fromContent: false)),
            };
            Transport.CaptureSnapshot = () => new SceneWriter(serializer).BuildScene(World, layers: null);
            Transport.RestoreSnapshot = snapshot => World.Publish(new LoadSceneRequest(snapshot));
            Transport.CaptureView = () => new CameraViewSnapshot(View.Position, View.Zoom, View.Rotation);
            Transport.RestoreView = v => { View.Position = v.Position; View.Zoom = v.Zoom; View.Rotation = v.Rotation; };
            Transport.SnapViewToRig = Rig.SnapViewToRig;
        }

        public Entity TaggedRoot()
        {
            using var set = World.GetEntities().With<SceneObjectComponent>().AsSet();
            foreach (var e in set.GetEntities()) return e;
            return default;
        }

        /// <summary>Runs the REAL CullingSystem and reports whether the (single) scene sprite reaches the
        /// draw path — the GraphicsDevice-free proxy for "a non-blank frame".</summary>
        public bool ContentPassesCulling()
        {
            using var culling = new CullingSystem(World, View);
            culling.Update(Edit());
            var root = TaggedRoot();
            return root.IsAlive && root.Has<VisibleComponent>() && root.Has<DrawComponent>();
        }

        public void Dispose() { Reader.Dispose(); World.Dispose(); }
    }

    // ─────────── The confirmed half: switch to Game mode → the scene must NOT disappear ───────────

    [Fact]
    public void FreshBoot_NullCamera_EnterGameMode_ContentStaysVisible()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            WriteNullCameraScene(fake, OffOrigin);
            using var b = new Boot();

            b.Transport.Reload(); // fresh boot: view auto-frames on content; rig adopts the post-load view
            Assert.True(b.ContentPassesCulling(), "boot: the scene must be visible in Scene mode");
            // The UX3-A fix: the rig no longer sits at the pre-load origin — it starts ON the content.
            Assert.NotEqual(Vector2.Zero, b.Rig.Position);

            // The user's first action: switch to Game mode (snaps the view onto the rig).
            b.Transport.EnterGameMode(Edit());

            // Before the fix the view jumped to the origin rig and the content culled away ("disappears").
            Assert.True(b.ContentPassesCulling(),
                "switching to Game mode must NOT make the scene disappear (the authored camera starts on content)");
        });
    }

    // ─────────── "Returning doesn't help": a full round-trip must keep the world AND cure Game mode ─────

    [Fact]
    public void FreshBoot_NullCamera_EnterExitReEnter_WorldIntact_AndGameModeStaysVisible()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            WriteNullCameraScene(fake, OffOrigin);
            using var b = new Boot();
            b.Transport.Reload();

            b.Transport.EnterGameMode(Edit()); // → blank before the fix
            b.Transport.ExitToSceneMode(Edit());

            // (a) The scene entities are alive and intact (a NEW entity, restored through the reader).
            var root = b.TaggedRoot();
            Assert.True(root.IsAlive, "returning to Scene mode must restore the scene entities");
            Assert.Equal(OffOrigin, root.Get<TransformComponent>().Position);
            Assert.True(root.Has<DrawComponent>()); // the reader restored the transient DrawComponent
            // (b) The VIEW ends where the content passes culling — Scene mode shows the scene.
            Assert.True(b.ContentPassesCulling(), "after returning to Scene mode the scene must be visible");

            // "Returning doesn't help" was: the rig stayed at origin, so re-entering Game mode blanks
            // AGAIN. Post-fix the snapshot re-persisted the on-content rig, so re-entry stays visible.
            b.Transport.EnterGameMode(Edit());
            Assert.True(b.ContentPassesCulling(),
                "re-entering Game mode after a round-trip must stay visible (the rig tracks the content)");
        });
    }
}
