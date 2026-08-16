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
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.UI;
using CursorFactory = MonoDreams.Cursor.Cursor;

namespace MonoDreams.Tests.Ui;

/// <summary>
/// The tooltip contract (issue #95): a floating label that rides the EXISTING pointer pick — never a
/// second hit-test — appears only after its dwell, follows the cursor, flips away from the screen
/// edges, and despawns on hover-out or target death.
/// Pure logic: the system is driven font-less (its label needs a real <c>BitmapFont</c>, i.e. a
/// GraphicsDevice) with an injected measurement callback — the same callback-measurement seam the
/// layout slots use — so the panel, its placement and its lifecycle are all observable headless.
/// </summary>
public class TooltipTests
{
    private sealed class TestInput : AInputState;

    private const float Dwell = 0.4f;

    private static GameState Frame(float totalSeconds, RunMode mode = RunMode.Play) =>
        new(new GameTime(TimeSpan.FromSeconds(totalSeconds), TimeSpan.Zero)) { RunMode = mode };

    /// An 800×600 virtual surface — the box the edge-flip works against.
    private static ViewportManager Viewport() =>
        new(null, 800, 600) { ScreenWidth = 800, ScreenHeight = 600 };

    private static UIFocusSystem Focus(World world) =>
        new(world, new TestInput(), new TestInput(), new TestInput(), new TestInput(),
            new TestInput(), new TestInput(), new TestInput());

    /// 10 px per character, 20 px tall — deterministic and font-free.
    private static Vector2 Measure(string text) => new(text.Length * 10f, 20f);

    private static TooltipStyle Style() => new()
    {
        Delay = Dwell, Padding = new Vector2(10f, 6f), Offset = new Vector2(16f, 20f), ScreenMargin = 6f,
    };

    private static TooltipSystem Tooltip(World world, TooltipStyle? style = null) =>
        new(world, Viewport(), font: null, style ?? Style(), RenderTargetID.HUD, Measure);

    private static Entity MakeCursor(World world) => CursorFactory.CreateMesh(world, Triangle(), RenderTargetID.HUD);

    private static MeshData Triangle() => new(
        [
            new VertexPositionColor(new Vector3(0, 0, 0), Color.White),
            new VertexPositionColor(new Vector3(8, 0, 0), Color.White),
            new VertexPositionColor(new Vector3(0, 8, 0), Color.White),
        ],
        [0, 1, 2]);

    private static void Point(Entity cursor, Vector2 at)
    {
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.Delta = at - input.VirtualPosition;
        input.VirtualPosition = at;
        input.WorldPosition = at;
    }

