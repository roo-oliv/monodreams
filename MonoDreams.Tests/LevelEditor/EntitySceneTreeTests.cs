#nullable enable
using System.Linq;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Inspector;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the entity scene-tree builder (task item 2): roots first, <c>ChildOfComponent</c>
/// descendants nested one indent deeper (pre-order), editor-infrastructure entities hidden, and a
/// child of a hidden parent re-parented to its nearest included ancestor (or promoted to a root).
/// Pure — a hand-built entity pool, no GraphicsDevice.
/// </summary>
public class EntitySceneTreeTests
{
    private static Entity Scene(World world)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(Vector2.Zero));
        return e;
    }

    private static bool NotInfra(Entity e) => !e.Has<EditorInfrastructureComponent>();

    [Fact]
    public void Build_RootsFirst_ChildrenIndentedByDepth_PreOrder()
    {
        using var world = new World();
        var root = Scene(world);
        var child = Scene(world);
        child.Set(new ChildOfComponent(root));
        var grandchild = Scene(world);
        grandchild.Set(new ChildOfComponent(child));
        var root2 = Scene(world);

        var nodes = EntitySceneTree.Build(new[] { root, child, grandchild, root2 }, NotInfra);
        var depth = nodes.ToDictionary(n => n.Entity, n => n.Depth);

        Assert.Equal(0, depth[root]);
        Assert.Equal(1, depth[child]);
        Assert.Equal(2, depth[grandchild]);
        Assert.Equal(0, depth[root2]);

        // Pre-order: root, then its subtree, before the next root.
        var order = nodes.Select(n => n.Entity).ToList();
        Assert.True(order.IndexOf(root) < order.IndexOf(child));
        Assert.True(order.IndexOf(child) < order.IndexOf(grandchild));
        Assert.True(order.IndexOf(grandchild) < order.IndexOf(root2));

        // HasChildren reflects the tree.
        Assert.True(nodes.Single(n => n.Entity == root).HasChildren);
        Assert.True(nodes.Single(n => n.Entity == child).HasChildren);
        Assert.False(nodes.Single(n => n.Entity == grandchild).HasChildren);
        Assert.False(nodes.Single(n => n.Entity == root2).HasChildren);
    }

    [Fact]
    public void Build_HidesEditorInfrastructureEntities()
    {
        using var world = new World();
        var root = Scene(world);
        var infra = Scene(world);
        infra.Set(new ChildOfComponent(root));
        infra.Set(new EditorInfrastructureComponent());

        var nodes = EntitySceneTree.Build(new[] { root, infra }, NotInfra);

        Assert.Contains(nodes, n => n.Entity == root);
        Assert.DoesNotContain(nodes, n => n.Entity == infra);
        // The included root has no visible children, so it draws no collapse arrow.
        Assert.False(nodes.Single(n => n.Entity == root).HasChildren);
    }

    [Fact]
    public void Build_ChildOfHiddenParent_IsPromotedToRoot()
    {
        using var world = new World();
        var infraParent = Scene(world);
        infraParent.Set(new EditorInfrastructureComponent());
        var gameChild = Scene(world);
        gameChild.Set(new ChildOfComponent(infraParent));

        var nodes = EntitySceneTree.Build(new[] { infraParent, gameChild }, NotInfra);

        Assert.DoesNotContain(nodes, n => n.Entity == infraParent);
        var childNode = nodes.Single(n => n.Entity == gameChild);
        Assert.Equal(0, childNode.Depth); // promoted to a root, not orphaned
    }

    [Fact]
    public void Build_ReparentsToNearestIncludedAncestor()
    {
        using var world = new World();
        var root = Scene(world);
        var hiddenMid = Scene(world);
        hiddenMid.Set(new ChildOfComponent(root));
        hiddenMid.Set(new EditorInfrastructureComponent());
        var leaf = Scene(world);
        leaf.Set(new ChildOfComponent(hiddenMid));

        var nodes = EntitySceneTree.Build(new[] { root, hiddenMid, leaf }, NotInfra);

        var depth = nodes.ToDictionary(n => n.Entity, n => n.Depth);
        Assert.False(depth.ContainsKey(hiddenMid));
        Assert.Equal(0, depth[root]);
        Assert.Equal(1, depth[leaf]); // re-attached under root (nearest included ancestor)
        Assert.True(nodes.Single(n => n.Entity == root).HasChildren);
    }

    [Fact]
    public void Build_EmptyPool_IsEmpty()
    {
        Assert.Empty(EntitySceneTree.Build(global::System.Array.Empty<Entity>()));
    }

    [Fact]
    public void Build_IncludesTheCameraEntity_AndHidesInfrastructure()
    {
        // CM: the camera is an ordinary scene entity now (NOT infra), so the default hide-infra filter
        // includes it naturally alongside the content, while editor infrastructure stays hidden.
        using var world = new World();
        var root = Scene(world);
        var camera = Scene(world);
        camera.Set(new CameraComponent { Zoom = 1f }); // an ordinary scene entity carrying a camera
        var otherInfra = Scene(world);
        otherInfra.Set(new EditorInfrastructureComponent());

        bool Include(Entity e) => !e.Has<EditorInfrastructureComponent>();
        var nodes = EntitySceneTree.Build(new[] { root, camera, otherInfra }, Include);

        Assert.Contains(nodes, n => n.Entity == root);
        Assert.Contains(nodes, n => n.Entity == camera);           // the camera entity is ordinary content
        Assert.DoesNotContain(nodes, n => n.Entity == otherInfra); // infra stays hidden
    }
}
