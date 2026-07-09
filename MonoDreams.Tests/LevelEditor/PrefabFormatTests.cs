using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.Extension;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the <b>write side</b> of the <c>.mdprefab</c> format (wave PF-C, design §1): the reused
/// <see cref="SceneData"/> schema with the prefab rules (exactly ONE root, root Transform normalized
/// to origin, no camera), the <b>additive</b> <c>prefab</c> field (an ordinary scene is byte-identical),
/// the diff-based instance compaction (<see cref="PrefabDiff"/> — inherited omitted, changed / added
/// kept, Transform always), and the membership exclusion of prefab-owned instance children
/// (<see cref="SceneWriter.CollectMembership"/>). Pure logic — hand-built entities, no disk, no
/// GraphicsDevice.
/// </summary>
public class PrefabFormatTests
{
    private static ComponentSerializerRegistry NewRegistry()
    {
        var r = new ComponentSerializerRegistry();
        r.RegisterEngineComponents();
        return r;
    }

    private static SceneSerializer NewSerializer() => new(NewRegistry());

    /// <summary>A tagged sprite root at <paramref name="pos"/> (a save-root).</summary>
    private static Entity MakeRoot(World w, string name, Vector2 pos)
    {
        var e = w.CreateEntity();
        e.Set(new SceneObjectComponent());
        e.Set(new EntityInfoComponent("Prop", name));
        e.Set(new TransformComponent(pos, rotation: 0.25f, scale: new Vector2(2f, 2f)));
        e.Set(new SpriteInfoComponent
        {
            AssetKey = "Atlas/" + name,
            Source = new Rectangle(0, 0, 16, 16),
            Size = new Vector2(16, 16),
            Color = Color.White,
            Target = RenderTargetID.Main,
            LayerDepth = 0.5f,
        });
        return e;
    }

    private static Entity MakeChild(World w, Entity parent, string name, Vector2 localPos)
    {
        var e = w.CreateEntity();
        e.Set(new EntityInfoComponent("Prop", name));
        e.Set(new TransformComponent(localPos));
        e.SetParent(parent);
        return e;
    }

    private static Vector2 ReadPosition(JsonElement transform)
    {
        var p = transform.GetProperty("position");
        return new Vector2(p[0].GetSingle(), p[1].GetSingle());
    }

    // ---------------------------------------------------------------- PrefabData validation

    [Fact]
    public void PrefabData_OneRoot_ResolvesTheRootIndex()
    {
        using var world = new World();
        var root = MakeRoot(world, "npc", new Vector2(3, 3));
        MakeChild(world, root, "head", new Vector2(0, -8));

        var scene = NewSerializer().Serialize(SceneWriter.CollectOrderedMembership(world));
        var prefab = PrefabData.FromScene("npc", scene);

        Assert.Null(scene.Entities[prefab.RootIndex].Parent); // the root is a top-level entry
        Assert.Equal("npc", prefab.Id);
    }

    [Fact]
    public void PrefabData_MultipleRoots_ThrowsLoud()
    {
        using var world = new World();
        MakeRoot(world, "a", new Vector2(1, 1));
        MakeRoot(world, "b", new Vector2(2, 2)); // a SECOND root → not a valid prefab

        var scene = NewSerializer().Serialize(SceneWriter.CollectOrderedMembership(world));
        var ex = Assert.Throws<InvalidOperationException>(() => PrefabData.FromScene("multi", scene));
        Assert.Contains("more than one root", ex.Message);
    }

    // ---------------------------------------------------------------- PrefabWriter format rules

