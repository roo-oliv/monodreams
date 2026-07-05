#nullable enable
using System.Collections.Generic;
using System.Linq;
using DefaultEcs;
using MonoDreams.Component;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.UI;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the SCENE-tree model (<see cref="SceneTreeBuilder"/>): roots come first with their
/// <c>ChildOfComponent</c> descendants nested and indented; editor-infrastructure entities are
/// hidden; a child of a hidden/dead parent is promoted to a root; labels resolve from
/// <c>EntityInfoComponent</c> → <c>EditorId</c> → a stable hash; a <c>ChildOf</c> cycle cannot
/// loop the walk. Pure logic — a headless <see cref="World"/>, no GraphicsDevice.
/// </summary>
public class SceneTreeBuilderTests
{
    [Fact]
    public void Build_RootsFirst_ChildrenNestedAndIndented()
    {
        using var world = new World();
        var a = world.CreateEntity();
        var b = world.CreateEntity();
        var c = world.CreateEntity();
        var d = world.CreateEntity();
        a.Set(new EntityInfoComponent("A"));
        b.Set(new EntityInfoComponent("B"));
        c.Set(new EntityInfoComponent("C"));
        d.Set(new EntityInfoComponent("D"));
        b.Set(new ChildOfComponent(a)); // A > B
        c.Set(new ChildOfComponent(b)); // B > C
        // d is a second root.

        var rows = SceneTreeBuilder.Build(new[] { a, b, c, d });

        Assert.Equal(new[] { "A", "B", "C", "D" }, rows.Select(r => r.Label).ToArray());
        Assert.Equal(new[] { 0, 1, 2, 0 }, rows.Select(r => r.Depth).ToArray());
    }

    [Fact]
    public void Build_HidesEditorInfrastructureEntities()
    {
        using var world = new World();
        var game = world.CreateEntity();
        var chrome = world.CreateEntity();
        game.Set(new EntityInfoComponent("Player"));
        chrome.Set(new EntityInfoComponent("Chrome"));
        chrome.Set(new EditorInfrastructureComponent());

        var rows = SceneTreeBuilder.Build(new[] { game, chrome });

        Assert.Single(rows);
        Assert.Equal("Player", rows[0].Label);
    }

    [Fact]
    public void Build_ChildOfAHiddenParent_BecomesARoot()
    {
        using var world = new World();
        var infraParent = world.CreateEntity();
        var child = world.CreateEntity();
        infraParent.Set(new EditorInfrastructureComponent());
        child.Set(new EntityInfoComponent("Child"));
        child.Set(new ChildOfComponent(infraParent)); // parent is hidden → child is promoted

        var rows = SceneTreeBuilder.Build(new[] { infraParent, child });

        Assert.Single(rows);
        Assert.Equal(0, rows[0].Depth); // a root, not indented under the hidden parent
    }

    [Fact]
    public void Build_ChildOfCycle_DoesNotLoop()
    {
        using var world = new World();
        var a = world.CreateEntity();
        var b = world.CreateEntity();
        a.Set(new EntityInfoComponent("A"));
        b.Set(new EntityInfoComponent("B"));
        a.Set(new ChildOfComponent(b));
        b.Set(new ChildOfComponent(a)); // 2-cycle — must not infinite-loop

        var rows = SceneTreeBuilder.Build(new[] { a, b }); // returns (no throw / no hang)
        Assert.True(rows.Count <= 2);
    }

    [Fact]
    public void LabelFor_PrefersName_ThenType_ThenEditorId()
    {
        using var world = new World();
        var named = world.CreateEntity();
        named.Set(new EntityInfoComponent("Enemy", "Goblin"));
        Assert.Equal("Goblin", SceneTreeBuilder.LabelFor(named));

        var typedOnly = world.CreateEntity();
        typedOnly.Set(new EntityInfoComponent("Crate"));
        Assert.Equal("Crate", SceneTreeBuilder.LabelFor(typedOnly));

        var idOnly = world.CreateEntity();
        idOnly.Set(new EditorIdComponent(7));
        Assert.Equal("#7", SceneTreeBuilder.LabelFor(idOnly));

        var bare = world.CreateEntity();
        Assert.StartsWith("Entity #", SceneTreeBuilder.LabelFor(bare));
    }
}
