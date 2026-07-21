#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using MonoDreams.LevelEditor.Serialization;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the CM <b>camera migrator</b> (<see cref="CameraMigration"/>, the v2→v3 lift in the umbrella
/// <c>monodreams migrate</c> chain): the <c>camera</c>-block → <c>core.Camera</c>-entity transform (position
/// / rotation / zoom copied verbatim, no drift), the camera-less default at the origin, the block-dropped
/// case when a camera entity already exists, the prefab version-bump-only rule, idempotence, and loud
/// failure on unparseable input. Pure logic — hand-built canonical v2 fixtures, no <c>GraphicsDevice</c>.
/// </summary>
public class CameraMigrationTests
{
    private static JsonElement El(object value) => CanonicalJson.SerializeToElement(value);
    private static JsonElement Transform(float x, float y) =>
        El(new { position = new[] { x, y }, rotation = 0f, scale = new[] { 1f, 1f }, origin = new[] { 0f, 0f } });
    private static JsonElement Info(string type, string name) => El(new { type, name });

    private const string Cam = CameraMigration.CameraKey;
    private const string Xf = CameraMigration.TransformKey;
    private const string Ei = CameraMigration.EntityInfoKey;

    private static SceneEntityData Entity(int? id, int? parent, params (string Key, JsonElement Value)[] comps)
    {
        var e = new SceneEntityData { Id = id, Parent = parent };
        foreach (var (k, v) in comps) e.Components[k] = v;
        return e;
    }

    // ---- Keys + target version must not drift from the engine serializer ----

    [Fact]
    public void MigratorKeys_MatchEngineSerializerKeys()
    {
        Assert.Equal(EngineComponentSerializers.CameraKey, CameraMigration.CameraKey);
        Assert.Equal(EngineComponentSerializers.TransformKey, CameraMigration.TransformKey);
        Assert.Equal(EngineComponentSerializers.EntityInfoKey, CameraMigration.EntityInfoKey);
        Assert.Equal("Camera", CameraMigration.CameraEntityType);
        // The camera lift targets the CURRENT version (v3), unlike the collider lift's v2.
        Assert.Equal(3, CameraMigration.TargetVersion);
        Assert.Equal(SceneData.CurrentVersion, CameraMigration.TargetVersion);
    }

    // ---- A v2 scene WITH a camera block lifts it into a camera entity (verbatim pos/rot/zoom) ----

    [Fact]
    public void V2WithCameraBlock_LiftsToCameraEntity_VerbatimPositionRotationZoom()
    {
        var scene = new SceneData { Version = 2 };
        scene.Camera = new SceneCameraData { Position = new[] { 320.5f, -180.25f }, Zoom = 4f, Rotation = 0.75f };
        scene.Entities.Add(Entity(0, null, (Ei, Info("Prop", "grass")), (Xf, Transform(0, 0))));

        var result = CameraMigration.Migrate(CanonicalJson.Serialize(scene), "level.mdscene");

        Assert.True(result.Changed);
        Assert.True(result.CameraBlockLifted);
        Assert.False(result.DefaultCameraAdded);

        var migrated = CanonicalJson.Deserialize<SceneData>(result.Json)!;
        Assert.Equal(3, migrated.Version);
        Assert.Null(migrated.Camera);                       // the block is gone
        Assert.Equal(2, migrated.Entities.Count);           // prop + the new camera entity

        var cam = migrated.Entities.Single(e => e.Components.ContainsKey(Cam));
        Assert.Equal(1, cam.Id);                            // max root id (0) + 1 → sorts last
        Assert.Equal(4f, cam.Components[Cam].GetProperty("zoom").GetSingle(), 4);
        Assert.Equal("Camera", cam.Components[Ei].GetProperty("type").GetString());
        Assert.False(cam.Components[Ei].TryGetProperty("name", out _)); // name null-omitted
        var pos = cam.Components[Xf].GetProperty("position").EnumerateArray().Select(v => v.GetSingle()).ToArray();
        Assert.Equal(new[] { 320.5f, -180.25f }, pos);      // verbatim — no drift
        Assert.Equal(0.75f, cam.Components[Xf].GetProperty("rotation").GetSingle(), 4);
    }

    // ---- A camera-less v2 scene gets the uniformly-explicit default camera at the origin ----

    [Fact]
    public void V2CameraLess_AddsDefaultCameraAtOrigin()
    {
        var scene = new SceneData { Version = 2 };
        scene.Entities.Add(Entity(0, null, (Ei, Info("Prop", "grass")), (Xf, Transform(10, 20))));
        scene.Entities.Add(Entity(1, null, (Ei, Info("Prop", "stone")), (Xf, Transform(30, 40))));

        var result = CameraMigration.Migrate(CanonicalJson.Serialize(scene), "level.mdscene");

        Assert.True(result.Changed);
        Assert.True(result.DefaultCameraAdded);
        Assert.False(result.CameraBlockLifted);

        var migrated = CanonicalJson.Deserialize<SceneData>(result.Json)!;
        Assert.Equal(3, migrated.Version);
        var cam = migrated.Entities.Single(e => e.Components.ContainsKey(Cam));
        Assert.Equal(2, cam.Id);                            // after grass(0) + stone(1)
        Assert.Equal(1f, cam.Components[Cam].GetProperty("zoom").GetSingle(), 4);
        var pos = cam.Components[Xf].GetProperty("position").EnumerateArray().Select(v => v.GetSingle()).ToArray();
        Assert.Equal(new[] { 0f, 0f }, pos);
    }

