using System;
using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.Input;
using MonoDreams.State;
using MonoDreams.UI;
using CursorFactory = MonoDreams.Cursor.Cursor;

namespace MonoDreams.Tests.Ui;

/// <summary>
/// Protects the ui premise "There is ONE pointer pick": <see cref="UIFocusSystem"/> resolves what the
/// pointer is over exactly once and publishes it on the cursor entity as
/// <see cref="PointerPickComponent"/>; every system that reacts to what the pointer is over reads
/// that instead of hit-testing again. These tests pin both halves — the publication (topmost wins,
/// the same group/disabled filters focus and click use, and a dwell clock that survives re-publication)
/// and a consumer (<see cref="CursorHoverSystem"/>) that now derives its swap from the pick alone.
/// Pure logic: no GraphicsDevice, no font — the cursor mesh factory and the mesh library take plain
/// vertex arrays.
/// </summary>
public class PointerPickTests
{
    private sealed class TestInput : AInputState;

    private static GameState Frame(float totalSeconds = 0f) =>
        new(new GameTime(TimeSpan.FromSeconds(totalSeconds), TimeSpan.Zero));

    /// The real focus system with never-pressed nav inputs, so only the POINTER pass can move focus
    /// or resolve a pick — which is exactly what these tests are about.
    private static UIFocusSystem Focus(World world, Func<int>? activeGroup = null) =>
        new(world, new TestInput(), new TestInput(), new TestInput(), new TestInput(),
            new TestInput(), new TestInput(), new TestInput(), activeGroup);

    private static MeshData Triangle(int size) => new(
        [
            new VertexPositionColor(new Vector3(0, 0, 0), Color.White),
            new VertexPositionColor(new Vector3(size, 0, 0), Color.White),
            new VertexPositionColor(new Vector3(0, size, 0), Color.White),
        ],
        [0, 1, 2]);

    /// The cursor through its own module's factory (the contract entry point), on the mesh path so no
    /// texture asset is needed.
    private static Entity MakeCursor(World world) =>
        CursorFactory.CreateMesh(world, Triangle(8), RenderTargetID.HUD);

    /// Points the cursor at a virtual-screen position. <paramref name="moved"/> mirrors a real mouse
    /// move (the pointer only steals focus when it actually moves); the pick itself is move-independent.
    private static void Point(Entity cursor, Vector2 at, bool moved = true)
    {
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.Delta = moved ? at - input.VirtualPosition : Vector2.Zero;
        input.VirtualPosition = at;
        input.WorldPosition = at;
    }

