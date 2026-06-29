using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Selection;
using MonoDreams.LevelEditor.System;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the level-editor premise "Selection picks MAX final <c>LayerDepth</c> with a
/// selection-owned tiebreak" via the named tests <c>SelectionTopmostTest</c> and
/// <c>SelectionOrderingTest</c>. Pure logic: hand-built entities + a hand-driven
/// <see cref="SelectionSystem.Update"/> frame; no GraphicsDevice (selection reads
/// <c>DrawComponent.LayerDepth</c>, which the test sets directly to mimic the post-YSort value).
/// </summary>
public class SelectionTests
{
    private static Entity MakeCursor(World world, Vector2 worldPoint, bool leftPressed)
    {
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent
        {
            WorldPosition = worldPoint,
            LeftButtonPressed = leftPressed,
            LeftButton = leftPressed,
        });
        return cursor;
    }

    /// <summary>A 10×10 sprite at <paramref name="position"/> (origin top-left), already "rendered"
    /// (Visible + a DrawComponent whose LayerDepth stands in for the post-YSort final depth).</summary>
    private static Entity MakeSprite(World world, Vector2 position, float finalDepth,
        float rotation = 0f, Vector2? scale = null, Vector2? origin = null, int size = 10)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(position, rotation, scale, origin));
        e.Set(new SpriteInfoComponent
        {
            Source = new Rectangle(0, 0, size, size),
            Size = new Vector2(size, size),
            Origin = origin ?? Vector2.Zero,
            Target = RenderTargetID.Main,
        });
        e.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main, LayerDepth = finalDepth });
        e.Set(new VisibleComponent());
        return e;
    }

    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    private static Entity? Selected(World world)
    {
        using var set = world.GetEntities().With<SelectedComponent>().AsSet();
        foreach (var e in set.GetEntities()) return e;
        return null;
    }

    // ---- SelectionTopmostTest: stacked overlapping sprites → click selects MAX final LayerDepth ----

    [Fact]
    public void SelectionTopmostTest()
    {
        using var world = new World();

        // Two sprites overlapping the click point, on different final depths.
        var back = MakeSprite(world, new Vector2(0, 0), finalDepth: 0.2f);
        var front = MakeSprite(world, new Vector2(0, 0), finalDepth: 0.8f);
        MakeCursor(world, new Vector2(5, 5), leftPressed: true);

        using var selection = new SelectionSystem(world);
        selection.Update(Edit());

        // The frontmost (MAX final LayerDepth) is selected, not the back sprite.
        Assert.Equal(front, Selected(world));
        Assert.False(back.Has<SelectedComponent>());
    }

    [Fact]
    public void SelectionTopmost_InPlayMode_IsInert()
    {
        using var world = new World();
        MakeSprite(world, new Vector2(0, 0), finalDepth: 0.8f);
        MakeCursor(world, new Vector2(5, 5), leftPressed: true);

        using var selection = new SelectionSystem(world);
        selection.Update(Play()); // Edit-guarded: a click in Play selects nothing.

        Assert.Null(Selected(world));
    }

    [Fact]
    public void SelectionTopmost_ClickEmptySpace_ClearsSelection()
    {
        using var world = new World();
        var sprite = MakeSprite(world, new Vector2(0, 0), finalDepth: 0.8f);
        var cursor = MakeCursor(world, new Vector2(5, 5), leftPressed: true);

        using var selection = new SelectionSystem(world);
        selection.Update(Edit());
        Assert.Equal(sprite, Selected(world));

        // Move the cursor off any sprite and click again → clears.
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.WorldPosition = new Vector2(500, 500);
        input.LeftButtonPressed = true;
        selection.Update(Edit());

        Assert.Null(Selected(world));
    }

    [Fact]
    public void SelectionTopmost_HonorsRotationScaleOrigin()
    {
        // A sprite scaled 2× and rotated 90° about an origin. A point that is OUTSIDE the naive
        // unrotated/unscaled bounds but INSIDE the actual rendered quad must hit; a point outside both misses.
        using var world = new World();

        // 10×10 source, scale 2 → 20×20 rendered, placed at world (100,100), no origin, no rotation first.
        var sprite = MakeSprite(world, new Vector2(100, 100), finalDepth: 0.5f,
            scale: new Vector2(2, 2), origin: Vector2.Zero, size: 10);

        // (100,100)+(15,15): inside the 20×20 scaled quad (0..20), but outside an unscaled 10×10 quad.
        var inside = SpriteHitTest.Contains(sprite.Get<TransformComponent>(), sprite.Get<SpriteInfoComponent>(),
            new Vector2(115, 115));
        Assert.True(inside);

        // Far outside the scaled quad.
        var outside = SpriteHitTest.Contains(sprite.Get<TransformComponent>(), sprite.Get<SpriteInfoComponent>(),
            new Vector2(140, 140));
        Assert.False(outside);

        // Now a rotated sprite: rotate 90° about top-left origin. The local +X axis maps to world +Y.
        var rotated = MakeSprite(world, new Vector2(200, 200), finalDepth: 0.5f,
            rotation: MathHelper.PiOver2, origin: Vector2.Zero, size: 10);
        // Local (8,1) under +90° about origin → world ≈ (200 - 1, 200 + 8) = (199, 208): inside the rotated quad.
        var rotInside = SpriteHitTest.Contains(rotated.Get<TransformComponent>(), rotated.Get<SpriteInfoComponent>(),
            new Vector2(199, 208));
        Assert.True(rotInside);
        // A point in the unrotated quad footprint (203,203) is NOT inside the rotated quad.
        var rotOutside = SpriteHitTest.Contains(rotated.Get<TransformComponent>(), rotated.Get<SpriteInfoComponent>(),
            new Vector2(203, 203));
        Assert.False(rotOutside);
    }

    // ---- SelectionOrderingTest: exact-depth tie → selection-owned tiebreak (MAX EditorId = seen later) ----

    [Fact]
    public void SelectionOrderingTest()
    {
        using var world = new World();

        // Two overlapping sprites at the SAME final depth: the renderer's private insertion index can't
        // be observed, so selection breaks the tie by its own stable EditorId (seen later wins = drawn last).
        var firstCreated = MakeSprite(world, new Vector2(0, 0), finalDepth: 0.5f);
        var secondCreated = MakeSprite(world, new Vector2(0, 0), finalDepth: 0.5f);
        MakeCursor(world, new Vector2(5, 5), leftPressed: true);

        using var selection = new SelectionSystem(world);
        selection.Update(Edit());

        // EditorIds are assigned in first-seen order; the later-seen (larger id) wins the exact-depth tie.
        var firstId = firstCreated.Get<EditorIdComponent>().Id;
        var secondId = secondCreated.Get<EditorIdComponent>().Id;
        var winner = secondId > firstId ? secondCreated : firstCreated;
        Assert.Equal(winner, Selected(world));
    }

    [Fact]
    public void SelectionOrdering_TiebreakRuleIsDeterministic()
    {
        // The pure tiebreak: MAX depth wins; on an exact-depth tie, MAX id wins; first candidate always beats "no best".
        Assert.True(SelectionSystem.PickTopmost(0.5f, 0, hasBest: false, bestDepth: 0, bestId: 0));   // first candidate
        Assert.True(SelectionSystem.PickTopmost(0.8f, 0, hasBest: true, bestDepth: 0.5f, bestId: 99)); // higher depth
        Assert.False(SelectionSystem.PickTopmost(0.2f, 99, hasBest: true, bestDepth: 0.5f, bestId: 0)); // lower depth
        Assert.True(SelectionSystem.PickTopmost(0.5f, 5, hasBest: true, bestDepth: 0.5f, bestId: 3));  // tie → larger id
        Assert.False(SelectionSystem.PickTopmost(0.5f, 3, hasBest: true, bestDepth: 0.5f, bestId: 5)); // tie → smaller id loses
    }

    [Fact]
    public void Selection_ReplacesPreviousSelection_SingleSelect()
    {
        using var world = new World();
        var a = MakeSprite(world, new Vector2(0, 0), finalDepth: 0.5f);
        var b = MakeSprite(world, new Vector2(100, 0), finalDepth: 0.5f);
        var cursor = MakeCursor(world, new Vector2(5, 5), leftPressed: true);

        using var selection = new SelectionSystem(world);
        selection.Update(Edit());
        Assert.Equal(a, Selected(world));

        // Click the other sprite → single-select replaces, not adds.
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.WorldPosition = new Vector2(105, 5);
        input.LeftButtonPressed = true;
        selection.Update(Edit());

        Assert.Equal(b, Selected(world));
        Assert.False(a.Has<SelectedComponent>());
        using var set = world.GetEntities().With<SelectedComponent>().AsSet();
        var selectedCount = 0;
        foreach (var _ in set.GetEntities()) selectedCount++;
        Assert.Equal(1, selectedCount);
    }
}
