#nullable enable
using System;
using System.Linq;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Camera;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the camera-as-entity model (CM-A): the <see cref="CameraComponent"/> + the sync adapter, the
/// follow-writes-the-entity retarget, the one-camera writer/prefab/expander rules, the reader's
/// ensure-one-camera, and the camera-entity byte round-trip. Pure logic — hand-built worlds, no
/// GraphicsDevice.
/// </summary>
public class CameraEntityTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    private static ComponentSerializerRegistry Registry()
    {
        var r = new ComponentSerializerRegistry();
        r.RegisterEngineComponents();
        return r;
    }

    private static Entity CameraEntity(World world, Vector2 pos, float rotation = 0f, float zoom = 1f, bool tagged = true)
    {
        var e = world.CreateEntity();
        if (tagged) e.Set(new SceneObjectComponent());
        e.Set(new EntityInfoComponent("Camera"));
        e.Set(new TransformComponent(pos, rotation));
        e.Set(new CameraComponent { Zoom = zoom });
        return e;
    }

    private static Entity Prop(World world, Vector2 pos, string name = "Tree")
    {
        var e = world.CreateEntity();
        e.Set(new SceneObjectComponent());
        e.Set(new EntityInfoComponent("Prop", name));
        e.Set(new TransformComponent(pos));
        e.Set(new SpriteInfoComponent
        {
            AssetKey = "Atlas/TX Tree",
            Source = new Rectangle(0, 0, 16, 16),
            Size = new Vector2(16, 16),
            Color = Color.White,
            Target = RenderTargetID.Main,
            LayerDepth = 0.5f,
        });
        return e;
    }

    // ─────────────────────── CameraSyncSystem: entity pose → adapter ───────────────────────

    [Fact]
    public void Sync_CopiesEntityPositionRotationZoom_IntoTheAdapter()
    {
        using var world = new World();
        var adapter = new GameCamera(800, 600);
        CameraEntity(world, new Vector2(120, -34), rotation: 0.5f, zoom: 2.5f);

        using var sync = new CameraSyncSystem(world, adapter);
        sync.Update(Play());

        Assert.Equal(new Vector2(120, -34), adapter.Position);
        Assert.Equal(0.5f, adapter.Rotation, 4);          // WorldRotation from the Transform (pre-mortem #1)
        Assert.Equal(2.5f, adapter.Zoom, 4);
    }

    [Fact]
    public void Sync_FrozenInEdit_NeverWritesTheAdapter_ButRunsInPlay()
    {
        using var world = new World();
        var adapter = new GameCamera(800, 600) { Position = new Vector2(1, 2) };
        CameraEntity(world, new Vector2(500, 500), zoom: 3f);

        var gated = new GatedSystem(new CameraSyncSystem(world, adapter), EditTimeBehavior.Freeze);

        gated.Update(Edit());
        // Frozen: the editor's free view (the adapter) is untouched — CM pre-mortem #2.
        Assert.Equal(new Vector2(1, 2), adapter.Position);
        Assert.Equal(1f, adapter.Zoom, 4);

        gated.Update(Play());
        // Play: the sync copies the camera entity's pose into the adapter.
        Assert.Equal(new Vector2(500, 500), adapter.Position);
        Assert.Equal(3f, adapter.Zoom, 4);

        gated.Dispose();
    }

    // ─────────────────────── CameraFollowSystem: eases the ENTITY, sync follows ───────────────────────

    [Fact]
    public void Follow_MovesTheCameraEntity_Inspectable_AdapterFollowsViaSync()
    {
        using var world = new World();
        var adapter = new GameCamera(800, 600);
        var camera = CameraEntity(world, Vector2.Zero);

        var target = world.CreateEntity();
        target.Set(new TransformComponent(new Vector2(400, 300)));
        target.Set(new CameraFollowTargetComponent
        {
            DampingX = 1000f, DampingY = 1000f, MaxDistanceX = 1e6f, MaxDistanceY = 1e6f,
        });

        using var follow = new CameraFollowSystem(world);
        using var sync = new CameraSyncSystem(world, adapter);

        var tick = new GameState(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1))) { RunMode = RunMode.Play };
        follow.Update(tick);

        // The follow wrote the camera ENTITY's Transform (live-inspectable), NOT the adapter directly.
        Assert.Equal(400f, camera.Get<TransformComponent>().Position.X, 1);
        Assert.Equal(300f, camera.Get<TransformComponent>().Position.Y, 1);
        Assert.Equal(Vector2.Zero, adapter.Position); // adapter not yet touched (sync is the only writer)

        sync.Update(tick);
        Assert.Equal(400f, adapter.Position.X, 1);
        Assert.Equal(300f, adapter.Position.Y, 1);
    }

    // ─────────────────────── One-camera writer / prefab / expander rules ───────────────────────

    [Fact]
    public void Writer_RefusesTwoCameraEntities_NamingThem()
    {
        using var world = new World();
        var camA = world.CreateEntity();
        camA.Set(new SceneObjectComponent());
        camA.Set(new EntityInfoComponent("Camera", "MainCam"));
        camA.Set(new TransformComponent(Vector2.Zero));
        camA.Set(new CameraComponent());
        var camB = world.CreateEntity();
        camB.Set(new SceneObjectComponent());
        camB.Set(new EntityInfoComponent("Camera", "SecondCam"));
        camB.Set(new TransformComponent(new Vector2(10, 10)));
        camB.Set(new CameraComponent());

        var ex = Assert.Throws<InvalidOperationException>(
            () => new SceneWriter(new SceneSerializer(Registry())).BuildScene(world));
        Assert.Contains("2 camera entities", ex.Message);
        Assert.Contains("MainCam", ex.Message);
        Assert.Contains("SecondCam", ex.Message);
    }

    [Fact]
    public void Writer_OneCameraEntity_SerializesIt()
    {
        using var world = new World();
        Prop(world, new Vector2(50, 50));
        CameraEntity(world, new Vector2(100, 100), zoom: 1.5f);

        var scene = new SceneWriter(new SceneSerializer(Registry())).BuildScene(world);

        var cam = scene.Entities.Single(e => e.Components.ContainsKey(EngineComponentSerializers.CameraKey));
        Assert.Equal(1.5f, cam.Components[EngineComponentSerializers.CameraKey].GetProperty("zoom").GetSingle(), 4);
    }

    [Fact]
    public void PrefabWriter_RefusesACameraEntity()
    {
        using var world = new World();
        // A single-root prefab world whose root carries a camera — illegal (a prefab is a class).
        var root = world.CreateEntity();
        root.Set(new SceneObjectComponent());
        root.Set(new EntityInfoComponent("Camera"));
        root.Set(new TransformComponent(Vector2.Zero));
        root.Set(new CameraComponent());

        var writer = new PrefabWriter(new SceneWriter(new SceneSerializer(Registry())));
        var ex = Assert.Throws<InvalidOperationException>(() => writer.BuildPrefab(world, "bad-prefab"));
        Assert.Contains("cannot contain a camera", ex.Message);
    }

    [Fact]
    public void PrefabExpander_RefusesALegacyPrefabCarryingACamera()
    {
        // A hand-built prefab SceneData that (illegally) contains a camera entity.
        var scene = new SceneData();
        var cam = new SceneEntityData { Id = 0 };
        cam.Components[EngineComponentSerializers.TransformKey] =
            CanonicalJson.SerializeToElement(new { position = new[] { 0f, 0f }, rotation = 0f, scale = new[] { 1f, 1f }, origin = new[] { 0f, 0f } });
        cam.Components[EngineComponentSerializers.CameraKey] = CanonicalJson.SerializeToElement(new { zoom = 1f });
        scene.Entities.Add(cam);
        var prefab = PrefabData.FromScene("legacy-cam", scene);

        var expander = new PrefabExpander(new SceneSerializer(Registry()), _ => prefab);
        using var world = new World();
        var ex = Assert.Throws<InvalidOperationException>(() => expander.Instantiate(world, "legacy-cam"));
        Assert.Contains("contains a camera entity", ex.Message);
    }

    // ─────────────────────── Reader ensures exactly one camera (pre-mortem #3) ───────────────────────

    [Fact]
    public void Reader_EnsuresOneCamera_WhenSceneHasNone_PositionedOnContent_Tagged()
    {
        using var world = new World();
        // A prop-only scene (no camera), restored in-memory with ensureSingleCamera on.
        var authorWorld = new World();
        Prop(authorWorld, new Vector2(200, 100));
        var scene = new SceneWriter(new SceneSerializer(Registry())).BuildScene(authorWorld);
        authorWorld.Dispose();

        using var reader = new SceneReaderSystem(world, new SceneSerializer(Registry()), content: null!,
            loadTexture: _ => null!, ensureSingleCamera: true);
        world.Publish(new LoadSceneRequest(scene));

        using var cams = world.GetEntities().With<CameraComponent>().AsSet();
        var created = cams.GetEntities().ToArray();
        Assert.Single(created);
        var cam = created[0];
        Assert.True(cam.Has<SceneObjectComponent>());                       // saves with the scene
        Assert.Equal("Camera", cam.Get<EntityInfoComponent>().Type);
        // Positioned on the content AABB centre (sprite at (200,100), size 16 → centre (208,108)).
        Assert.Equal(208f, cam.Get<TransformComponent>().Position.X, 1);
        Assert.Equal(108f, cam.Get<TransformComponent>().Position.Y, 1);
    }

    [Fact]
    public void Reader_EnsureIsIdempotent_WhenSceneAlreadyHasACamera()
    {
        using var authorWorld = new World();
        Prop(authorWorld, new Vector2(0, 0));
        CameraEntity(authorWorld, new Vector2(42, 42), zoom: 1.25f);
        var scene = new SceneWriter(new SceneSerializer(Registry())).BuildScene(authorWorld);

        using var world = new World();
        using var reader = new SceneReaderSystem(world, new SceneSerializer(Registry()), content: null!,
            loadTexture: _ => null!, ensureSingleCamera: true);
        world.Publish(new LoadSceneRequest(scene));

        using var cams = world.GetEntities().With<CameraComponent>().AsSet();
        var created = cams.GetEntities().ToArray();
        Assert.Single(created);                                            // no second camera created
        Assert.Equal(new Vector2(42, 42), created[0].Get<TransformComponent>().Position);
        Assert.Equal(1.25f, created[0].Get<CameraComponent>().Zoom, 4);
    }

    [Fact]
    public void Reader_EnsureContentlessScene_PlacesCameraAtOrigin()
    {
        using var world = new World();
        using var reader = new SceneReaderSystem(world, new SceneSerializer(Registry()), content: null!,
            loadTexture: _ => null!, ensureSingleCamera: true);
        world.Publish(new LoadSceneRequest(new SceneData())); // empty scene

        using var cams = world.GetEntities().With<CameraComponent>().AsSet();
        var created = cams.GetEntities().ToArray();
        Assert.Single(created);
        Assert.Equal(Vector2.Zero, created[0].Get<TransformComponent>().Position);
    }

    [Fact]
    public void Reader_PureRoundTripPath_DoesNotEnsureACamera()
    {
        // ensureSingleCamera defaults false → a serialization-fidelity load adds NO camera entity.
        using var authorWorld = new World();
        Prop(authorWorld, new Vector2(0, 0));
        var scene = new SceneWriter(new SceneSerializer(Registry())).BuildScene(authorWorld);

        using var world = new World();
        using var reader = new SceneReaderSystem(world, new SceneSerializer(Registry()), content: null!,
            loadTexture: _ => null!);
        world.Publish(new LoadSceneRequest(scene));

        using var cams = world.GetEntities().With<CameraComponent>().AsSet();
        Assert.Empty(cams.GetEntities().ToArray());
    }

    // ─────────────────────── Camera-entity byte round-trip (canonical fixture) ───────────────────────

    [Fact]
    public void CameraEntity_RoundTrips_ByteFixedPoint()
    {
        using var world1 = new World();
        Prop(world1, new Vector2(10, 20));
        CameraEntity(world1, new Vector2(208, 108), rotation: 0.25f, zoom: 1.75f);

        var json1 = CanonicalJson.Serialize(new SceneWriter(new SceneSerializer(Registry())).BuildScene(world1));

        // Load json1 into a fresh world through the reader (re-tags roots + restores ids), then re-save.
        var scene = CanonicalJson.Deserialize<SceneData>(json1)!;
        using var world2 = new World();
        using var reader = new SceneReaderSystem(world2, new SceneSerializer(Registry()), content: null!,
            loadTexture: _ => null!, ensureSingleCamera: true); // idempotent — a camera is already present
        world2.Publish(new LoadSceneRequest(scene));

        var json2 = CanonicalJson.Serialize(new SceneWriter(new SceneSerializer(Registry())).BuildScene(world2));

        Assert.Equal(json1, json2);                        // save → load → save is a byte fixed point
        Assert.Contains("\"core.Camera\"", json1);         // the camera is an ordinary serialized entity
        Assert.Equal(3, CanonicalJson.Deserialize<SceneData>(json1)!.Version); // stamped v3
    }
}