    /// A HUD-space focusable box at <paramref name="pos"/>.
    private static Entity MakeFocusable(
        World world, Vector2 pos, Vector2 size, int group = 0, bool disabled = false,
        CursorType hoverCursor = CursorType.Default, int tabIndex = 0)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(pos));
        e.Set(new FocusableComponent
        {
            TabIndex = tabIndex, Group = group, Disabled = disabled, Size = size,
            Target = RenderTargetID.HUD, HoverCursor = hoverCursor,
        });
        return e;
    }

    private static PointerPickComponent Pick(Entity cursor) => cursor.Get<PointerPickComponent>();

    // ── Publication ─────────────────────────────────────────────────────────────

    /// <summary>The pick names the focusable under the pointer, stamped with the time the hover began.</summary>
    [Fact]
    public void PointerOverAFocusable_PublishesItAsThePickWithTheHoverStartTime()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var box = MakeFocusable(world, new Vector2(100, 100), new Vector2(80, 30));
        using var focus = Focus(world);

        Point(cursor, new Vector2(120, 110));
        focus.Update(Frame(2.5f));

        Assert.Equal(box, Pick(cursor).Hovered);
        Assert.Equal(2.5f, Pick(cursor).HoverStartTime, 3);
    }

    /// <summary>
    /// The dwell clock: holding the same entity leaves <c>HoverStartTime</c> alone across frames, so a
    /// consumer's delay is one subtraction. (This is what makes the tooltip's hover delay free.)
    /// </summary>
    [Fact]
    public void PointerHoldsTheSameFocusable_KeepsTheOriginalHoverStartTime()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        MakeFocusable(world, new Vector2(100, 100), new Vector2(80, 30));
        using var focus = Focus(world);

        Point(cursor, new Vector2(120, 110));
        focus.Update(Frame(1f));
        Point(cursor, new Vector2(125, 112)); // still inside the same box
        focus.Update(Frame(1.6f));

        Assert.Equal(1f, Pick(cursor).HoverStartTime, 3);
    }

    /// <summary>Moving to a different focusable restarts the dwell clock.</summary>
    [Fact]
    public void PointerMovesToAnotherFocusable_RestartsTheHoverStartTime()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        MakeFocusable(world, new Vector2(0, 0), new Vector2(50, 50), tabIndex: 0);
        var second = MakeFocusable(world, new Vector2(200, 0), new Vector2(50, 50), tabIndex: 1);
        using var focus = Focus(world);

        Point(cursor, new Vector2(10, 10));
        focus.Update(Frame(1f));
        Point(cursor, new Vector2(210, 10));
        focus.Update(Frame(1.6f));

        Assert.Equal(second, Pick(cursor).Hovered);
        Assert.Equal(1.6f, Pick(cursor).HoverStartTime, 3);
    }

    /// <summary>Pointer over nothing ⇒ the pick is published as "nothing", not left stale.</summary>
    [Fact]
    public void PointerOverNothing_PublishesAnEmptyPick()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        MakeFocusable(world, new Vector2(100, 100), new Vector2(80, 30));
        using var focus = Focus(world);

        Point(cursor, new Vector2(120, 110));
        focus.Update(Frame(1f));
        Point(cursor, new Vector2(600, 400)); // off the box
        focus.Update(Frame(1.1f));

        Assert.False(Pick(cursor).Hovered.IsAlive);
    }

    /// <summary>
    /// The pick uses the SAME filters focus and click use: a tab-gated or control-disabled focusable,
    /// and one outside the active group, is not picked even with the pointer squarely on it. This is
    /// what makes "the tooltip shows for exactly what a click would act on" true by construction.
    /// </summary>
    [Fact]
    public void DisabledOrOutOfGroupFocusable_IsNeverPicked()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        MakeFocusable(world, new Vector2(100, 100), new Vector2(80, 30), disabled: true);
        MakeFocusable(world, new Vector2(300, 100), new Vector2(80, 30), group: 100);
        using var focus = Focus(world); // active group defaults to 0

        Point(cursor, new Vector2(120, 110));
        focus.Update(Frame(1f));
        Assert.False(Pick(cursor).Hovered.IsAlive);

        Point(cursor, new Vector2(320, 110));
        focus.Update(Frame(1.1f));
        Assert.False(Pick(cursor).Hovered.IsAlive);
    }

    /// <summary>A world with no focusables at all still publishes an (empty) pick.</summary>
    [Fact]
    public void NoFocusablesAtAll_StillPublishesAnEmptyPick()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        using var focus = Focus(world);

        Point(cursor, new Vector2(10, 10));
        focus.Update(Frame());

        Assert.True(cursor.Has<PointerPickComponent>());
        Assert.False(Pick(cursor).Hovered.IsAlive);
    }

    // ── Consumption: CursorHoverSystem swaps off the pick, never its own hit-test ────

    private static (Entity cursor, MeshData arrow, MeshData hand) CursorWithLibrary(World world)
    {
        var cursor = MakeCursor(world);
        var arrow = Triangle(8);
        var hand = Triangle(16);
        cursor.Set(new CursorMeshLibraryComponent
        {
            Meshes = new Dictionary<CursorType, MeshData> { [CursorType.Default] = arrow, [CursorType.Hand] = hand },
        });
        return (cursor, arrow, hand);
    }

    /// <summary>
    /// The hand appears over a focusable that asks for it, and the cursor's mesh is swapped to the
    /// library's Hand entry — driven purely by the pick the focus system published.
    /// </summary>
    [Fact]
    public void PickedFocusableRequestingAHand_SwapsTheCursorTypeAndMesh()
    {
        using var world = new World();
        var (cursor, _, hand) = CursorWithLibrary(world);
        MakeFocusable(world, new Vector2(100, 100), new Vector2(80, 30), hoverCursor: CursorType.Hand);
        using var focus = Focus(world);
        using var hover = new CursorHoverSystem(world);

        Point(cursor, new Vector2(120, 110));
        focus.Update(Frame(1f));
        hover.Update(Frame(1f));

        Assert.Equal(CursorType.Hand, cursor.Get<CursorControllerComponent>().Type);
        Assert.Same(hand.Indices, cursor.Get<DrawComponent>().Indices);

        // …and back to the arrow when the pick moves off it.
        Point(cursor, new Vector2(600, 400));
        focus.Update(Frame(1.1f));
        hover.Update(Frame(1.1f));

        Assert.Equal(CursorType.Default, cursor.Get<CursorControllerComponent>().Type);
    }

    /// <summary>
    /// The consumer inherits the pick's filters for free: a link trapped under an open overlay (its
    /// group is no longer active) no longer paints a hand, because it is not what a click would act on.
    /// Before the pick existed this system ran its own group-blind hit-test and disagreed.
    /// </summary>
    [Fact]
    public void FocusableOutsideTheActiveGroup_DoesNotSwapTheCursor()
    {
        using var world = new World();
        var (cursor, _, _) = CursorWithLibrary(world);
        MakeFocusable(world, new Vector2(100, 100), new Vector2(80, 30), hoverCursor: CursorType.Hand);
        MakeFocusable(world, new Vector2(300, 300), new Vector2(80, 30), group: 100); // the "open dialog"
        using var focus = Focus(world, activeGroup: () => 100);
        using var hover = new CursorHoverSystem(world);

        Point(cursor, new Vector2(120, 110)); // squarely over the link, which is now out of group
        focus.Update(Frame(1f));
        hover.Update(Frame(1f));

        Assert.Equal(CursorType.Default, cursor.Get<CursorControllerComponent>().Type);
    }

    /// <summary>
    /// Documented graceful degradation: without the pick's owner in the pipeline there is no pick, so
    /// the consumer stands down (resting arrow) rather than falling back to a hit-test of its own.
    /// </summary>
    [Fact]
    public void NoPickPublished_LeavesTheCursorUntouched()
    {
        using var world = new World();
        var (cursor, _, _) = CursorWithLibrary(world);
        MakeFocusable(world, new Vector2(100, 100), new Vector2(80, 30), hoverCursor: CursorType.Hand);
        using var hover = new CursorHoverSystem(world); // no UIFocusSystem registered

        Point(cursor, new Vector2(120, 110));
        hover.Update(Frame(1f));

        Assert.Equal(CursorType.Default, cursor.Get<CursorControllerComponent>().Type);
    }
}
