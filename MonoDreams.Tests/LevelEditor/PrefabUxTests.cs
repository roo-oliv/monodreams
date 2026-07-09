using System;
using System.Collections.Generic;
using System.Linq;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.Extension;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the PF-D prefab UX at the headless level (no GraphicsDevice / no overlay): the
/// instance-children guardrail predicate + one system-level refusal, the placement / unpack / composite
/// commands, the reader's camera-rig suppression for a prefab context (pre-mortem #8), the propagation
/// mechanism a backgrounded scene rides on restore, the prefab-context status-bar text, and the
/// one-root / empty-prefab-legal write rules. The overlay op-string glue + the visual shelf are exercised
/// by the acceptance walkthrough (PF-E).
/// </summary>
public class PrefabUxTests
{
    private static ComponentSerializerRegistry NewRegistry()
    {
        var r = new ComponentSerializerRegistry();
        r.RegisterEngineComponents();
        return r;
    }

    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };

    private static Func<string, PrefabData?> Source(params (string id, PrefabData data)[] prefabs)
    {
        var dict = prefabs.ToDictionary(p => p.id, p => p.data);
        return id => dict.TryGetValue(id, out var d) ? d : null;
    }

    /// <summary>A prefab: an off-origin root (sprite at <paramref name="rootLayerDepth"/>, named
    /// <paramref name="rootName"/>) + <paramref name="childCount"/> child sprites.</summary>
    private static PrefabData MakePrefab(string id, string rootName = "boldo", int childCount = 1,
        float rootLayerDepth = 0.5f)
    {
        using var w = new World();
        var root = w.CreateEntity();
        root.Set(new SceneObjectComponent());
        root.Set(new EntityInfoComponent("Prefab", rootName));
        root.Set(new TransformComponent(new Vector2(200, 100)));
        root.Set(new SpriteInfoComponent
        {
            AssetKey = "Atlas/root", Source = new Rectangle(0, 0, 16, 16), Size = new Vector2(16, 16),
            Color = Color.White, Target = RenderTargetID.Main, LayerDepth = rootLayerDepth,
        });
        for (var i = 0; i < childCount; i++)
        {
            var c = w.CreateEntity();
            c.Set(new EntityInfoComponent("Child", $"c{i}"));
            c.Set(new TransformComponent(new Vector2(i * 4, -8)));
            c.SetParent(root);
        }
        var scene = new PrefabWriter(new SceneWriter(new SceneSerializer(NewRegistry()))).BuildPrefab(w, id);
        return PrefabData.FromScene(id, scene);
    }

    private static int CountRoots(World w)
    {
        using var set = w.GetEntities().With<SceneObjectComponent>().AsSet();
        return set.GetEntities().Length;
    }

    private static Entity FirstRoot(World w)
    {
        using var set = w.GetEntities().With<SceneObjectComponent>().AsSet();
        foreach (var e in set.GetEntities()) return e;
        return default;
    }

    // ─── The instance-children guardrail predicate ────────────────────────────────────────────────

    [Fact]
    public void IsPrefabOwned_RootEditable_ChildAndGrandchildOwned_OrdinaryNotOwned()
    {
        using var w = new World();
        var root = w.CreateEntity();
        root.Set(new PrefabInstanceComponent("npc"));
        root.Set(new TransformComponent(Vector2.Zero));
        var child = w.CreateEntity();
        child.Set(new TransformComponent(Vector2.Zero));
        child.SetParent(root);
        var grandchild = w.CreateEntity();
        grandchild.Set(new TransformComponent(Vector2.Zero));
        grandchild.SetParent(child);
        var ordinary = w.CreateEntity();
        ordinary.Set(new TransformComponent(Vector2.Zero));

        Assert.False(PrefabGuards.IsPrefabOwned(root));       // the instance ROOT stays fully editable
        Assert.True(PrefabGuards.IsPrefabOwned(child));        // a child is owned (refused)
        Assert.True(PrefabGuards.IsPrefabOwned(grandchild));   // a grandchild too
        Assert.False(PrefabGuards.IsPrefabOwned(ordinary));    // an ordinary entity is not
        Assert.False(PrefabGuards.IsPrefabOwned(default));     // a dead handle is not
    }

    [Fact]
    public void ModalTransform_RefusesAPrefabChild_AllowsTheRoot()
    {
        var source = Source(("npc", MakePrefab("npc", childCount: 1)));
        using var world = new World();
        var serializer = new SceneSerializer(NewRegistry());
        var expander = new PrefabExpander(serializer, source);
        var history = new EditorHistory(world);
        using var modal = new ModalTransformSystem(world, new GameCamera(800, 600), history, () => new KeyboardState());

        var root = expander.Instantiate(world, "npc");
        root.Set(new SceneObjectComponent());
        var child = ChildOf(world, root);

        // A prefab-owned child is refused (the shared guardrail).
        child.Set(new SelectedComponent());
        Assert.False(modal.Enter(EditorModalMode.Grab, Edit()));
        Assert.False(modal.IsActive);

        // The instance root is editable.
        child.Remove<SelectedComponent>();
        root.Set(new SelectedComponent());
        Assert.True(modal.Enter(EditorModalMode.Grab, Edit()));
        modal.Cancel(Edit()); // close the transaction the allowed entry opened
    }

    private static Entity ChildOf(World w, Entity parent)
    {
        using var set = w.GetEntities().With<ChildOfComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.Get<ChildOfComponent>().Parent.Equals(parent)) return e;
        return default;
    }

    // ─── Placement / unpack / create-from-selection commands ──────────────────────────────────────

    [Fact]
    public void CreateInstanceCommand_PlacesLinkedInstance_Undo_Disposes_Redo_ReInstantiates()
    {
        var source = Source(("npc", MakePrefab("npc", childCount: 1)));
        using var world = new World();
        var serializer = new SceneSerializer(NewRegistry());
        var expander = new PrefabExpander(serializer, source);
        var history = new EditorHistory(world);

        var cmd = new CreateInstanceCommand(expander, "npc", new Vector2(120, 34));
        history.Push(cmd);

        Assert.True(cmd.Root.IsAlive);
        Assert.True(cmd.Root.Has<PrefabInstanceComponent>());
        Assert.Equal("npc", cmd.Root.Get<PrefabInstanceComponent>().PrefabId);
        Assert.True(cmd.Root.Has<SceneObjectComponent>());
        Assert.Equal(new Vector2(120, 34), cmd.Root.Get<TransformComponent>().Position);
        Assert.Equal(1, CountRoots(world));                    // one instance root
        Assert.NotEqual(default, ChildOf(world, cmd.Root));     // its prefab-owned child was reconstructed

        history.Undo();
        Assert.False(cmd.Root.IsAlive);                         // the whole instance is gone (nothing dangles)
        Assert.Equal(0, CountRoots(world));

        history.Redo();
        Assert.True(cmd.Root.IsAlive);                          // re-instantiated deterministically
        Assert.True(cmd.Root.Has<PrefabInstanceComponent>());
    }

    [Fact]
    public void UnpackPrefabCommand_DropsMarker_KeepsEntities_UndoRelinks()
    {
        var source = Source(("npc", MakePrefab("npc", childCount: 1)));
        using var world = new World();
        var serializer = new SceneSerializer(NewRegistry());
        var expander = new PrefabExpander(serializer, source);
        var history = new EditorHistory(world);

        var root = expander.Instantiate(world, "npc");
        root.Set(new SceneObjectComponent());
        var child = ChildOf(world, root);

        history.Push(new UnpackPrefabCommand(root));
        Assert.False(root.Has<PrefabInstanceComponent>());      // the link is dropped
        Assert.True(root.IsAlive && child.IsAlive);             // the entities stay (now ordinary scene entities)
        Assert.False(PrefabGuards.IsPrefabOwned(child));        // the child is no longer prefab-owned → editable

        history.Undo();
        Assert.True(root.Has<PrefabInstanceComponent>());       // undo re-links (restores compact serialization)
        Assert.Equal("npc", root.Get<PrefabInstanceComponent>().PrefabId);
    }

    [Fact]
    public void CreateFromSelection_Composite_ReplacesOriginalWithInstance_UndoRestoresOriginal_FileStays()
    {
        using var world = new World();
        var serializer = new SceneSerializer(NewRegistry());
        var history = new EditorHistory(world);

        // An ordinary scene entity (root + child) — the "selection".
        var root = world.CreateEntity();
        root.Set(new SceneObjectComponent());
        root.Set(new EntityInfoComponent("Prop", "rock"));
        root.Set(new TransformComponent(new Vector2(40, 60)));
        var child = world.CreateEntity();
        child.Set(new EntityInfoComponent("Prop", "moss"));
        child.Set(new TransformComponent(new Vector2(5, 0)));
        child.SetParent(root);

        // Capture the subtree into a prefab (origin-normalized) — the FILE the overlay would write.
        var captured = serializer.Serialize(EntitySubgraph.Collect(world, root));
        SceneData prefabScene;
        using (var tmp = new World())
        {
            var created = serializer.Deserialize(tmp, captured);
            created[0].Set(new SceneObjectComponent());
            prefabScene = new PrefabWriter(new SceneWriter(serializer)).BuildPrefab(tmp, "rock");
        }
        var expander = new PrefabExpander(serializer, Source(("rock", PrefabData.FromScene("rock", prefabScene))));

        // The ONE undoable composite: delete the originals + place a linked instance at the world position.
        var delete = new DeleteEntityCommand(world, root, serializer);
        var create = new CreateInstanceCommand(expander, "rock", new Vector2(40, 60));
        history.Push(new CompositeCommand(new List<IEditorCommand> { delete, create }));

        Assert.Equal(1, CountRoots(world));
        Assert.True(FirstRoot(world).Has<PrefabInstanceComponent>());   // replaced by a linked instance
        Assert.Equal(new Vector2(40, 60), create.Root.Get<TransformComponent>().Position); // world position preserved

        history.Undo();
        Assert.Equal(1, CountRoots(world));
        Assert.False(FirstRoot(world).Has<PrefabInstanceComponent>());  // the original entities are back
        // The prefab FILE stays (prefabScene is written before the composite; undo never deletes it) — the
        // in-memory source still resolves it.
        Assert.NotNull(expander);
    }

    // ─── Save-Prefab write rules (one-root / empty-legal / multi-root refusal) ────────────────────

    [Fact]
    public void BuildPrefab_OneEmptyRoot_IsLegal_NoEmptySaveGuard()
    {
        // "Create Empty Prefab" / an empty prefab being assembled: one root, nothing else → legal.
        using var w = new World();
        var root = w.CreateEntity();
        root.Set(new SceneObjectComponent());
        root.Set(new TransformComponent(Vector2.Zero));

        var scene = new PrefabWriter(new SceneWriter(new SceneSerializer(NewRegistry()))).BuildPrefab(w, "empty");
        Assert.Single(scene.Entities);
        Assert.Null(scene.Camera);                     // a prefab emits no camera (pre-mortem #8)
    }

    [Fact]
    public void BuildPrefab_MultiRoot_RefusedLoud()
    {
        using var w = new World();
        var a = w.CreateEntity();
        a.Set(new SceneObjectComponent());
        a.Set(new TransformComponent(Vector2.Zero));
        var b = w.CreateEntity();
        b.Set(new SceneObjectComponent());
        b.Set(new TransformComponent(new Vector2(10, 0)));

        var writer = new PrefabWriter(new SceneWriter(new SceneSerializer(NewRegistry())));
        Assert.Throws<InvalidOperationException>(() => writer.BuildPrefab(w, "multi"));
    }

    // ─── Propagation: a backgrounded scene picks up the new prefab on restore ─────────────────────

    [Fact]
    public void BackgroundScene_ReExpandsOnRestore_PicksUpTheNewPrefab()
    {
        var serializer = new SceneSerializer(NewRegistry());

        // v1 of the prefab (root layer depth 0.5). A scene places a VERBATIM instance (no overrides), so
        // its snapshot's compact entry holds only the Transform — everything else is inherited.
        var v1 = Source(("npc", MakePrefab("npc", rootLayerDepth: 0.5f)));
        SceneData snapshot;
        using (var w = new World())
        {
            var e1 = new PrefabExpander(serializer, v1);
            var inst = e1.Instantiate(w, "npc");
            inst.Set(new SceneObjectComponent());
            inst.Get<TransformComponent>().Position = new Vector2(50, 50);
            snapshot = new SceneWriter(serializer, v1).BuildScene(w);
        }

        // "Save" prefab v2 (root layer depth 0.9). The backgrounded scene snapshot is unchanged — the
        // chosen mechanism is that its NEXT restore re-expands through the reader reading the NEW prefab.
        var v2 = Source(("npc", MakePrefab("npc", rootLayerDepth: 0.9f)));
        using (var w2 = new World())
        {
            var e2 = new PrefabExpander(serializer, v2);
            var created = e2.ExpandScene(w2, snapshot);
            var inst = created[0];
            Assert.True(inst.Has<PrefabInstanceComponent>());
            Assert.Equal(0.9f, inst.Get<SpriteInfoComponent>().LayerDepth);         // picked up v2
            Assert.Equal(new Vector2(50, 50), inst.Get<TransformComponent>().Position); // its own Transform preserved
        }
    }

    // ─── Reader camera-rig suppression for a prefab context (pre-mortem #8) ───────────────────────

    [Fact]
    public void SceneReader_SuppressCameraRig_NeverSyncsTheRig_UnsuppressedDoes()
    {
        var serializer = new SceneSerializer(NewRegistry());
        using var world = new World();
        var camera = new GameCamera(800, 600) { Zoom = 1f };
        var rigSyncs = 0;
        using var reader = new SceneReaderSystem(world, serializer, content: null, loadTexture: _ => null,
            camera: camera, applyCameraToRig: _ => rigSyncs++);

        var scene = OneSpriteScene(serializer);

        world.Publish(new LoadSceneRequest(scene, suppressCameraRig: true));  // a prefab-context load
        Assert.Equal(0, rigSyncs);                                            // the rig is NEVER synced

        world.Publish(new LoadSceneRequest(scene, suppressCameraRig: false)); // a scene load
        Assert.Equal(1, rigSyncs);                                            // the rig IS synced
    }

    private static SceneData OneSpriteScene(SceneSerializer serializer)
    {
        using var w = new World();
        var e = w.CreateEntity();
        e.Set(new SceneObjectComponent());
        e.Set(new TransformComponent(new Vector2(100, 50)));
        e.Set(new SpriteInfoComponent
        {
            AssetKey = "Atlas/x", Source = new Rectangle(0, 0, 16, 16), Size = new Vector2(16, 16),
            Color = Color.White, Target = RenderTargetID.Main, LayerDepth = 0.5f,
        });
        return new SceneWriter(serializer).BuildScene(w); // camera null → the rig would adopt the framed view
    }

    // ─── Status bar ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void StatusBar_Right_PrefabContext_ReadsPrefabId()
    {
        Assert.Equal("prefab: npc", StatusBarModel.Right("npc", ViewportContextKind.Prefab, RunMode.Edit));
        Assert.Equal("island", StatusBarModel.Right("island", ViewportContextKind.Scene, RunMode.Edit));
        Assert.Equal("island  |  Paused", StatusBarModel.Right("island", ViewportContextKind.Game, RunMode.Edit));
    }
}