    // ---- A block AND an existing camera entity → the block is dropped, no second camera ----

    [Fact]
    public void V2WithBlockAndExistingCameraEntity_DropsBlock_NoSecondCamera()
    {
        var scene = new SceneData { Version = 2 };
        scene.Camera = new SceneCameraData { Position = new[] { 9f, 9f }, Zoom = 9f };
        scene.Entities.Add(Entity(0, null, (Ei, Info("Camera", "MainCam")), (Xf, Transform(1, 2)), (Cam, El(new { zoom = 2f }))));

        var result = CameraMigration.Migrate(CanonicalJson.Serialize(scene), "level.mdscene");

        Assert.True(result.CameraBlockDropped);
        Assert.False(result.CameraBlockLifted);
        Assert.False(result.DefaultCameraAdded);

        var migrated = CanonicalJson.Deserialize<SceneData>(result.Json)!;
        Assert.Null(migrated.Camera);
        Assert.Single(migrated.Entities.Where(e => e.Components.ContainsKey(Cam)));  // one camera, the entity
        Assert.Equal(2f, migrated.Entities[0].Components[Cam].GetProperty("zoom").GetSingle(), 4); // entity's zoom wins
    }

    // ---- A prefab gets a version bump only — never a camera ----

    [Fact]
    public void Prefab_VersionBumpOnly_NeverACamera()
    {
        var scene = new SceneData { Version = 2 };
        scene.Entities.Add(Entity(0, null, (Ei, Info("Prop", "root")), (Xf, Transform(0, 0))));

        var result = CameraMigration.Migrate(CanonicalJson.Serialize(scene), "thing.mdprefab", isPrefab: true);

        Assert.True(result.Changed);
        Assert.True(result.IsPrefab);
        Assert.False(result.CameraBlockLifted);
        Assert.False(result.DefaultCameraAdded);

        var migrated = CanonicalJson.Deserialize<SceneData>(result.Json)!;
        Assert.Equal(3, migrated.Version);
        Assert.Single(migrated.Entities);                                   // no camera added
        Assert.DoesNotContain(migrated.Entities, e => e.Components.ContainsKey(Cam));
    }

    [Fact]
    public void Prefab_WithStrayCameraBlock_DropsIt_AddsNoCamera()
    {
        var scene = new SceneData { Version = 2 };
        scene.Camera = new SceneCameraData { Position = new[] { 5f, 5f }, Zoom = 2f };
        scene.Entities.Add(Entity(0, null, (Ei, Info("Prop", "root")), (Xf, Transform(0, 0))));

        var result = CameraMigration.Migrate(CanonicalJson.Serialize(scene), "thing.mdprefab", isPrefab: true);

        var migrated = CanonicalJson.Deserialize<SceneData>(result.Json)!;
        Assert.Null(migrated.Camera);
        Assert.Single(migrated.Entities);
        Assert.DoesNotContain(migrated.Entities, e => e.Components.ContainsKey(Cam));
    }

    // ---- Idempotence: a v3 input is a byte-identical no-op (also for a re-migrate of the output) ----

    [Fact]
    public void AlreadyV3_IsANoOp_BytesUnchanged()
    {
        var scene = new SceneData(); // version defaults to 3
        scene.Entities.Add(Entity(0, null, (Ei, Info("Prop", "p")), (Xf, Transform(1, 2))));
        var v3 = CanonicalJson.Serialize(scene);

        var result = CameraMigration.Migrate(v3, "current.mdscene");
        Assert.True(result.AlreadyCurrent);
        Assert.False(result.Changed);
        Assert.Equal(v3, result.Json);

        // Double-migrate = single-migrate: the lift's own output re-migrates to a no-op.
        var v2 = new SceneData { Version = 2 };
        v2.Camera = new SceneCameraData { Position = new[] { 3f, 4f }, Zoom = 2f };
        v2.Entities.Add(Entity(0, null, (Ei, Info("Prop", "p")), (Xf, Transform(0, 0))));
        var once = CameraMigration.Migrate(CanonicalJson.Serialize(v2), "f.mdscene").Json;
        var twice = CameraMigration.Migrate(once, "f.mdscene");
        Assert.True(twice.AlreadyCurrent);
        Assert.Equal(once, twice.Json);
    }

    [Fact]
    public void UnparseableInput_ThrowsLoud()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => CameraMigration.Migrate("{ not json", "bad.mdscene"));
        Assert.Contains("bad.mdscene", ex.Message);
    }
}
