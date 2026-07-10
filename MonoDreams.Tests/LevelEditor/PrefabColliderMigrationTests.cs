#nullable enable
using System.Linq;
using System.Text.Json;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.Undo;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// THE USER'S WIP PREFAB-MIGRATION READINESS (CE-D, item 6). The user's untracked <c>house.mdprefab</c>
/// is a legacy version-1 prefab with a box collider on the sprite root <c>house</c> and a convex collider
/// on the sprite child <c>House2</c> (at a local offset + sub-1 scale). This proves the
/// <c>monodreams migrate-colliders</c> core (<see cref="ColliderMigration"/>) handles a <c>.mdprefab</c>
/// input INCLUDING the prefab-specific wrinkle — a migrated prefab must still satisfy the one-root rule:
/// each embedded collider becomes a NEW collider CHILD entity (the box parents to the root, the convex to
/// <c>House2</c>), and the result loads through <see cref="PrefabData.FromScene"/> (which refuses a
/// multi-root prefab) AND expands + places WORLD-CORRECT through the real <see cref="PrefabExpander"/> +
/// <see cref="CreateInstanceCommand"/> (the class of bug PF-G patched, now on a migrated prefab).
///
/// EQUIVALENT hand-built fixtures — this NEVER reads the user's files. Pure logic: canonical version-1
/// JSON + the real migrator/reader/expander; no GraphicsDevice, no user files.
/// </summary>
public class PrefabColliderMigrationTests
{
    private const string Box = EngineComponentSerializers.BoxColliderKey;
    private const string Convx = EngineComponentSerializers.ConvexColliderKey;
    private const string Xf = EngineComponentSerializers.TransformKey;
    private const string Ei = EngineComponentSerializers.EntityInfoKey;
    private const string Spr = EngineComponentSerializers.SpriteInfoKey;

    private static JsonElement El(object v) => CanonicalJson.SerializeToElement(v);
    private static JsonElement Transform(float x, float y, float scale = 1f) =>
        El(new { position = new[] { x, y }, rotation = 0f, scale = new[] { scale, scale }, origin = new[] { 0f, 0f } });
    private static JsonElement Info(string type, string? name = null) => El(new { type, name });
    private static JsonElement BoxBounds(int x, int y, int w, int h, bool passive = true) =>
        El(new { bounds = new[] { x, y, w, h }, activeLayers = new[] { -1 }, passive, enabled = true });
    private static JsonElement Convex(float[][] verts, bool passive = true) =>
        El(new { modelVertices = verts, activeLayers = new[] { -1 }, passive, enabled = true, ignoreTransformRotation = true });
    private static JsonElement Sprite(string assetKey) => El(new
    {
        assetKey,
        source = new[] { 0, 0, 32, 32 },
        size = new[] { 32f, 32f },
        color = new byte[] { 255, 255, 255, 255 },
        origin = new[] { 16f, 32f },
        offset = new[] { 0f, 0f },
        target = 0, // RenderTargetID.Main (enums serialize as numbers under CanonicalJson.Options)
        layerDepth = 0.5f,
        ySortOffset = 0f,
        ySortDepthBias = 0f,
    });

    private static SceneEntityData Entity(int? id, int? parent, params (string Key, JsonElement Value)[] comps)
    {
        var e = new SceneEntityData { Id = id, Parent = parent };
        foreach (var (k, v) in comps) e.Components[k] = v;
        return e;
    }

    /// <summary>The user's house shape as a canonical version-1 <c>.mdprefab</c>: a sprite ROOT carrying a
    /// footprint BOX (bounds), and a sprite CHILD <c>House2</c> (local offset + 0.5 scale) carrying a
    /// CONVEX — both embedded on their (visual) owner, the pre-CE shape.</summary>
    private static string V1HousePrefabJson()
    {
        var scene = new SceneData { Version = 1 };
        // Root house: sprite + footprint box (bounds → a feet-anchored 32×8, centre (0,-4)).
        scene.Entities.Add(Entity(0, null,
            (Ei, Info("house", "house")),
            (Spr, Sprite("file:Island/House.png")),
            (Box, BoxBounds(-16, -8, 32, 8)),
            (Xf, Transform(0, 0))));
        // Child House2: sprite + convex, at local (-7,-40) scale 0.5.
        scene.Entities.Add(Entity(null, 0,
            (Spr, Sprite("file:Island/House2.png")),
            (Convx, Convex(new[] { new[] { 0f, 0f }, new[] { 20f, 0f }, new[] { 10f, 15f } })),
            (Xf, Transform(-7, -40, scale: 0.5f))));
        return CanonicalJson.Serialize(scene);
    }

    private static ComponentSerializerRegistry NewRegistry()
    {
        var r = new ComponentSerializerRegistry();
        r.RegisterEngineComponents();
        return r;
    }