    /// A HUD-space focusable box carrying (optionally) a tooltip.
    private static Entity MakeButton(
        World world, Vector2 pos, Vector2 size, string tooltip, float? delay = null, bool disabled = false)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(pos));
        e.Set(new FocusableComponent
        {
            Group = 0, Disabled = disabled, Size = size, Target = RenderTargetID.HUD,
        });
        if (tooltip != null) e.Set(new TooltipComponent { Text = tooltip, Delay = delay });
        return e;
    }

    /// The live tooltip entities the system owns (it tags them `EntityInfoComponent("Tooltip", …)`).
    private static List<Entity> Panels(World world)
    {
        using var set = world.GetEntities().With<EntityInfoComponent>().With<DrawComponent>().AsSet();
        var found = new List<Entity>();
        foreach (var e in set.GetEntities())
            if (e.Get<EntityInfoComponent>().Type == "Tooltip") found.Add(e);
        return found;
    }

    private static bool Showing(World world) => Panels(world).Count > 0;

    // ── Dwell ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// The mosquito test: an instant tooltip on every pointer crossing is the failure mode the delay
    /// exists to prevent. Nothing shows until the pointer has RESTED on the entity for the dwell.
    /// </summary>
    [Fact]
    public void Tooltip_AppearsOnlyAfterTheHoverDelay()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        MakeButton(world, new Vector2(100, 100), new Vector2(80, 30), "Primary");
        using var focus = Focus(world);
        using var tooltip = Tooltip(world);

        Point(cursor, new Vector2(120, 110));
        focus.Update(Frame(0f));
        tooltip.Update(Frame(0f));
        Assert.False(Showing(world));

        focus.Update(Frame(0.3f));
        tooltip.Update(Frame(0.3f));
        Assert.False(Showing(world)); // still inside the dwell

        focus.Update(Frame(0.5f));
        tooltip.Update(Frame(0.5f));
        Assert.True(Showing(world));
    }

    /// <summary>An unlabeled icon cap wants its name immediately: a per-entity <c>Delay = 0</c> wins
    /// over the style's default dwell.</summary>
    [Fact]
    public void PerEntityDelay_OverridesTheStyleDefault()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        MakeButton(world, new Vector2(100, 100), new Vector2(80, 30), "Add to favourites", delay: 0f);
        using var focus = Focus(world);
        using var tooltip = Tooltip(world);

        Point(cursor, new Vector2(120, 110));
        focus.Update(Frame(0f));
        tooltip.Update(Frame(0f));

        Assert.True(Showing(world));
    }

    /// <summary>Empty text mutes a tooltip without removing the component.</summary>
    [Fact]
    public void EmptyTooltipText_NeverShows()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        MakeButton(world, new Vector2(100, 100), new Vector2(80, 30), "", delay: 0f);
        using var focus = Focus(world);
        using var tooltip = Tooltip(world);

        Point(cursor, new Vector2(120, 110));
        focus.Update(Frame(0f));
        tooltip.Update(Frame(0f));

        Assert.False(Showing(world));
    }

    // ── The single-pick invariant ───────────────────────────────────────────────

    /// <summary>
    /// The load-bearing case. The system reads the PICK, not geometry: with the pick already resting
    /// on the button, moving the pointer far outside its bounds without re-running the pick's owner
    /// still shows that button's tooltip — and it has moved to the new pointer position. A system
    /// doing its own hit-test would have hidden it.
    /// </summary>
    [Fact]
    public void Tooltip_FollowsThePick_NotItsOwnHitTest()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        MakeButton(world, new Vector2(100, 100), new Vector2(80, 30), "Primary", delay: 0f);
        using var focus = Focus(world);
        using var tooltip = Tooltip(world);

        Point(cursor, new Vector2(120, 110));
        focus.Update(Frame(0f));
        tooltip.Update(Frame(0f));
        var first = Panels(world)[0].Get<TransformComponent>().Position;

        Point(cursor, new Vector2(400, 300)); // nowhere near the button — but the pick still names it
        tooltip.Update(Frame(0.1f));

        Assert.True(Showing(world));
        Assert.NotEqual(first, Panels(world)[0].Get<TransformComponent>().Position);
    }

    /// <summary>
    /// The mirror image: the pointer sits squarely inside a tooltip'd entity's bounds, but that entity
    /// is disabled so the pick skips it — and no tooltip appears. A second hit-test would have shown
    /// one for a control a click can't reach.
    /// </summary>
    [Fact]
    public void PointerInsideADisabledControl_ShowsNoTooltip()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        MakeButton(world, new Vector2(100, 100), new Vector2(80, 30), "Primary", delay: 0f, disabled: true);
        using var focus = Focus(world);
        using var tooltip = Tooltip(world);

        Point(cursor, new Vector2(120, 110));
        focus.Update(Frame(0f));
        tooltip.Update(Frame(0f));

        Assert.False(Showing(world));
    }

    /// <summary>A picked entity with no <see cref="TooltipComponent"/> simply has nothing to say.</summary>
    [Fact]
    public void PickedEntityWithoutATooltipComponent_ShowsNothing()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        MakeButton(world, new Vector2(100, 100), new Vector2(80, 30), tooltip: null);
        using var focus = Focus(world);
        using var tooltip = Tooltip(world);

        Point(cursor, new Vector2(120, 110));
        focus.Update(Frame(0f));
        tooltip.Update(Frame(1f));

        Assert.False(Showing(world));
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────────

    /// <summary>Hover-out despawns the label — it is transient, never a leaked entity.</summary>
    [Fact]
    public void HoverOut_DespawnsTheTooltip()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        MakeButton(world, new Vector2(100, 100), new Vector2(80, 30), "Primary", delay: 0f);
        using var focus = Focus(world);
        using var tooltip = Tooltip(world);

        Point(cursor, new Vector2(120, 110));
        focus.Update(Frame(0f));
        tooltip.Update(Frame(0f));
        Assert.True(Showing(world));

        Point(cursor, new Vector2(600, 500));
        focus.Update(Frame(0.1f));
        tooltip.Update(Frame(0.1f));

        Assert.False(Showing(world));
    }

    /// <summary>
    /// Target death is the same case as hover-out: the pick still names the entity (its owner has not
    /// run again), so the system must re-check that the pick names a LIVE entity — otherwise the label
    /// of a despawned control hangs on screen.
    /// </summary>
    [Fact]
    public void TargetDies_DespawnsTheTooltip()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        var button = MakeButton(world, new Vector2(100, 100), new Vector2(80, 30), "Primary", delay: 0f);
        using var focus = Focus(world);
        using var tooltip = Tooltip(world);

        Point(cursor, new Vector2(120, 110));
        focus.Update(Frame(0f));
        tooltip.Update(Frame(0f));
        Assert.True(Showing(world));

        button.Dispose(); // the pick is now a dangling reference
        tooltip.Update(Frame(0.1f));

        Assert.False(Showing(world));
    }

    /// <summary>Moving to another control rebuilds the label rather than reusing a stale panel, and
    /// never leaves two on screen.</summary>
    [Fact]
    public void MovingToAnotherControl_RebuildsASingleTooltip()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        MakeButton(world, new Vector2(0, 0), new Vector2(50, 50), "First", delay: 0f);
        MakeButton(world, new Vector2(200, 0), new Vector2(50, 50), "Second", delay: 0f);
        using var focus = Focus(world);
        using var tooltip = Tooltip(world);

        Point(cursor, new Vector2(10, 10));
        focus.Update(Frame(0f));
        tooltip.Update(Frame(0f));
        var firstPanel = Panels(world)[0];

        Point(cursor, new Vector2(210, 10));
        focus.Update(Frame(0.1f));
        tooltip.Update(Frame(0.1f));

        Assert.Single(Panels(world));
        Assert.False(firstPanel.IsAlive);
    }

    /// <summary>Disposing the system takes its entities with it — no orphan on a screen teardown.</summary>
    [Fact]
    public void DisposingTheSystem_DespawnsTheTooltip()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        MakeButton(world, new Vector2(100, 100), new Vector2(80, 30), "Primary", delay: 0f);
        using var focus = Focus(world);
        var tooltip = Tooltip(world);

        Point(cursor, new Vector2(120, 110));
        focus.Update(Frame(0f));
        tooltip.Update(Frame(0f));
        Assert.True(Showing(world));

        tooltip.Dispose();

        Assert.False(Showing(world));
    }

    /// <summary>
    /// The editor case. An editor-capable screen registers the tooltip <c>Freeze</c> (it is a
    /// play-only pointer cosmetic), so Play → Pause with a label on screen never runs
    /// <c>Update</c> again to hide it — while the prep + render pass, which does NOT freeze, would
    /// keep drawing the orphaned panel on the HUD for the rest of the session. The gate hands the
    /// system its <c>ISuspendableSystem.Suspend</c> instead, and it comes back on resume.
    /// </summary>
    [Fact]
    public void FreezingTheSystem_DespawnsTheTooltip()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        MakeButton(world, new Vector2(100, 100), new Vector2(80, 30), "Primary", delay: 0f);
        using var focus = Focus(world);
        using var gate = new GatedSystem(Tooltip(world), EditTimeBehavior.Freeze);

        Point(cursor, new Vector2(120, 110));
        focus.Update(Frame(0f));
        gate.Update(Frame(0f));
        Assert.True(Showing(world));

        gate.Update(Frame(0.1f, RunMode.Edit)); // the transport's Pause
        Assert.False(Showing(world));

        focus.Update(Frame(0.2f));
        gate.Update(Frame(0.2f)); // Play again — the teardown was not a kill switch
        Assert.True(Showing(world));
    }

    /// <summary>The system's own kill switch reads the same way: a disabled tooltip system means NO
    /// tooltip, not a frozen one left on screen.</summary>
    [Fact]
    public void DisablingTheSystem_DespawnsTheTooltip()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        MakeButton(world, new Vector2(100, 100), new Vector2(80, 30), "Primary", delay: 0f);
        using var focus = Focus(world);
        using var tooltip = Tooltip(world);

        Point(cursor, new Vector2(120, 110));
        focus.Update(Frame(0f));
        tooltip.Update(Frame(0f));
        Assert.True(Showing(world));

        tooltip.IsEnabled = false;
        tooltip.Update(Frame(0.1f));

        Assert.False(Showing(world));
    }

    /// <summary>The label is screen-space by construction: a world-space target is refused loudly at
    /// composition time rather than silently drawing the panel under the camera transform.</summary>
    [Fact]
    public void AWorldSpaceTarget_IsRefused()
    {
        using var world = new World();
        Assert.Throws<ArgumentException>(() =>
            new TooltipSystem(world, Viewport(), font: null, Style(), RenderTargetID.Main, Measure));
    }

    // ── Placement: riding the pointer and flipping at the edges ─────────────────

    /// <summary>The resting case: below-right of the pointer by the style's offset.</summary>
    [Fact]
    public void Place_AwayFromEveryEdge_SitsBelowRightOfThePointer()
    {
        var at = TooltipPlacement.Place(
            new Vector2(100, 100), new Vector2(120, 32), new Vector2(16, 20), 6f, new Vector2(800, 600));

        Assert.Equal(new Vector2(116, 120), at);
    }

    /// <summary>The right-most icon: the label opens LEFTWARDS instead of sliding off screen.</summary>
    [Fact]
    public void Place_NearTheRightEdge_FlipsToTheLeftOfThePointer()
    {
        var size = new Vector2(120, 32);
        var at = TooltipPlacement.Place(new Vector2(780, 100), size, new Vector2(16, 20), 6f, new Vector2(800, 600));

        Assert.Equal(780 - 16 - size.X, at.X);
        Assert.True(at.X + size.X <= 800 - 6f);
        Assert.Equal(120, at.Y); // the vertical axis is untouched
    }

    /// <summary>The bottom edge flips independently of the horizontal one.</summary>
    [Fact]
    public void Place_NearTheBottomEdge_FlipsAboveThePointer()
    {
        var size = new Vector2(120, 32);
        var at = TooltipPlacement.Place(new Vector2(100, 590), size, new Vector2(16, 20), 6f, new Vector2(800, 600));

        Assert.Equal(116, at.X);
        Assert.Equal(590 - 20 - size.Y, at.Y);
    }

    /// <summary>The bottom-right corner flips on both axes at once.</summary>
    [Fact]
    public void Place_InTheBottomRightCorner_FlipsOnBothAxes()
    {
        var size = new Vector2(120, 32);
        var screen = new Vector2(800, 600);
        var at = TooltipPlacement.Place(new Vector2(790, 590), size, new Vector2(16, 20), 6f, screen);

        Assert.True(at.X + size.X <= screen.X - 6f);
        Assert.True(at.Y + size.Y <= screen.Y - 6f);
        Assert.True(at is { X: >= 6f, Y: >= 6f });
    }

    /// <summary>A label too big for either side is clamped inside the margins — never pushed out of
    /// view, and never handed a NaN by an inverted clamp range.</summary>
    [Fact]
    public void Place_LabelWiderThanTheScreen_ClampsToTheMargin()
    {
        var at = TooltipPlacement.Place(
            new Vector2(400, 300), new Vector2(1200, 900), new Vector2(16, 20), 6f, new Vector2(800, 600));

        Assert.Equal(new Vector2(6, 6), at);
    }

    /// <summary>End to end: the placed panel is where the pure math says it should be, so the system
    /// and the (tested) placement never drift apart.</summary>
    [Fact]
    public void ShownTooltip_SitsWhereThePlacementMathSays()
    {
        using var world = new World();
        var cursor = MakeCursor(world);
        MakeButton(world, new Vector2(100, 100), new Vector2(80, 30), "Primary", delay: 0f);
        using var focus = Focus(world);
        using var tooltip = Tooltip(world);

        var pointer = new Vector2(120, 110);
        Point(cursor, pointer);
        focus.Update(Frame(0f));
        tooltip.Update(Frame(0f));

        var style = Style();
        var expected = TooltipPlacement.Place(
            pointer, Measure("Primary") + style.Padding * 2f, style.Offset, style.ScreenMargin,
            new Vector2(800, 600));

        Assert.Equal(expected, Panels(world)[0].Get<TransformComponent>().Position);
    }
}
