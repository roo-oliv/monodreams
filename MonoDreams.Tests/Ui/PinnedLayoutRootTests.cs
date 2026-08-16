using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.UI;

namespace MonoDreams.Tests.Ui;

/// <summary>
/// Protects the pinned-layout-root primitive (issue #94): N independent layout roots, each placed at
/// an arbitrary screen position, instead of one implicit solver container that stacks every root like
/// flyers on a bulletin board. Two things are load-bearing and covered here — pinned roots are OUT OF
/// the solver's flow (they neither stack nor push anchored roots around), and
/// <see cref="PinnedLayoutRootSystem"/> occupies exactly one pipeline slot: AFTER
/// <see cref="AutoLayoutSystem"/> (which would otherwise overwrite the placement) and BEFORE
/// <see cref="HierarchySystem"/> and every other world-position consumer (which would otherwise read
/// the un-pinned position). That ordering IS the feature.
/// </summary>
public class PinnedLayoutRootTests
{
    private const int ScreenW = 800;
    private const int ScreenH = 600;

    /// Layout's (0,0) — the screen's top-left — expressed in the centre-origin world space that
    /// Main/UI-target entities live in.
    private static readonly Vector2 TopLeftOrigin = new(-ScreenW / 2f, -ScreenH / 2f);

    private static readonly Vector2 ContentSize = new(200, 100);

    private static GameState Frame() => new(new GameTime());

    private static ViewportManager Viewport() => new(null, ScreenW, ScreenH);

    /// A root holding one slot whose content measures <paramref name="size"/>, so the root hugs to
    /// exactly that size. Returns the root slot entity and the content entity under it.
    private static (Entity root, Entity content) Root(
        World world,
        ViewportManager viewport,
        Vector2? pinnedAt = null,
        ScreenAnchor anchor = ScreenAnchor.TopLeft,
        RenderTargetID target = RenderTargetID.Main,
        Vector2? size = null)
    {
        var measured = size ?? ContentSize;
        var content = world.CreateEntity();
        content.Set(new TransformComponent(Vector2.Zero));

        var builder = new AutoLayoutBuilder(world, viewport);
        var container = pinnedAt.HasValue
            ? builder.CreatePinnedRoot(pinnedAt.Value, anchor, target)
            : builder.CreateRoot(anchor, target);

        var root = container
            .AddSlot(slot => slot.Attach(content).MeasureWith(_ => measured))
            .Build();

        return (root, content);
    }

    /// The canonical frame order for a UI screen: measure → solve → place pinned roots → hierarchy.
    private static void RunFrame(
        IntrinsicSizingSystem intrinsic,
        AutoLayoutSystem layout,
        PinnedLayoutRootSystem pin,
        HierarchySystem hierarchy)
    {
        intrinsic.Update(Frame());
        layout.Update(Frame());
        pin.Update(Frame());
        hierarchy.Update(Frame());
    }

    // ── Out of flow: pinned roots don't stack, and they don't move anchored roots ──────────────

    [Fact]
    public void TwoPinnedRoots_EachLandAtTheirOwnPosition_InsteadOfStacking()
    {
        using var world = new World();
        var vm = Viewport();
        var (a, _) = Root(world, vm, pinnedAt: new Vector2(40, 30));
        var (b, _) = Root(world, vm, pinnedAt: new Vector2(500, 400));

        using var intrinsic = new IntrinsicSizingSystem(world);
        using var layout = new AutoLayoutSystem(world, vm);
        using var pin = new PinnedLayoutRootSystem(world, vm);
        using var hierarchy = new HierarchySystem(world);
        RunFrame(intrinsic, layout, pin, hierarchy);

        Assert.Equal(TopLeftOrigin + new Vector2(40, 30), a.Get<TransformComponent>().Position);
        Assert.Equal(TopLeftOrigin + new Vector2(500, 400), b.Get<TransformComponent>().Position);
    }

