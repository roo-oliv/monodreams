using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Extension;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// PF-F prefab-context hygiene: auto-parenting placed entities under the single prefab root (so assembly
/// never creates a second, un-savable root), the screen-infrastructure delete guard (the crash fix), and
/// the prefab-root resolver + KeepAlive screen-infra predicate the tree-hide + delete guard share.
/// Pure logic — hand-built entities, no GraphicsDevice.
/// </summary>
public class PrefabContextHygieneTests
{
    private static SceneSerializer NewSerializer()
    {
        var r = new ComponentSerializerRegistry();
        r.RegisterEngineComponents();
        return new SceneSerializer(r);
    }

    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };

    private static Entity Root(World w, string name)
    {
        var e = w.CreateEntity();
        e.Set(new SceneObjectComponent());
        e.Set(new TransformComponent(Vector2.Zero));
        e.Set(new EntityInfoComponent("Prop", name));
        return e;
    }

    private static Entity FindByName(World w, string name)
    {
        using var set = w.GetEntities().With<EntityInfoComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.IsAlive && e.Get<EntityInfoComponent>().Name == name) return e;
        return default;
    }

    private static int RootCount(World w)
    {
        var n = 0;
        using var set = w.GetEntities().With<SceneObjectComponent>().AsSet();
        foreach (var e in set.GetEntities())
        {
            if (!e.IsAlive) continue;
            if (e.Has<ChildOfComponent>() && e.Get<ChildOfComponent>().Parent.IsAlive) continue;
            n++;
        }
        return n;
    }

    // ── PrefabContextRoot.Resolve ────────────────────────────────────────────────────────────────────

    [Fact]
    public void PrefabContextRoot_ResolvesTheSingleRoot_DefaultWhenAmbiguous()
    {
        using var world = new World();
        var root = Root(world, "root");
        Assert.Equal(root, PrefabContextRoot.Resolve(world));

        var second = Root(world, "second"); // now two roots → ambiguous
        Assert.Equal(default, PrefabContextRoot.Resolve(world));
        // Excluding the just-created one still finds the pre-existing root (the mid-placement resolve).
        Assert.Equal(root, PrefabContextRoot.Resolve(world, exclude: second));
    }

    // ── Auto-parent: a placement in a prefab context becomes a CHILD, never a second root ────────────

    [Fact]
    public void CreateEntityCommand_WithParentTo_ParentsUnderRoot_DropsSaveRootTag_UndoRedoClean()
    {
        var serializer = NewSerializer();
        using var world = new World();
        var prefabRoot = Root(world, "prefab-root");
        var history = new EditorHistory(world);

        var cmd = new CreateEntityCommand(world, serializer, w =>
        {
            var e = w.CreateEntity();
            e.Set(new TransformComponent(new Vector2(10, 10)));
            e.Set(new EntityInfoComponent("Prop", "placed"));
            return e;
        }, parentTo: prefabRoot);
        history.Push(cmd);

        var placed = FindByName(world, "placed");
        Assert.True(placed.IsAlive);
        Assert.True(placed.Has<ChildOfComponent>());
        Assert.Equal(prefabRoot, placed.Get<ChildOfComponent>().Parent);
        Assert.False(placed.Has<SceneObjectComponent>());  // no longer a save-root — it is prefab content
        Assert.Equal(1, RootCount(world));                 // still exactly ONE root (the prefab root)

        history.Undo();
        Assert.Equal(default, FindByName(world, "placed")); // disposed

        history.Redo();
        var replaced = FindByName(world, "placed");
        Assert.True(replaced.IsAlive);
        Assert.Equal(prefabRoot, replaced.Get<ChildOfComponent>().Parent); // re-parented on redo
        Assert.False(replaced.Has<SceneObjectComponent>());
        Assert.Equal(1, RootCount(world));
    }

    [Fact]
    public void CreateEntityCommand_WithoutParentTo_StaysASaveRoot()
    {
        var serializer = NewSerializer();
        using var world = new World();
        var history = new EditorHistory(world);
        var cmd = new CreateEntityCommand(world, serializer, w =>
        {
            var e = w.CreateEntity();
            e.Set(new TransformComponent(Vector2.Zero));
            e.Set(new EntityInfoComponent("Prop", "scene-placed"));
            return e;
        });
        history.Push(cmd);
        var placed = FindByName(world, "scene-placed");
        Assert.True(placed.Has<SceneObjectComponent>());   // a scene placement keeps the save-root
        Assert.False(placed.Has<ChildOfComponent>());
    }

    // ── The KeepAlive delete guard (THE CRASH FIX) ───────────────────────────────────────────────────

    [Fact]
    public void DeleteSelection_RefusesScreenInfrastructure_ButDeletesOrdinaryEntities()
    {
        var serializer = NewSerializer();
        using var world = new World();
        var history = new EditorHistory(world);

        var dialog = Root(world, "Dialog");   // stands in for the screen-held dialogue-UI root
        var ordinary = Root(world, "grass");

        // isScreenInfrastructure flags the "Dialog" root (the KeepAlive predicate's role).
        var cmds = new EditorCommandSystem(world, history, serializer,
            isScreenInfrastructure: e => e.IsAlive && e.Has<EntityInfoComponent>()
                                         && e.Get<EntityInfoComponent>().Name == "Dialog");

        // Deleting the screen infrastructure is REFUSED (it survives — no crash).
        dialog.Set(new SelectedComponent());
        cmds.DeleteSelection(Edit());
        Assert.True(dialog.IsAlive);

        // Deleting an ordinary entity works.
        dialog.Remove<SelectedComponent>();
        ordinary.Set(new SelectedComponent());
        cmds.DeleteSelection(Edit());
        Assert.False(ordinary.IsAlive);
    }

    // ── Transport.IsScreenInfrastructure: KeepAlive + ChildOf ancestor propagation ───────────────────

    [Fact]
    public void Transport_IsScreenInfrastructure_MatchesKeepAlive_AndPropagatesDownChildOf()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var transport = new EditorTransport(world, history);

        var dialogRoot = Root(world, "Dialog");
        var dialogChild = world.CreateEntity();
        dialogChild.Set(new TransformComponent(Vector2.Zero));
        dialogChild.SetParent(dialogRoot);
        var other = Root(world, "other");

        Assert.False(transport.IsScreenInfrastructure(dialogRoot)); // null KeepAlive → nothing is infra

        transport.KeepAlive = e => e.IsAlive && e.Has<EntityInfoComponent>()
                                   && e.Get<EntityInfoComponent>().Name == "Dialog";

        Assert.True(transport.IsScreenInfrastructure(dialogRoot));  // named directly
        Assert.True(transport.IsScreenInfrastructure(dialogChild)); // kept via the ChildOf ancestor walk
        Assert.False(transport.IsScreenInfrastructure(other));      // an ordinary entity is not infra
    }
}