    [Fact]
    public void MigratedHousePrefab_SatisfiesOneRoot_LoadsThroughPrefabData()
    {
        var result = ColliderMigration.Migrate(V1HousePrefabJson(), "house.mdprefab");

        // Both embedded colliders moved onto NEW child collider entities (the box off the visual root, the
        // convex off the visual child) — two visual owners, so two colliders relocate.
        Assert.True(result.Changed);
        Assert.Equal(2, result.CollidersMovedToChild);
        Assert.Equal(0, result.BoxesReshapedInPlace);

        var scene = CanonicalJson.Deserialize<SceneData>(result.Json)!;
        Assert.Equal(2, scene.Version);
        Assert.Equal(4, scene.Entities.Count); // house + box-child + House2 + convex-child

        // THE PREFAB WRINKLE: exactly one root survives (PrefabData.FromScene refuses a multi-root prefab).
        var prefab = PrefabData.FromScene("house", scene);
        var root = prefab.Root;
        Assert.True(root.Components.ContainsKey(Spr));          // the root is still the sprite house …
        Assert.False(root.Components.ContainsKey(Box));         // … with the box stripped off it
        Assert.Null(root.Parent);                               // the single root

        // The box became a CHILD of the root (index RootIndex); the convex a child of House2 (a grandchild).
        var boxChild = scene.Entities.Single(e =>
            e.Components.ContainsKey(Box) && !e.Components.ContainsKey(Spr));
        Assert.Equal(prefab.RootIndex, boxChild.Parent); // "the new collider child parents to the root"

        var house2 = scene.Entities.Single(e =>
            e.Components.ContainsKey(Spr) && e.Parent == prefab.RootIndex);
        var convexChild = scene.Entities.Single(e =>
            e.Components.ContainsKey(Convx) && !e.Components.ContainsKey(Spr));
        Assert.Equal(scene.Entities.IndexOf(house2), convexChild.Parent);
        // Convex verts copied verbatim (no re-basing — pre-mortem #3).
        Assert.Equal(0f, convexChild.Components[Convx].GetProperty("modelVertices")[0][0].GetSingle());
    }

    [Fact]
    public void MigratedHousePrefab_MigrateLoadSave_IsAByteFixedPoint()
    {
        var migrated = ColliderMigration.Migrate(V1HousePrefabJson(), "house.mdprefab").Json;

        // Load it as a prefab, then re-serialize: the migrated bytes are a canonical fixed point (so the
        // user's migrated prefab does not churn on the next editor save — pre-mortem #3, on a .mdprefab).
        var scene = CanonicalJson.Deserialize<SceneData>(migrated)!;
        var prefab = PrefabData.FromScene("house", scene);
        Assert.Equal(migrated, CanonicalJson.Serialize(prefab.Scene));

        // And re-migrating the migrator's own output is an idempotent no-op.
        var again = ColliderMigration.Migrate(migrated, "house.mdprefab");
        Assert.True(again.AlreadyCurrent);
        Assert.Equal(migrated, again.Json);
    }

    [Fact]
    public void MigratedHousePrefab_ExpandsAndPlacesWorldCorrect()
    {
        var scene = CanonicalJson.Deserialize<SceneData>(
            ColliderMigration.Migrate(V1HousePrefabJson(), "house.mdprefab").Json)!;
        var prefab = PrefabData.FromScene("house", scene);

        var serializer = new SceneSerializer(NewRegistry());
        var expander = new PrefabExpander(serializer,
            id => id == "house" ? prefab : null, loadTexture: _ => null!);

        using var world = new World();
        var history = new EditorHistory(world);
        var place = new Vector2(500, 300);
        history.Push(new CreateInstanceCommand(expander, "house", place));

        var root = Single<PrefabInstanceComponent>(world);
        Assert.Equal(place, root.Get<TransformComponent>().WorldPosition); // root placed at the cursor

        // The convex collider entity (a grandchild: root → House2 → convex-child) sits at its WORLD pose,
        // the placement folded in — NOT at a parent-relative local offset (the PF-G class of bug, now on a
        // migrated prefab's child collider).
        var convexEntity = Single<ConvexColliderComponent>(world);
        var t = convexEntity.Get<TransformComponent>();
        Assert.Equal(new Vector2(493, 260), t.WorldPosition); // place + House2 local (-7,-40)
        Assert.Equal(new Vector2(0.5f, 0.5f), t.WorldScale);

        var convex = convexEntity.Get<ConvexColliderComponent>();
        convex.UpdateWorldVertices(t);
        Assert.Equal(new Vector2(493, 260), convex.WorldVertices[0]);     // (0,0)*0.5 + (493,260)
        Assert.Equal(new Vector2(503, 260), convex.WorldVertices[1]);     // (20,0)*0.5 + (493,260)
        Assert.Equal(new Vector2(498, 267.5f), convex.WorldVertices[2]);  // (10,15)*0.5 + (493,260)

        // The box collider entity (a child of the root) is world-correct too: root (500,300) + its local
        // bounds-centre (0,-4) → world (500,296), the footprint's authored world position.
        var boxEntity = Single<BoxColliderComponent>(world);
        Assert.Equal(new Vector2(500, 296), boxEntity.Get<TransformComponent>().WorldPosition);
        Assert.Equal(new Vector2(32, 8), boxEntity.Get<BoxColliderComponent>().Size);
    }

    private static Entity Single<T>(World world)
    {
        using var set = world.GetEntities().With<T>().AsSet();
        foreach (var e in set.GetEntities()) return e;
        return default;
    }
}
