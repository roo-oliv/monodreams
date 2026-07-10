using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.Extension;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.EntityFactory;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Message;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the prefab expansion / compaction round-trip (wave PF-C, design §1 + pre-mortems #1, #2,
/// #7): the ONE <see cref="PrefabExpander"/> shared by the reader, the <see cref="PrefabFactory"/>, and
/// live <see cref="PrefabPropagation"/>. Covers expansion (root + prefab-owned children + whole-component
/// overrides + marker + re-tag + rehydration/DrawComponent), the diff-based compaction round-trip byte
/// fixed point, missing-prefab fail-loud, cycle refusal (save) + depth-cap (load), the factory channel,
/// and propagation with override preservation + the history-clear rule. Pure logic — hand-built
/// entities, in-memory scene snapshots (the reader's <c>LoadSceneRequest(SceneData)</c> path — no disk),
/// a null texture loader (we assert AssetKey + the DrawComponent pairing, not pixels).
/// </summary>
public class PrefabExpansionTests
{
    private static ComponentSerializerRegistry NewRegistry()
    {
        var r = new ComponentSerializerRegistry();
        r.RegisterEngineComponents();
        return r;
    }

    private static Func<string, PrefabData?> Source(params (string id, PrefabData data)[] prefabs)
    {
        var dict = prefabs.ToDictionary(p => p.id, p => p.data);
        return id => dict.TryGetValue(id, out var d) ? d : null;
    }

    /// <summary>Builds the "npc" prefab: a tagged root sprite (off-origin) + a BoxCollider + a child
    /// sprite. <paramref name="rootName"/> sets the root's EntityInfo name; <paramref name="withCollider"/>
    /// adds the (v2) root BoxCollider.</summary>
    private static PrefabData BuildNpcPrefab(string id = "npc", string rootName = "boldo", bool withCollider = false)
    {
        using var w = new World();
        var root = w.CreateEntity();
        root.Set(new SceneObjectComponent());
        root.Set(new EntityInfoComponent("NPC", rootName));
        root.Set(new TransformComponent(new Vector2(100, 50), rotation: 0.2f, scale: new Vector2(2, 2)));
        root.Set(new SpriteInfoComponent
        {
            AssetKey = "Atlas/npc",
            Source = new Rectangle(0, 0, 16, 16),
            Size = new Vector2(16, 16),
            Color = Color.White,
            Target = RenderTargetID.Main,
            LayerDepth = 0.5f,
        });
        if (withCollider)
            root.Set(new BoxColliderComponent(new Vector2(16, 8), new HashSet<int> { 1 }, passive: true));

        var child = w.CreateEntity();
        child.Set(new EntityInfoComponent("Prop", "head"));
        child.Set(new TransformComponent(new Vector2(0, -8)));
        child.Set(new SpriteInfoComponent
        {
            AssetKey = "Atlas/head",
            Source = new Rectangle(0, 0, 8, 8),
            Size = new Vector2(8, 8),
            Color = Color.White,
            Target = RenderTargetID.Main,
        });
        child.SetParent(root);

        var scene = new PrefabWriter(new SceneWriter(new SceneSerializer(NewRegistry()))).BuildPrefab(w, id);
        return PrefabData.FromScene(id, scene);
    }

    /// <summary>Expands one instance of <paramref name="prefabId"/> into a scene world, tags it, places it
    /// at <paramref name="pos"/>, applies an EntityInfo override, and compacts to a scene via the writer.</summary>
    private static SceneData BuildSceneWithInstance(Func<string, PrefabData?> source, string prefabId,
        Vector2 pos, string overrideName)
    {
        using var world = new World();
        var serializer = new SceneSerializer(NewRegistry());
        var expander = new PrefabExpander(serializer, source);

        var root = expander.Instantiate(world, prefabId);
        root.Set(new SceneObjectComponent());            // a scene member (the reader/factory would tag this)
        root.Get<TransformComponent>().Position = pos;   // place the instance
        root.Set(new EntityInfoComponent("NPC", overrideName)); // whole-component override

        return new SceneWriter(serializer, source).BuildScene(world);
    }

    private static int CountChildren(World world, Entity parent)
    {
        var n = 0;
        using var set = world.GetEntities().With<ChildOfComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.Get<ChildOfComponent>().Parent.Equals(parent)) n++;
        return n;
    }

    private static Entity SingleInstanceRoot(World world, string prefabId)
    {
        Entity found = default;
        var count = 0;
        using var set = world.GetEntities().With<PrefabInstanceComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.Get<PrefabInstanceComponent>().PrefabId == prefabId) { found = e; count++; }
        Assert.Equal(1, count);
        return found;
    }

    // ---------------------------------------------------------------- Expansion via the reader

    [Fact]
    public void Reader_ExpandsInstance_RootChildrenOverridesMarkerRetagRehydrate()
    {
        var source = Source(("npc", BuildNpcPrefab()));
        var scene = BuildSceneWithInstance(source, "npc", new Vector2(200, 100), overrideName: "renamed");

        using var world = new World();
        var serializer = new SceneSerializer(NewRegistry());
        var expander = new PrefabExpander(serializer, source, loadTexture: _ => null);
        using var reader = new SceneReaderSystem(world, serializer, content: null, loadTexture: _ => null,
            prefabExpander: expander);

        world.Publish(new LoadSceneRequest(scene));

        var root = SingleInstanceRoot(world, "npc");
        Assert.Equal("renamed", root.Get<EntityInfoComponent>().Name);            // override applied
        Assert.Equal("Atlas/npc", root.Get<SpriteInfoComponent>().AssetKey);      // inherited from the prefab
        Assert.Equal(new Vector2(200, 100), root.Get<TransformComponent>().Position); // instance placement
        Assert.True(root.Has<SceneObjectComponent>());                            // re-tagged a scene root
        Assert.True(root.Has<SceneEntityIdComponent>());                          // scene id restored
        Assert.True(root.Has<DrawComponent>());                                   // sprite DrawComponent restored
        Assert.Equal(1, CountChildren(world, root));                              // the prefab-owned child came back
    }

    [Fact]
    public void ExpandScene_IsByteStableFixedPoint_SaveLoadSave()
    {
        var source = Source(("npc", BuildNpcPrefab()));
        var scene1 = BuildSceneWithInstance(source, "npc", new Vector2(200, 100), overrideName: "renamed");
        var json1 = CanonicalJson.Serialize(scene1);

        // The compact instance entry: prefab id + Transform + only the changed EntityInfo (inherited omitted).
        Assert.Contains("\"prefab\": \"npc\"", json1);
        Assert.Contains(EngineComponentSerializers.EntityInfoKey, json1);
        Assert.DoesNotContain(EngineComponentSerializers.SpriteInfoKey, json1); // inherited → omitted

        using var world = new World();
        var serializer = new SceneSerializer(NewRegistry());
        var expander = new PrefabExpander(serializer, source, loadTexture: _ => null);
        using var reader = new SceneReaderSystem(world, serializer, content: null, loadTexture: _ => null,
            prefabExpander: expander);
        world.Publish(new LoadSceneRequest(scene1));

        var scene2 = new SceneWriter(serializer, source).BuildScene(world);
        var json2 = CanonicalJson.Serialize(scene2);

        Assert.Equal(json1, json2); // load → save reproduces the exact bytes (pre-mortem #1)
    }

    [Fact]
    public void Reader_MissingPrefab_FailsLoud()
    {
        var source = Source(("npc", BuildNpcPrefab()));
        var scene = BuildSceneWithInstance(source, "npc", new Vector2(0, 0), overrideName: "x");
        scene.Entities[0].Prefab = "ghost"; // now references a prefab that does not resolve

        using var world = new World();
        var serializer = new SceneSerializer(NewRegistry());
        var expander = new PrefabExpander(serializer, source, loadTexture: _ => null);
        using var reader = new SceneReaderSystem(world, serializer, content: null, loadTexture: _ => null,
            prefabExpander: expander);

        var ex = Assert.Throws<InvalidOperationException>(() => world.Publish(new LoadSceneRequest(scene)));
        Assert.Contains("ghost", ex.Message);
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void Deserialize_PrefabEntry_NoExpander_FailsLoud()
    {
        var source = Source(("npc", BuildNpcPrefab()));
        var scene = BuildSceneWithInstance(source, "npc", new Vector2(0, 0), overrideName: "x");

        using var world = new World();
        var serializer = new SceneSerializer(NewRegistry());
        // No expander passed → a prefab entry cannot be reconstructed → fail loud (never a half-entity).
        var ex = Assert.Throws<InvalidOperationException>(() => serializer.Deserialize(world, scene));
        Assert.Contains("no prefab expander", ex.Message);
    }

    // ---------------------------------------------------------------- Cycles

    [Fact]
    public void Expansion_SelfReferencingPrefab_CycleRefusedAtLoad()
    {
        // Hand-build a prefab that references itself (bypassing the writer's save-time refusal) to prove
        // the LOAD-time cap: root (0) + a nested self-instance child (1).
        var registry = NewRegistry();
        var baseline = BuildNpcPrefab("loop").Scene;
        var loop = new SceneData();
        loop.Entities.Add(baseline.Entities[0]); // the root (index 0)
        loop.Entities.Add(new SceneEntityData
        {
            Prefab = "loop",                       // a nested instance of ITSELF
            Parent = 0,
            Components = new Dictionary<string, JsonElement>
            {
                [EngineComponentSerializers.TransformKey] = baseline.Entities[0].Components[EngineComponentSerializers.TransformKey],
            },
        });

        var source = Source(("loop", PrefabData.FromScene("loop", loop)));
        using var world = new World();
        var expander = new PrefabExpander(new SceneSerializer(registry), source, loadTexture: _ => null);

        var ex = Assert.Throws<InvalidOperationException>(() => expander.Instantiate(world, "loop"));
        Assert.Contains("cycle", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public void PrefabWriter_RefusesTransitiveCycle()
    {
        // a → (contains an instance of) b → (contains an instance of) a.  Saving 'a' must refuse.
        var aScene = BuildNpcPrefab("a").Scene;
        var bScene = BuildNpcPrefab("b").Scene;

        // Give 'b' a nested instance of 'a' (b → a).
        bScene.Entities.Add(new SceneEntityData
        {
            Prefab = "a",
            Parent = 0,
            Components = new Dictionary<string, JsonElement>
            {
                [EngineComponentSerializers.TransformKey] = bScene.Entities[0].Components[EngineComponentSerializers.TransformKey],
            },
        });

        var source = Source(("a", PrefabData.FromScene("a", aScene)), ("b", PrefabData.FromScene("b", bScene)));

        // Build a live world for 'a' that contains an instance of 'b' (a → b → a).
        using var world = new World();
        var serializer = new SceneSerializer(NewRegistry());
        var expander = new PrefabExpander(serializer, source);
        var root = expander.Instantiate(world, "a");
        root.Set(new SceneObjectComponent());
        var bInstance = expander.Instantiate(world, "b");
        bInstance.Set(new SceneObjectComponent());
        bInstance.SetParent(root);

        var writer = new PrefabWriter(new SceneWriter(serializer, source));
        var ex = Assert.Throws<InvalidOperationException>(() => writer.BuildPrefab(world, "a", source));
        Assert.Contains("cannot contain an instance of itself", ex.Message);
    }

    // ---------------------------------------------------------------- Factory channel

    [Fact]
    public void Factory_SpawnViaEntitySpawnRequest_IsTheSameExpansion()
    {
        var source = Source(("npc", BuildNpcPrefab()));
        using var world = new World();
        var serializer = new SceneSerializer(NewRegistry());
        var expander = new PrefabExpander(serializer, source, loadTexture: _ => null);
        var factory = new PrefabFactory(expander);

        var root = factory.CreateEntity(world, new EntitySpawnRequest("prefab:npc", new Vector2(300, 150)));

        Assert.True(root.IsAlive);
        Assert.Equal("npc", root.Get<PrefabInstanceComponent>().PrefabId);   // linked instance marker
        Assert.Equal(new Vector2(300, 150), root.Get<TransformComponent>().Position); // placed at the request
        Assert.True(root.Has<SceneObjectComponent>());                       // a scene object
        Assert.True(root.Has<DrawComponent>());                              // finished (DrawComponent restored)
        Assert.Equal(1, CountChildren(world, root));                         // the prefab-owned child came back
    }

    [Fact]
    public void Factory_UnknownPrefabId_WarnsAndDrops_NoThrow()
    {
        var source = Source(("npc", BuildNpcPrefab()));
        using var world = new World();
        var serializer = new SceneSerializer(NewRegistry());
        var expander = new PrefabExpander(serializer, source, loadTexture: _ => null);
        var factory = new PrefabFactory(expander);

        var root = factory.CreateEntity(world, new EntitySpawnRequest("prefab:ghost", Vector2.Zero));

        Assert.False(root.IsAlive); // dropped (warn-and-drop convention), no exception
        using var set = world.GetEntities().With<PrefabInstanceComponent>().AsSet();
        Assert.Empty(set.GetEntities().ToArray());
    }

    [Fact]
    public void EntitySpawnSystem_PrefixDispatch_RoutesPrefabRequestsToTheFactory()
    {
        var source = Source(("npc", BuildNpcPrefab()));
        using var world = new World();
        var serializer = new SceneSerializer(NewRegistry());
        var expander = new PrefabExpander(serializer, source, loadTexture: _ => null);

        using var spawnSystem = new MonoDreams.System.EntitySpawn.EntitySpawnSystem(
            world, content: null, renderTargets: new Dictionary<RenderTargetID, RenderTarget2D>());
        spawnSystem.RegisterEntityFactoryPrefix(PrefabFactory.IdentifierPrefix, new PrefabFactory(expander));

        world.Publish(new EntitySpawnRequest("prefab:npc", new Vector2(10, 20)));

        var root = SingleInstanceRoot(world, "npc");
        Assert.Equal(new Vector2(10, 20), root.Get<TransformComponent>().Position);
    }

    // ---------------------------------------------------------------- Propagation

    [Fact]
    public void Propagation_ReExpandsInstances_PreservingOverrides_AndClearsHistory()
    {
        var v1 = BuildNpcPrefab("npc", rootName: "boldo-v1", withCollider: false);
        var v2 = BuildNpcPrefab("npc", rootName: "boldo-v2", withCollider: true); // the new definition (adds a collider)

        using var world = new World();
        var serializer = new SceneSerializer(NewRegistry());

        // Expand an instance from v1 and give it an EntityInfo override.
        var expanderV1 = new PrefabExpander(serializer, Source(("npc", v1)), loadTexture: _ => null);
        var instance = expanderV1.Instantiate(world, "npc");
        instance.Set(new SceneObjectComponent());
        instance.Get<TransformComponent>().Position = new Vector2(400, 250);
        instance.Set(new EntityInfoComponent("NPC", "custom-name")); // the override to preserve
        Assert.False(instance.Has<BoxColliderComponent>());          // v1 root has no collider

        var history = new EditorHistory(world);
        history.Push(new NoOpCommand()); // something on the undo stack to prove it is cleared
        Assert.True(history.CanUndo);

        // "Save" v2: the expander's source now resolves the NEW definition.
        var expanderV2 = new PrefabExpander(serializer, Source(("npc", v2)), loadTexture: _ => null);
        var rebuilt = PrefabPropagation.ReExpand(world, "npc", oldPrefab: v1, expanderV2, serializer.Registry, history);

        Assert.Equal(1, rebuilt);
        var root = SingleInstanceRoot(world, "npc");
        Assert.Equal("custom-name", root.Get<EntityInfoComponent>().Name); // override preserved across re-expansion
        Assert.True(root.Has<BoxColliderComponent>());                     // v2's new content propagated in
        Assert.Equal(new Vector2(400, 250), root.Get<TransformComponent>().Position); // instance placement preserved

        // The Restart rule: history cleared + scene dirty.
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.True(history.IsDirty);
    }

    [Fact]
    public void Propagation_NoOpenInstances_LeavesHistoryUntouched()
    {
        var prefab = BuildNpcPrefab("npc");
        using var world = new World();
        var serializer = new SceneSerializer(NewRegistry());
        var expander = new PrefabExpander(serializer, Source(("npc", prefab)), loadTexture: _ => null);

        // A world with NO instance of "npc"; a dirty-ish history that must survive untouched.
        var history = new EditorHistory(world);
        history.Push(new NoOpCommand());
        var wasDirty = history.IsDirty;

        var rebuilt = PrefabPropagation.ReExpand(world, "npc", oldPrefab: prefab, expander, serializer.Registry, history);

        Assert.Equal(0, rebuilt);
        Assert.True(history.CanUndo);          // untouched
        Assert.Equal(wasDirty, history.IsDirty);
    }

    /// <summary>A do-nothing command so the history has an undoable entry to prove the Restart clear.</summary>
    private sealed class NoOpCommand : IEditorCommand
    {
        public void Apply(World world) { }
        public void Revert(World world) { }
    }
}