    [Fact]
    public void PrefabWriter_NormalizesRootToOrigin_ChildrenKeepLocalOffsets()
    {
        using var world = new World();
        var root = MakeRoot(world, "npc", new Vector2(100, 50)); // off-origin
        MakeChild(world, root, "head", new Vector2(10, -8));     // local offset

        var prefab = new PrefabWriter(new SceneWriter(NewSerializer())).BuildPrefab(world, "npc");
        var rootIndex = PrefabData.FromScene("npc", prefab).RootIndex;

        // Root position normalized to origin; rotation / scale preserved.
        var rootTransform = prefab.Entities[rootIndex].Components[EngineComponentSerializers.TransformKey];
        Assert.Equal(Vector2.Zero, ReadPosition(rootTransform));
        Assert.Equal(0.25f, rootTransform.GetProperty("rotation").GetSingle());

        // The child keeps its LOCAL offset (positions are parent-relative — normalization touches only the root).
        var childEntry = prefab.Entities.Single(e => e.Parent == rootIndex);
        Assert.Equal(new Vector2(10, -8), ReadPosition(childEntry.Components[EngineComponentSerializers.TransformKey]));
    }

    [Fact]
    public void PrefabWriter_OmitsCamera()
    {
        using var world = new World();
        MakeRoot(world, "npc", new Vector2(5, 5));

        var prefab = new PrefabWriter(new SceneWriter(NewSerializer())).BuildPrefab(world, "npc");

        Assert.Null(prefab.Camera);
        Assert.DoesNotContain("\"camera\"", CanonicalJson.Serialize(prefab));
    }

    [Fact]
    public void PrefabWriter_RefusesMultipleRoots()
    {
        using var world = new World();
        MakeRoot(world, "a", new Vector2(1, 1));
        MakeRoot(world, "b", new Vector2(2, 2));

        var writer = new PrefabWriter(new SceneWriter(NewSerializer()));
        Assert.Throws<InvalidOperationException>(() => writer.BuildPrefab(world, "multi"));
    }

    [Fact]
    public void PrefabWriter_RefusesDirectCycle()
    {
        // A prefab world containing an instance of ITSELF (direct self-reference) → refused at save.
        using var world = new World();
        var root = MakeRoot(world, "boldo", new Vector2(0, 0));
        var selfInstance = world.CreateEntity();
        selfInstance.Set(new EntityInfoComponent("Prop", "self"));
        selfInstance.Set(new TransformComponent(new Vector2(5, 5)));
        selfInstance.Set(new PrefabInstanceComponent("boldo")); // instance of the prefab being saved
        selfInstance.SetParent(root);

        // A source that resolves "boldo" so compaction can run (it diffs against the prefab root).
        var dict = new Dictionary<string, SceneData>();
        Func<string, PrefabData?> source = id => dict.TryGetValue(id, out var s) ? PrefabData.FromScene(id, s) : null;
        // Seed a trivial "boldo" definition for the compaction diff of the self-instance.
        using (var seedWorld = new World())
        {
            MakeRoot(seedWorld, "boldo", Vector2.Zero);
            dict["boldo"] = new PrefabWriter(new SceneWriter(NewSerializer())).BuildPrefab(seedWorld, "boldo");
        }

        var writer = new PrefabWriter(new SceneWriter(NewSerializer(), source));
        var ex = Assert.Throws<InvalidOperationException>(() => writer.BuildPrefab(world, "boldo", source));
        Assert.Contains("cannot contain an instance of itself", ex.Message);
    }

    // ---------------------------------------------------------------- Additivity (ordinary scenes unchanged)

    [Fact]
    public void OrdinaryScene_OmitsThePrefabField_ByteAdditive()
    {
        using var world = new World();
        MakeRoot(world, "a", new Vector2(10, 20));
        MakeRoot(world, "b", new Vector2(30, 40));

        var json = CanonicalJson.Serialize(new SceneWriter(NewSerializer()).BuildScene(world));

        // The additive `prefab` field never appears for an ordinary (non-instance) scene — every
        // pre-prefab scene is byte-identical (CanonicalJson omits the null field).
        Assert.DoesNotContain("\"prefab\"", json);
    }