    /// The behavior this primitive exists to replace: a second ANCHORED root is pushed down by the
    /// first one's height (they share the implicit solver container), while a second PINNED root at
    /// the same anchor lands exactly where the first one is.
    [Fact]
    public void SecondAnchoredRoot_Stacks_WhileSecondPinnedRoot_DoesNot()
    {
        var expectedCentre = new Vector2(-ContentSize.X / 2f, -ContentSize.Y / 2f);

        using (var stacked = new World())
        {
            var vm = Viewport();
            var (first, _) = Root(stacked, vm, anchor: ScreenAnchor.Center);
            var (second, _) = Root(stacked, vm, anchor: ScreenAnchor.Center);

            using var intrinsic = new IntrinsicSizingSystem(stacked);
            using var layout = new AutoLayoutSystem(stacked, vm);
            using var pin = new PinnedLayoutRootSystem(stacked, vm);
            using var hierarchy = new HierarchySystem(stacked);
            RunFrame(intrinsic, layout, pin, hierarchy);

            Assert.Equal(expectedCentre, first.Get<TransformComponent>().Position);
            // Pushed down by the first root's height — the bulletin-board behavior.
            Assert.Equal(
                expectedCentre + new Vector2(0, ContentSize.Y),
                second.Get<TransformComponent>().Position);
        }

        using (var pinned = new World())
        {
            var vm = Viewport();
            var (first, _) = Root(pinned, vm, anchor: ScreenAnchor.Center);
            var (second, _) = Root(pinned, vm, pinnedAt: Vector2.Zero, anchor: ScreenAnchor.Center);

            using var intrinsic = new IntrinsicSizingSystem(pinned);
            using var layout = new AutoLayoutSystem(pinned, vm);
            using var pin = new PinnedLayoutRootSystem(pinned, vm);
            using var hierarchy = new HierarchySystem(pinned);
            RunFrame(intrinsic, layout, pin, hierarchy);

            // The anchored root keeps its place, and the pinned root sits on the same centre.
            Assert.Equal(expectedCentre, first.Get<TransformComponent>().Position);
            Assert.Equal(expectedCentre, second.Get<TransformComponent>().Position);
        }
    }

    /// A pinned root is out of flow in BOTH directions: adding one must not shift the anchored roots
    /// that were already laid out.
    [Fact]
    public void AddingAPinnedRoot_DoesNotMoveAnAlreadyAnchoredRoot()
    {
        using var world = new World();
        var vm = Viewport();
        var (anchored, _) = Root(world, vm, anchor: ScreenAnchor.TopCenter);

        using var intrinsic = new IntrinsicSizingSystem(world);
        using var layout = new AutoLayoutSystem(world, vm);
        using var pin = new PinnedLayoutRootSystem(world, vm);
        using var hierarchy = new HierarchySystem(world);
        RunFrame(intrinsic, layout, pin, hierarchy);
        var baseline = anchored.Get<TransformComponent>().Position;

        Root(world, vm, pinnedAt: new Vector2(10, 10));
        RunFrame(intrinsic, layout, pin, hierarchy);

        Assert.Equal(baseline, anchored.Get<TransformComponent>().Position);
    }

    // ── Placement: anchor + offset, resolved against the SOLVED size, per render target ────────

    /// The exact expression the ui demo used to hand-roll after every layout pass
    /// (<c>new Vector2(-w / 2f, -h / 2f)</c>) is now what a Center-anchored pin computes.
    [Fact]
    public void CentreAnchoredPin_CentresTheRootOnItsSolvedSize()
    {
        using var world = new World();
        var vm = Viewport();
        var (root, _) = Root(world, vm, pinnedAt: Vector2.Zero, anchor: ScreenAnchor.Center);

        using var intrinsic = new IntrinsicSizingSystem(world);
        using var layout = new AutoLayoutSystem(world, vm);
        using var pin = new PinnedLayoutRootSystem(world, vm);
        using var hierarchy = new HierarchySystem(world);
        RunFrame(intrinsic, layout, pin, hierarchy);

        ref readonly var slot = ref root.Get<LayoutSlotComponent>();
        Assert.Equal(ContentSize.X, slot.ComputedWidth);
        Assert.Equal(ContentSize.Y, slot.ComputedHeight);
        Assert.Equal(
            new Vector2(-slot.ComputedWidth / 2f, -slot.ComputedHeight / 2f),
            root.Get<TransformComponent>().Position);
    }

    [Fact]
    public void PinOffset_IsMeasuredFromItsOwnAnchor()
    {
        using var world = new World();
        var vm = Viewport();
        var (root, _) = Root(
            world, vm, pinnedAt: new Vector2(-24, -16), anchor: ScreenAnchor.BottomRight);

        using var intrinsic = new IntrinsicSizingSystem(world);
        using var layout = new AutoLayoutSystem(world, vm);
        using var pin = new PinnedLayoutRootSystem(world, vm);
        using var hierarchy = new HierarchySystem(world);
        RunFrame(intrinsic, layout, pin, hierarchy);

        // Bottom-right corner, inset by the root's own size and then by the offset.
        var expected = new Vector2(
            ScreenW / 2f - ContentSize.X - 24,
            ScreenH / 2f - ContentSize.Y - 16);
        Assert.Equal(expected, root.Get<TransformComponent>().Position);
    }

    /// HUD entities render without the camera transform, so their layout output lives in
    /// top-left-origin screen space — the pin uses the same anchor math as an anchored root and must
    /// land in that space too.
    [Fact]
    public void PinnedHudRoot_LandsInTopLeftOriginScreenSpace()
    {
        using var world = new World();
        var vm = Viewport();
        var (root, _) = Root(
            world, vm, pinnedAt: new Vector2(32, 24), target: RenderTargetID.HUD);

        using var intrinsic = new IntrinsicSizingSystem(world);
        using var layout = new AutoLayoutSystem(world, vm);
        using var pin = new PinnedLayoutRootSystem(world, vm);
        using var hierarchy = new HierarchySystem(world);
        RunFrame(intrinsic, layout, pin, hierarchy);

        Assert.Equal(new Vector2(32, 24), root.Get<TransformComponent>().Position);
    }

