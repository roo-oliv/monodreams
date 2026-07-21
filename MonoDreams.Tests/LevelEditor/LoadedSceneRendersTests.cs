using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.Platform;
using MonoDreams.State;
using MonoDreams.System.Draw;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Regression coverage for the FW1 "reloaded scene renders blank" bug (BUG #3). A reconstructed
/// sprite carries <see cref="SpriteInfoComponent"/> + <see cref="TransformComponent"/> but — before
/// the fix — no <see cref="DrawComponent"/> (it is transient / never serialized). <c>SpritePrepSystem</c>
/// requires <c>[With(DrawComponent, SpriteInfoComponent, TransformComponent, VisibleComponent)]</c>, so
/// a <c>DrawComponent</c>-less sprite is never prepped, never drawn, and the Main target stays the
/// backbuffer clear color. These tests reload a native scene through the REAL <see cref="SceneReaderSystem"/>
/// and assert (a) the reloaded sprite HAS a <see cref="DrawComponent"/>, and (b) after the reader
/// auto-frames the camera on the (off-origin) content, the REAL <see cref="CullingSystem"/> tags the
/// sprite <see cref="VisibleComponent"/> — i.e. it enters the draw path at the content region rather
/// than culling to an empty frame. Both assertions FAIL before the fix.
///
/// <para>No <c>GraphicsDevice</c>: the sprite's texture is a stub (the paint step needs a GPU, but the
/// bug is that the sprite never REACHES the paint step). Reaching <c>SpritePrepSystem</c>'s query
/// (DrawComponent present) + surviving culling (Visible added) is the GraphicsDevice-free proof that a
/// non-blank frame would be painted. Routes through the process-global <see cref="PlatformServices.Current"/>,
/// so this class is in the non-parallel collection and restores the default.</para>
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class LoadedSceneRendersTests
{
    private const string SceneFileName = "loaded-scene.mdscene";

    // A far-off-origin position, mirroring Blender_Level's content sitting at ~(1275,-530) — the exact
    // case where a camera stuck at (0,0) renders blank while the content is elsewhere.
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

    // Texture stub: no GraphicsDevice (returns null). The reloaded sprite still enters the draw path;
    // the missing GPU texture only means the paint step has nothing to blit, not that it never runs.
    private static Texture2D StubTexture(string _) => null;

    /// <summary>Writes a scene holding one tagged sprite root at <paramref name="position"/> (plus an
    /// optional active camera-follow target) into the in-memory store, ready to reload.</summary>
    private static void WriteSceneWithSprite(InMemoryPlatformServices fake, Vector2 position, bool withFollowTarget)
    {
        using var world = new World();
        var writer = new SceneWriter(new SceneSerializer(NewEngineRegistry()));

        var root = world.CreateEntity();
        root.Set(new SceneObjectComponent());
        root.Set(new EntityInfoComponent("Prop", "Tree"));
        root.Set(new TransformComponent(position));
        root.Set(new SpriteInfoComponent
        {
            AssetKey = "Atlas/TX Tree",
            Source = new Rectangle(0, 0, 16, 16),
            Size = new Vector2(16, 16),
            Color = Color.White,
            Target = RenderTargetID.Main,
            LayerDepth = 0.5f,
        });
        // Deliberately NO DrawComponent on the source root either — the writer never serializes it, so
        // whether the source had one is irrelevant; the reader must reconstruct it on load.
        if (withFollowTarget)
            root.Set(new CameraFollowTargetComponent { IsActive = true });

        writer.Save(world, SceneFileName, layers: null);
        Assert.True(fake.Files.ContainsKey(SceneFileName));
    }

    private static List<Entity> LoadedEntities(World world)
    {
        var list = new List<Entity>();
        using var set = world.GetEntities().With<SpriteInfoComponent>().AsSet();
        foreach (var e in set.GetEntities()) list.Add(e);
        return list;
    }

    // ---- BUG #3 core: the reloaded sprite HAS a DrawComponent (so SpritePrepSystem preps it) ----

    [Fact]
    public void ReloadedSprite_HasDrawComponent_SoItEntersThePrepQuery()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            WriteSceneWithSprite(fake, OffOrigin, withFollowTarget: false);

            using var world = new World();
            var camera = new MonoDreams.Component.Camera();
            using var reader = new SceneReaderSystem(world, new SceneSerializer(NewEngineRegistry()),
                content: null, loadTexture: StubTexture, camera: camera);

            world.Publish(new LoadSceneRequest(SceneFileName, fromContent: false));

            var sprite = Assert.Single(LoadedEntities(world));
            // The pairing SpritePrepSystem's [With(DrawComponent, …)] query requires — absent before the fix.
            Assert.True(sprite.Has<DrawComponent>(),
                "A reloaded sprite must have a DrawComponent, else SpritePrepSystem never preps it and it renders blank.");
            var draw = sprite.Get<DrawComponent>();
            Assert.Equal(DrawElementType.Sprite, draw.Type);
            Assert.Equal(RenderTargetID.Main, draw.Target); // mirrors the sprite's own Target
        });
    }

    // ---- BUG #3 secondary: auto-frame + real CullingSystem => the off-origin sprite is drawn ----

    [Fact]
    public void ReloadedScene_AutoFramesCameraAndPassesCulling_SoItRendersNonBlank()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            WriteSceneWithSprite(fake, OffOrigin, withFollowTarget: false);

            using var world = new World();
            var camera = new MonoDreams.Component.Camera(); // starts at (0,0) — where a naive load leaves it, off the content
            using var reader = new SceneReaderSystem(world, new SceneSerializer(NewEngineRegistry()),
                content: null, loadTexture: StubTexture, camera: camera);

            world.Publish(new LoadSceneRequest(SceneFileName, fromContent: false));

            // Auto-frame moved the camera onto the off-origin content (no follow target present).
            Assert.NotEqual(Vector2.Zero, camera.Position);
            Assert.True(camera.Position.X > 1000f, $"camera X {camera.Position.X} should sit on the ~1275 content");
            Assert.True(camera.Position.Y < 0f, $"camera Y {camera.Position.Y} should sit on the ~-530 content");

            // The REAL culling system now sees the content and tags it visible — the last gate before
            // SpritePrepSystem. Before the fix the camera stayed at (0,0) and the sprite culled away.
            using var culling = new CullingSystem(world, camera);
            var state = new GameState(new GameTime()) { RunMode = RunMode.Edit };
            culling.Update(state);

            var sprite = Assert.Single(LoadedEntities(world));
            Assert.True(sprite.Has<VisibleComponent>(),
                "After auto-framing, CullingSystem must tag the on-screen sprite VisibleComponent.");
            Assert.True(sprite.Has<DrawComponent>()); // both draw-path gates satisfied => a non-blank frame
        });
    }

    // ---- Auto-frame must NOT override an active camera-follow target (a scene with a player) ----

    [Fact]
    public void ReloadedScene_WithActiveFollowTarget_LeavesCameraAlone()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            WriteSceneWithSprite(fake, OffOrigin, withFollowTarget: true);

            using var world = new World();
            var camera = new MonoDreams.Component.Camera(); // (0,0)
            using var reader = new SceneReaderSystem(world, new SceneSerializer(NewEngineRegistry()),
                content: null, loadTexture: StubTexture, camera: camera);

            world.Publish(new LoadSceneRequest(SceneFileName, fromContent: false));

            // CameraFollowSystem owns the camera when a follow target is present — the reader must not fight it.
            Assert.Equal(Vector2.Zero, camera.Position);
            Assert.Equal(1.0f, camera.Zoom);
            // The DrawComponent restore is unconditional, though — the sprite is still drawable.
            Assert.True(Assert.Single(LoadedEntities(world)).Has<DrawComponent>());
        });
    }
}