    [Fact]
    public void OrdinaryScene_RoundTripsWithNullPrefabField()
    {
        using var world = new World();
        MakeRoot(world, "a", new Vector2(1, 2));

        var scene = new SceneWriter(NewSerializer()).BuildScene(world);
        var json = CanonicalJson.Serialize(scene);
        var reparsed = CanonicalJson.Deserialize<SceneData>(json);

        Assert.All(reparsed!.Entities, e => Assert.Null(e.Prefab));
    }

    // ---------------------------------------------------------------- PrefabDiff (override detection)

    [Fact]
    public void PrefabDiff_KeepsTransform_OmitsInherited_KeepsChangedAndAdded()
    {
        var registry = NewRegistry();

        // A "prefab root" component set.
        using var world = new World();
        var prefabRoot = MakeRoot(world, "npc", Vector2.Zero);
        var prefabComponents = registry.SerializeEntity(prefabRoot).Components;

        // An "instance" whose EntityInfo differs (override), SpriteInfo identical (inherited), and which
        // adds a BoxCollider the prefab lacks (addition).
        var instance = MakeRoot(world, "npc", new Vector2(99, 99)); // different Transform (always kept)
        instance.Set(new EntityInfoComponent("Prop", "renamed"));   // OVERRIDE
        instance.Set(new BoxColliderComponent(new Rectangle(0, 0, 8, 8), new HashSet<int> { 1 })); // ADDITION
        var instanceComponents = registry.SerializeEntity(instance).Components;

        var overrides = PrefabDiff.ComputeOverrides(instanceComponents, prefabComponents);

        Assert.Contains(EngineComponentSerializers.TransformKey, overrides.Keys);   // always kept
        Assert.Contains(EngineComponentSerializers.EntityInfoKey, overrides.Keys);  // changed → kept
        Assert.Contains(EngineComponentSerializers.BoxColliderKey, overrides.Keys); // added → kept
        Assert.DoesNotContain(EngineComponentSerializers.SpriteInfoKey, overrides.Keys); // identical → inherited
    }

    [Fact]
    public void CanonicalEquals_TrueForEqualValues_FalseForDifferent()
    {
        var a = CanonicalJson.SerializeToElement(new { x = 1, y = 2f });
        var b = CanonicalJson.SerializeToElement(new { x = 1, y = 2f });
        var c = CanonicalJson.SerializeToElement(new { x = 1, y = 3f });

        Assert.True(CanonicalJson.CanonicalEquals(a, b));
        Assert.False(CanonicalJson.CanonicalEquals(a, c));
    }

    // ---------------------------------------------------------------- Membership excludes instance children

    [Fact]
    public void CollectMembership_IncludesInstanceRoot_ExcludesItsPrefabOwnedChildren()
    {
        using var world = new World();

        // A linked instance root with prefab-owned children (ordinary ChildOf descendants).
        var instanceRoot = world.CreateEntity();
        instanceRoot.Set(new SceneObjectComponent());
        instanceRoot.Set(new EntityInfoComponent("Prop", "npc-instance"));
        instanceRoot.Set(new TransformComponent(new Vector2(50, 50)));
        instanceRoot.Set(new PrefabInstanceComponent("npc"));

        var child = MakeChild(world, instanceRoot, "prefab-owned-head", new Vector2(0, -8));
        MakeChild(world, child, "prefab-owned-hat", new Vector2(0, -4)); // grandchild

        // A plain (non-instance) tagged root with its own child — its closure IS serialized.
        var plainRoot = MakeRoot(world, "plain", new Vector2(0, 0));
        var plainChild = MakeChild(world, plainRoot, "plain-child", new Vector2(4, 4));

        var members = SceneWriter.CollectMembership(world);

        Assert.Contains(instanceRoot, members);         // the instance ROOT is a scene member
        Assert.DoesNotContain(child, members);          // ...but its prefab-owned children are NOT
        Assert.Contains(plainRoot, members);            // a plain root's closure is unaffected
        Assert.Contains(plainChild, members);
    }
}