    /// Pinning is a ROOT-placement primitive. A non-root slot keeps the position its parent
    /// container gave it — the system must not fight the solver inside a tree.
    [Fact]
    public void PinComponentOnANonRootSlot_IsIgnored()
    {
        using var world = new World();
        var vm = Viewport();
        var content = world.CreateEntity();
        content.Set(new TransformComponent(Vector2.Zero));

        Entity childSlot = default;
        new AutoLayoutBuilder(world, vm)
            .CreateRoot(ScreenAnchor.TopLeft)
            .Padding(20)
            .AddSlot(slot => slot.Attach(content).MeasureWith(_ => ContentSize))
            .Build();

        foreach (var entity in world.GetEntities().With<LayoutSlotComponent>().AsSet().GetEntities())
        {
            if (entity.Get<LayoutSlotComponent>().IsRoot) continue;
            childSlot = entity;
        }
        childSlot.Set(new PinnedLayoutRootComponent { Offset = new Vector2(999, 999) });

        using var intrinsic = new IntrinsicSizingSystem(world);
        using var layout = new AutoLayoutSystem(world, vm);
        using var pin = new PinnedLayoutRootSystem(world, vm);
        using var hierarchy = new HierarchySystem(world);
        RunFrame(intrinsic, layout, pin, hierarchy);

        // The container's padding, not the pin offset.
        Assert.Equal(new Vector2(20, 20), childSlot.Get<TransformComponent>().Position);
    }

    // ── The ordering IS the feature ───────────────────────────────────────────────────────────

    /// Run the pin BEFORE the solver and the solver overwrites it: the root falls back to its bare
    /// anchor with the offset dropped. This is why the system's slot is post-AutoLayout.
    [Fact]
    public void PinBeforeAutoLayout_IsOverwrittenBySolver()
    {
        using var world = new World();
        var vm = Viewport();
        var (root, _) = Root(world, vm, pinnedAt: new Vector2(120, 90));

        using var intrinsic = new IntrinsicSizingSystem(world);
        using var layout = new AutoLayoutSystem(world, vm);
        using var pin = new PinnedLayoutRootSystem(world, vm);

        intrinsic.Update(Frame());
        pin.Update(Frame());    // too early
        layout.Update(Frame()); // …and the solver's own write wins

        Assert.Equal(TopLeftOrigin, root.Get<TransformComponent>().Position);
        Assert.NotEqual(TopLeftOrigin + new Vector2(120, 90), root.Get<TransformComponent>().Position);
    }

    /// Run the pin BEFORE hierarchy (and before every other world-position consumer — mesh prep,
    /// debug overlays, culling, render) and the whole tree reads the pinned position the same frame.
    [Fact]
    public void PinBeforeHierarchy_PutsDescendantsAtThePinnedPositionThisFrame()
    {
        using var world = new World();
        var vm = Viewport();
        var (_, content) = Root(world, vm, pinnedAt: new Vector2(120, 90));

        using var intrinsic = new IntrinsicSizingSystem(world);
        using var layout = new AutoLayoutSystem(world, vm);
        using var pin = new PinnedLayoutRootSystem(world, vm);
        using var hierarchy = new HierarchySystem(world);
        RunFrame(intrinsic, layout, pin, hierarchy);

        // A prep-style consumer reads the content entity's WORLD position (root → slot → content).
        Assert.Equal(
            TopLeftOrigin + new Vector2(120, 90),
            content.Get<TransformComponent>().WorldPosition);
    }

    /// The mirror image: a world-position consumer that runs between the solver and the pin reads the
    /// un-pinned position — the one-frame-stale mesh / overlay this ordering exists to prevent.
    [Fact]
    public void PinAfterAWorldPositionConsumer_LeavesThatConsumerOnTheUnpinnedPosition()
    {
        using var world = new World();
        var vm = Viewport();
        var (_, content) = Root(world, vm, pinnedAt: new Vector2(120, 90));

        using var intrinsic = new IntrinsicSizingSystem(world);
        using var layout = new AutoLayoutSystem(world, vm);
        using var pin = new PinnedLayoutRootSystem(world, vm);
        using var hierarchy = new HierarchySystem(world);

        intrinsic.Update(Frame());
        layout.Update(Frame());
        hierarchy.Update(Frame());
        var observed = content.Get<TransformComponent>().WorldPosition; // e.g. ButtonMeshPrepSystem
        pin.Update(Frame());                                            // …too late for that consumer

        Assert.Equal(TopLeftOrigin, observed);
        Assert.NotEqual(TopLeftOrigin + new Vector2(120, 90), observed);
    }
}
