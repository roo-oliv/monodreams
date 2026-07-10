using System;
using System.Linq;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Serialization;
using MonoDreams.Extension;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.Undo;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// PF-F capture reliability + feedback + naming. Repro-first over the user's REAL structure: the
/// <c>elephant-kid</c> family — root(sprite+collider+info+effect) → child(sprite+info+effect) →
/// grandchild(info), plus sibling children with colliders/dialogue-zones. Proves the shared
/// <see cref="PrefabCapture"/> helper captures the WHOLE subtree (both when the ROOT and when a CHILD
/// is selected), robustly finds the parentless root (never <c>created[0]</c>), refuses an empty capture
/// (the user's empty-shell symptom), and names the root; plus the instance-naming uniquifier series.
/// Pure logic — hand-built entities, no GraphicsDevice.
/// </summary>
public class PrefabCaptureTests
{
    private static ComponentSerializerRegistry NewRegistry()
    {
        var r = new ComponentSerializerRegistry();
        r.RegisterEngineComponents();
        r.RegisterGameComponents();
        return r;
    }

    private static SceneSerializer NewSerializer() => new(NewRegistry());

    private static void AddSprite(Entity e, string key) => e.Set(new SpriteInfoComponent
    {
        AssetKey = key,
        Source = new Rectangle(0, 0, 16, 16),
        Size = new Vector2(16, 16),
        Color = Color.White,
        Target = RenderTargetID.Main,
        LayerDepth = 0.5f,
    });

    /// <summary>Builds the 6-entity elephant-kid family (mirrors island3), returns the parentless root.</summary>
    private static Entity BuildElephantKid(World w)
    {
        var root = w.CreateEntity();
        root.Set(new EntityInfoComponent("Prop", "elephant-kid"));
        root.Set(new TransformComponent(new Vector2(300, 200)));
        AddSprite(root, "file:Island/elephant-kid.png");
        root.Set(new ConvexColliderComponent(new[]
            { new Vector2(-8, -8), new Vector2(8, -8), new Vector2(8, 8), new Vector2(-8, 8) }, passive: true));
        root.Set(new StopMotionEffect { CycleDuration = 0.4f });

        var shil = w.CreateEntity();
        shil.Set(new EntityInfoComponent("Prop", "elephant-kid-shilhouette"));
        shil.Set(new TransformComponent(new Vector2(0, -4)));
        AddSprite(shil, "file:Island/elephant-kid-shil.png");
        shil.Set(new StopMotionEffect());
        shil.SetParent(root);

        var shilIcon = w.CreateEntity();
        shilIcon.Set(new EntityInfoComponent("Icon", "elephant-kid-shilhouetteIcon"));
        shilIcon.Set(new TransformComponent(new Vector2(0, -8)));
        shilIcon.SetParent(shil);

        var dz1 = w.CreateEntity();
        dz1.Set(new EntityInfoComponent("talkzone", "dz_shil"));
        dz1.Set(new TransformComponent(new Vector2(0, 4)));
        dz1.Set(new BoxColliderComponent(new Vector2(48, 48), passive: true));
        dz1.Set(new DialogueZoneComponent("shil_talk", npcName: "Shil"));
        dz1.SetParent(shil);

        var kidIcon = w.CreateEntity();
        kidIcon.Set(new EntityInfoComponent("Icon", "elephant-kidIcon"));
        kidIcon.Set(new TransformComponent(new Vector2(0, -12)));
        kidIcon.SetParent(root);

        var dz2 = w.CreateEntity();
        dz2.Set(new EntityInfoComponent("talkzone", "dz_kid"));
        dz2.Set(new TransformComponent(new Vector2(0, 6)));
        dz2.Set(new BoxColliderComponent(new Vector2(48, 48), passive: true));
        dz2.Set(new DialogueZoneComponent("kid_talk", npcName: "Kid"));
        dz2.SetParent(root);

        return root;
    }

    // ── The capture story: the WHOLE family survives when the ROOT is selected ──────────────────────

    [Fact]
    public void Capture_ElephantKidFamily_ViaRoot_PreservesAllSixEntitiesAndComponents()
    {
        var serializer = NewSerializer();
        using var world = new World();
        var root = BuildElephantKid(world);

        var result = PrefabCapture.Build(world, root, "elephant-kid", serializer, prefabSource: null);

        Assert.True(result.Ok, result.Refusal);
        Assert.Equal(6, result.EntityCount);

        var prefab = PrefabData.FromScene("elephant-kid", result.Scene!);
        // Exactly one root, normalized to origin, carrying the root's full component stack.
        Assert.Equal(1, result.Scene!.Entities.Count(e => e.Parent == null));
        var rootEntry = prefab.Root;
        Assert.True(rootEntry.Components.ContainsKey(EngineComponentSerializers.TransformKey));
        Assert.True(rootEntry.Components.ContainsKey(EngineComponentSerializers.SpriteInfoKey));
        Assert.True(rootEntry.Components.ContainsKey(EngineComponentSerializers.ConvexColliderKey));
        Assert.True(rootEntry.Components.ContainsKey(EngineComponentSerializers.EntityInfoKey));
        var pos = rootEntry.Components[EngineComponentSerializers.TransformKey].GetProperty("position");
        Assert.Equal(0f, pos[0].GetSingle());
        Assert.Equal(0f, pos[1].GetSingle());
        // The two dialogue-zone children (with box colliders) survived as children.
        Assert.Equal(2, result.Scene!.Entities.Count(e =>
            e.Components.ContainsKey(EngineComponentSerializers.BoxColliderKey)));
    }

