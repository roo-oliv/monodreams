using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Level;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.System;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the COLLIDER half of <c>TileGridBakeSystem</c> — the half that needs no
/// <c>GraphicsDevice</c>: every paint value here leaves <c>TilesetKey</c> null, so the bake derives
/// greedy-merged collider children and no visuals at all (no texture resolver, no sprite/mesh path,
/// no render target). The streamed-sprite half is verified on the demo host, not here.
///
/// Covers the level-loading premise "The paint grid is authored cells + values; everything
/// visible/collidable is a bake product" (the bake-product half: <c>BakedProductComponent</c>
/// children, disposed and re-created per bake, in BOTH run modes) and the level-editor premise
/// "Tile sprites stream per chunk; colliders bake whole" (colliders never stream, so a bake with no
/// <c>FocusBounds</c> is the whole truth).
/// </summary>
public class TileGridBakeSystemTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    /// <summary>The grid's baked children — <c>BakedProductComponent</c> + a `ChildOf` link back to
    /// the grid. Colliders are parented (they ride the grid's matrix); tile visuals are not, so this
    /// query is exactly "the colliders" for a tileset-less grid.</summary>
    private static List<Entity> BakedChildren(World world, Entity grid)
    {
        var list = new List<Entity>();
        using var set = world.GetEntities().With<BakedProductComponent>().With<ChildOfComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.Get<ChildOfComponent>().Parent == grid)
                list.Add(e);
        return list;
    }

    /// <summary>Every baked product in the world, parented or not — so "no visuals were created" is
    /// assertable without touching a texture.</summary>
    private static int AllBakedCount(World world)
    {
        using var set = world.GetEntities().With<BakedProductComponent>().AsSet();
        return set.GetEntities().Length;
    }

    private static TilePaintValue Wall(params int[] activeLayers) => new()
    {
        Id = 1,
        Name = "Wall",
        ActiveLayers = activeLayers,
        Passive = true,
        TilesetKey = null, // collision-only paint: no visuals, so no GraphicsDevice
    };

    private static Entity MakeGrid(World world, TileGridComponent grid, Vector2 position = default)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(position));
        e.Set(grid); // fires the component-ADDED subscription → bake on the next Update
        return e;
    }

    private static TileGridComponent Grid(float cellSize, TilePaintValue value,
        params (int X, int Y)[] cells)
    {
        var grid = new TileGridComponent { CellSize = cellSize };
        grid.Values.Add(value);
        foreach (var (x, y) in cells) grid.Cells[TileGridComponent.Pack(x, y)] = value.Id;
        return grid;
    }

    // ---- the bake: merged colliders as marked, parented children ----------------------------

    [Fact]
    public void ComponentAdded_Bakes_MergedColliderChildren_AtTheRectCentres()
    {
        using var world = new World();
        using var bake = new TileGridBakeSystem(world);

        // Three cells in a row at cell size 32 → ONE merged 3x1 rect, not three per-cell colliders.
        var grid = MakeGrid(world, Grid(32f, Wall(3), (0, 0), (1, 0), (2, 0)));

        // Nothing baked until the queue drains in Update (event-driven, never per-frame).
        Assert.Empty(BakedChildren(world, grid));

        bake.Update(Edit());

        var collider = Assert.Single(BakedChildren(world, grid));
        Assert.True(collider.Has<BakedProductComponent>());
        Assert.Equal(grid, collider.Get<ChildOfComponent>().Parent);

        // Size = cells * CellSize; the transform sits at the rect's CENTRE (BoxCollider is centered).
        var box = collider.Get<BoxColliderComponent>();
        Assert.Equal(new Vector2(96, 32), box.Size);
        Assert.Equal(new HashSet<int> { 3 }, box.ActiveLayers);
        Assert.True(box.Passive);
        Assert.Equal(new Vector2(48, 16), collider.Get<TransformComponent>().Position);

        Assert.Equal(1, bake.BakeCount);
        // No tileset key ⇒ no visual products at all (the one collider is everything baked).
        Assert.Equal(1, AllBakedCount(world));
    }

    [Fact]
    public void BakedColliders_AreGridLocal_SoTheGridEntityIsTheAnchor()
    {
        using var world = new World();
        using var bake = new TileGridBakeSystem(world);

        // The grid entity's transform is the ONE anchor: cell (0,0)'s top-left sits on it, and the
        // colliders are parented, so their LOCAL position is the cell layout and their WORLD position
        // folds the anchor in. Bake() is public precisely so a test can force this without Update.
        var grid = MakeGrid(world, Grid(16f, Wall(1), (2, 3)), new Vector2(100, 200));

        bake.Bake(grid);

        var collider = Assert.Single(BakedChildren(world, grid));
        Assert.Equal(new Vector2(2 * 16 + 8, 3 * 16 + 8), collider.Get<TransformComponent>().Position);
        Assert.Equal(new Vector2(100 + 40, 200 + 56), collider.Get<TransformComponent>().WorldPosition);
        Assert.Equal(new Vector2(16, 16), collider.Get<BoxColliderComponent>().Size);
    }

    [Fact]
    public void NegativeCells_BakeAtNegativeLocalOffsets()
    {
        using var world = new World();
        using var bake = new TileGridBakeSystem(world);

        // Painting up/left of the anchor is ordinary (cells are signed).
        var grid = MakeGrid(world, Grid(32f, Wall(1), (-2, -1), (-1, -1)));

        bake.Update(Edit());

        var collider = Assert.Single(BakedChildren(world, grid));
        Assert.Equal(new Vector2(64, 32), collider.Get<BoxColliderComponent>().Size);
        // rect (-2,-1,2,1) → centre = (-64,-32) + (32,16)
        Assert.Equal(new Vector2(-32, -16), collider.Get<TransformComponent>().Position);
    }

    // ---- identity: EntityType ?? Name, plus the index-numbered name -------------------------

    [Fact]
    public void ColliderIdentity_UsesEntityTypeWhenSet_ElseTheValueName()
    {
        using var world = new World();
        using var bake = new TileGridBakeSystem(world);

        var spike = new TilePaintValue
        {
            Id = 1,
            Name = "Spike",
            EntityType = "Hazard", // what game systems pattern-match on
            ActiveLayers = [2],
            Passive = false,
        };
        var grid = MakeGrid(world, Grid(32f, spike, (0, 0)));

        bake.Update(Edit());

        var collider = Assert.Single(BakedChildren(world, grid));
        var info = collider.Get<EntityInfoComponent>();
        Assert.Equal("Hazard", info.Type);
        Assert.Equal("Spike_00", info.Name);
    }

    [Fact]
    public void ColliderIdentity_FallsBackToTheValueName_AndNumbersByRectIndex()
    {
        using var world = new World();
        using var bake = new TileGridBakeSystem(world);

        // Two disjoint runs in one row → two merged rects, numbered in the merge's (y, x) order.
        var grid = MakeGrid(world, Grid(32f, Wall(1), (0, 0), (1, 0), (5, 0), (6, 0)));

        bake.Update(Edit());

        var children = BakedChildren(world, grid);
        Assert.Equal(2, children.Count);
        var names = new List<string>();
        foreach (var child in children)
        {
            Assert.Equal("Wall", child.Get<EntityInfoComponent>().Type); // EntityType null → Name
            names.Add(child.Get<EntityInfoComponent>().Name);
        }
        names.Sort();
        Assert.Equal(new List<string> { "Wall_00", "Wall_01" }, names); // zero-padded to two digits
    }

    // ---- the game seam: configureCollider ---------------------------------------------------

    [Fact]
    public void ConfigureColliderCallback_IsInvokedOncePerBakedCollider_WithItsPaintValue()
    {
        using var world = new World();
        var seen = new List<(Entity Collider, TilePaintValue Value)>();

        var wall = Wall(1);
        // The module never references a game component; the game attaches its own here.
        using var bake = new TileGridBakeSystem(world,
            configureCollider: (collider, value) => seen.Add((collider, value)));

        var grid = MakeGrid(world, Grid(32f, wall, (0, 0), (1, 0), (5, 0), (6, 0)));

        bake.Update(Edit());

        var children = BakedChildren(world, grid);
        Assert.Equal(2, children.Count);
        Assert.Equal(2, seen.Count);
        foreach (var (collider, value) in seen)
        {
            Assert.Same(wall, value); // the AUTHORED value instance, so the hook can key on it
            Assert.Contains(collider, children);
        }
    }

    // ---- re-bake disposes the previous products ---------------------------------------------

    [Fact]
    public void ReBake_DisposesTheOldProducts_LeavingNoDuplicates()
    {
        using var world = new World();
        using var bake = new TileGridBakeSystem(world);

        var grid = MakeGrid(world, Grid(32f, Wall(1), (0, 0), (1, 0)));
        bake.Update(Edit());
        var first = Assert.Single(BakedChildren(world, grid));

        bake.Bake(grid); // same cells: the product set must stay the same SIZE, not double

        Assert.False(first.IsAlive); // the previous product was disposed, not orphaned
        Assert.Single(BakedChildren(world, grid));
        Assert.Equal(1, AllBakedCount(world));
        Assert.Equal(2, bake.BakeCount);
    }

    [Fact]
    public void ChangedGrid_ReBakesOnlyAfterTheQuietWindow()
    {
        using var world = new World();
        using var bake = new TileGridBakeSystem(world);

        var component = Grid(32f, Wall(1), (0, 0), (1, 0));
        var grid = MakeGrid(world, component);
        bake.Update(Edit());
        Assert.Equal(1, bake.BakeCount);
        var before = Assert.Single(BakedChildren(world, grid));
        Assert.Equal(new Vector2(64, 32), before.Get<BoxColliderComponent>().Size);

        // A paint stroke edits the cells in place and notifies — mid-stroke changes must NOT thrash
        // the bake every frame, so the re-bake waits QuietFrames frames of silence.
        component.Cells[TileGridComponent.Pack(2, 0)] = 1;
        grid.NotifyChanged<TileGridComponent>();

        for (var frame = 0; frame < TileGridBakeSystem.QuietFrames - 1; frame++)
        {
            bake.Update(Edit());
            Assert.Equal(1, bake.BakeCount); // still debouncing
        }

        bake.Update(Edit());

        Assert.Equal(2, bake.BakeCount);
        var after = Assert.Single(BakedChildren(world, grid));
        Assert.Equal(new Vector2(96, 32), after.Get<BoxColliderComponent>().Size); // extra cell merged in
    }

    // ---- what does NOT bake ------------------------------------------------------------------

    [Fact]
    public void ValueWithNoActiveLayers_BakesNoColliders()
    {
        using var world = new World();
        using var bake = new TileGridBakeSystem(world);

        // Empty ActiveLayers = a pure-visual paint. With no tileset either, this value derives
        // nothing at all — but the bake still ran (it is not an error).
        var decor = new TilePaintValue { Id = 1, Name = "Decor" }; // ActiveLayers defaults to empty
        var grid = MakeGrid(world, Grid(32f, decor, (0, 0), (1, 0)));

        bake.Update(Edit());

        Assert.Empty(BakedChildren(world, grid));
        Assert.Equal(0, AllBakedCount(world));
        Assert.Equal(1, bake.BakeCount);
    }

    [Fact]
    public void ValueId0_IsEmptyAndNeverBakes()
    {
        using var world = new World();
        using var bake = new TileGridBakeSystem(world);

        // Id 0 is the "empty" cell value by definition; a definition claiming it must not derive
        // colliders over the unpainted parts of the world.
        var component = new TileGridComponent { CellSize = 32f };
        component.Values.Add(new TilePaintValue { Id = 0, Name = "Empty", ActiveLayers = [1] });
        component.Values.Add(Wall(1));
        component.Cells[TileGridComponent.Pack(0, 0)] = 0;
        component.Cells[TileGridComponent.Pack(2, 0)] = 1;
        var grid = MakeGrid(world, component);

        bake.Update(Edit());

        var collider = Assert.Single(BakedChildren(world, grid));
        Assert.Equal("Wall_00", collider.Get<EntityInfoComponent>().Name);
    }

    [Fact]
    public void EmptyGrid_BakesNothing_ButStillCounts()
    {
        using var world = new World();
        using var bake = new TileGridBakeSystem(world);

        var grid = MakeGrid(world, new TileGridComponent { CellSize = 32f }); // no values, no cells

        bake.Update(Edit());

        Assert.Empty(BakedChildren(world, grid));
        Assert.Equal(1, bake.BakeCount);
    }

    [Fact]
    public void EmptyQueue_IsANoOp()
    {
        using var world = new World();
        using var bake = new TileGridBakeSystem(world);

        bake.Update(Edit()); // no grid added

        Assert.Equal(0, bake.BakeCount);
    }

    // ---- the bake is a scene-loading participant, not Edit-only tooling ---------------------

    [Fact]
    public void Bake_RunsInPlayMode_Too()
    {
        // A shipped game loading a scene must collide with its painted terrain on frame one — the
        // scene reader adding the component IS the bake trigger, in Play as much as in Edit.
        using var world = new World();
        using var bake = new TileGridBakeSystem(world);

        var grid = MakeGrid(world, Grid(32f, Wall(1), (0, 0), (0, 1)));

        bake.Update(Play());

        var collider = Assert.Single(BakedChildren(world, grid));
        Assert.Equal(new Vector2(32, 64), collider.Get<BoxColliderComponent>().Size);
        Assert.Equal(1, bake.BakeCount);
    }

    // ---- colliders never stream ---------------------------------------------------------------

    [Fact]
    public void CollidersBakeWhole_EvenAcrossChunkBorders()
    {
        using var world = new World();
        using var bake = new TileGridBakeSystem(world);

        // A run straddling the ChunkCells border must merge into ONE rect: streaming the colliders
        // per chunk would cut it and reintroduce exactly the flush-adjacent seam the merge avoids.
        var cells = new List<(int X, int Y)>();
        for (var x = TileGridBakeSystem.ChunkCells - 2; x <= TileGridBakeSystem.ChunkCells + 1; x++)
            cells.Add((x, 0));
        var grid = MakeGrid(world, Grid(32f, Wall(1), cells.ToArray()));

        bake.Update(Edit());

        var collider = Assert.Single(BakedChildren(world, grid));
        Assert.Equal(new Vector2(4 * 32, 32), collider.Get<BoxColliderComponent>().Size);
    }
}