    // ── The capture story via a CHILD selection: captures THAT subtree (never empties) ──────────────

    [Fact]
    public void Capture_ViaChildSelection_CapturesThatSubtree_NotEmpty()
    {
        var serializer = NewSerializer();
        using var world = new World();
        BuildElephantKid(world);
        // Select the "shilhouette" child (a viewport pick can land on a child sprite) — its subtree is
        // shilhouette + shilhouetteIcon + its dialogue zone = 3 entities. A partial, but never empty.
        var shil = FindByName(world, "elephant-kid-shilhouette");

        var result = PrefabCapture.Build(world, shil, "shil", serializer, prefabSource: null);

        Assert.True(result.Ok, result.Refusal);
        Assert.Equal(3, result.EntityCount);
        Assert.Equal(1, result.Scene!.Entities.Count(e => e.Parent == null));
    }

    // ── The empty-capture REFUSAL (the user's elephant-kid empty-shell) ─────────────────────────────

    [Fact]
    public void Capture_BareTransformRoot_IsRefused()
    {
        var serializer = NewSerializer();
        using var world = new World();
        var bare = world.CreateEntity();
        bare.Set(new TransformComponent(new Vector2(50, 50))); // ONLY a transform — nothing to capture

        var result = PrefabCapture.Build(world, bare, "empty-one", serializer, prefabSource: null);

        Assert.False(result.Ok);
        Assert.Equal(PrefabCapture.EmptyRefusal, result.Refusal);
        Assert.Null(result.Scene);
    }

    // ── Naming: a captured root without an EntityInfo gets EntityInfo(prefabId) ──────────────────────

    [Fact]
    public void Capture_RootWithoutName_StampsPrefabId()
    {
        var serializer = NewSerializer();
        using var world = new World();
        var root = world.CreateEntity();
        root.Set(new TransformComponent(new Vector2(10, 10)));
        AddSprite(root, "file:Island/thing.png"); // has content (not empty), but no EntityInfo

        var result = PrefabCapture.Build(world, root, "my-thing", serializer, prefabSource: null);

        Assert.True(result.Ok, result.Refusal);
        var prefab = PrefabData.FromScene("my-thing", result.Scene!);
        Assert.True(prefab.Root.Components.TryGetValue(EngineComponentSerializers.EntityInfoKey, out var info));
        // The stamped EntityInfo(prefabId) has Type == the prefab id.
        Assert.Equal("my-thing", info.GetProperty("type").GetString());
    }

    // ── The uniquifier series: House, House 2, House 3 ──────────────────────────────────────────────

    [Fact]
    public void EntityNaming_ExactNameScan_NextFreeSuffix()
    {
        using var world = new World();
        Named(world, "House");
        Named(world, "House 2");
        Assert.Equal("House 3", EntityNaming.UniqueName(world, "House"));
        Assert.Equal("Barn", EntityNaming.UniqueName(world, "Barn")); // free → un-suffixed
    }

    [Fact]
    public void CreateInstanceCommand_AutoName_UniquifiesAgainstTheLiveWorld()
    {
        var serializer = NewSerializer();
        // An in-memory "house" prefab whose root is named "House".
        PrefabData housePrefab;
        using (var tmp = new World())
        {
            var r = tmp.CreateEntity();
            r.Set(new SceneObjectComponent());
            r.Set(new EntityInfoComponent("Prop", "House"));
            r.Set(new TransformComponent(Vector2.Zero));
            housePrefab = PrefabData.FromScene("house",
                new PrefabWriter(new SceneWriter(serializer)).BuildPrefab(tmp, "house", null));
        }
        Func<string, PrefabData?> source = id => id == "house" ? housePrefab : null;
        var expander = new PrefabExpander(serializer, source, loadTexture: _ => null);

        using var world = new World();
        var history = new EditorHistory(world);
        var c1 = new CreateInstanceCommand(expander, "house", new Vector2(0, 0), autoName: true);
        var c2 = new CreateInstanceCommand(expander, "house", new Vector2(20, 0), autoName: true);
        var c3 = new CreateInstanceCommand(expander, "house", new Vector2(40, 0), autoName: true);
        history.Push(c1);
        history.Push(c2);
        history.Push(c3);

        Assert.Equal("House", c1.Root.Get<EntityInfoComponent>().Name);
        Assert.Equal("House 2", c2.Root.Get<EntityInfoComponent>().Name);
        Assert.Equal("House 3", c3.Root.Get<EntityInfoComponent>().Name);

        // Undo/redo re-derives the same name (the world returns to the same state before re-apply).
        history.Undo();
        history.Redo();
        Assert.Equal("House 3", c3.Root.Get<EntityInfoComponent>().Name);
    }

    private static Entity Named(World w, string name)
    {
        var e = w.CreateEntity();
        e.Set(new EntityInfoComponent("Prop", name));
        e.Set(new TransformComponent(Vector2.Zero));
        return e;
    }

    private static Entity FindByName(World w, string name)
    {
        using var set = w.GetEntities().With<EntityInfoComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.Get<EntityInfoComponent>().Name == name) return e;
        throw new InvalidOperationException($"no entity named '{name}'");
    }
}
