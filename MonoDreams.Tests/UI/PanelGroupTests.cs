using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Extension;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.UI;
using Xunit;

namespace MonoDreams.Tests.UI;

/// <summary>
/// Protects the ui premise "Exclusive panel groups PARK their inactive members; they never hide
/// them" — the invariant <see cref="PanelGroupComponent"/> / <see cref="PanelGroupSystem"/> exist to
/// make unbreakable. The headline case is the round trip: switch away from a panel, switch back, and
/// every transform in it must be byte-identical to what it was — a panel that came back at a
/// different position (or had to re-solve its layout in view) is exactly the bug parking prevents.
///
/// These run the REAL systems in the REAL pipeline order (<c>IntrinsicSizingSystem</c> →
/// <c>AutoLayoutSystem</c> → <see cref="PanelGroupSystem"/> → <c>HierarchySystem</c>), because the
/// interesting case is a layout-driven panel whose position the solver rewrites from scratch every
/// frame while it is parked.
/// </summary>
public class PanelGroupTests
{
    private static GameState Frame() => new(new GameTime());

    /// A panel: a root entity at <paramref name="position"/> with one child parented under it (the
    /// panel's content rides the root's transform, which is what parking moves).
    private static (Entity root, Entity child) MakePanel(World world, Vector2 position, Vector2 childLocal)
    {
        var root = world.CreateEntity();
        root.Set(new TransformComponent(position));

        var child = world.CreateEntity();
        child.Set(new TransformComponent(childLocal));
        child.SetParent(root);

        return (root, child);
    }

    private static Entity MakeGroup(World world, params Entity[] members)
    {
        var group = world.CreateEntity();
        group.Set(new PanelGroupComponent { Members = members, Active = 0 });
        return group;
    }

    // ── The headline premise: switch away and back → transforms identical ────────────────────────

    /// <summary>Switch away from a panel, sit on the other panel for a while, switch back: the
    /// panel's own local position AND its content's world position come back byte-identical. No
    /// tolerance — the round trip is exact by construction (the system restores the stashed home),
    /// which is what makes the switch-back frame pixel-identical instead of visibly re-solving.</summary>
    [Fact]
    public void SwitchAwayAndBack_RestoresEveryTransformIdentically()
    {
        using var world = new World();
        using var panels = new PanelGroupSystem(world);
        using var hierarchy = new HierarchySystem(world);

        var (first, firstChild) = MakePanel(world, new Vector2(120f, -40f), new Vector2(12f, 8f));
        var (second, _) = MakePanel(world, new Vector2(-300f, 60f), new Vector2(4f, 4f));
        var group = MakeGroup(world, first, second);

        void Tick()
        {
            panels.Update(Frame());
            hierarchy.Update(Frame());
        }

        Tick();
        var homeLocal = first.Get<TransformComponent>().Position;
        var homeChildWorld = firstChild.Get<TransformComponent>().WorldPosition;

        // Switch away and stay away for a while (the parked panel keeps living through every frame).
        group.Get<PanelGroupComponent>().Active = 1;
        for (var i = 0; i < 10; i++) Tick();

        // …and back.
        group.Get<PanelGroupComponent>().Active = 0;
        Tick();

        Assert.Equal(homeLocal, first.Get<TransformComponent>().Position);
        Assert.Equal(homeChildWorld, firstChild.Get<TransformComponent>().WorldPosition);
        Assert.False(first.Has<PanelParkedComponent>());
    }

    /// <summary>The same round trip for a LAYOUT-DRIVEN panel — the real-world shape, and the one
    /// that breaks naive parking: <c>AutoLayoutSystem</c> rewrites every root slot's position from
    /// scratch each frame, so the park has to be re-applied on top of a value the system does not
    /// own. Away and back, the solver's placement is unchanged and the offset never compounded.</summary>
    [Fact]
    public void LayoutDrivenPanel_SwitchesAwayAndBack_WithoutDriftingOrCompounding()
    {
        using var world = new World();
        var viewport = new ViewportManager(null, 800, 600);

        var (contentA, _) = MakePanel(world, Vector2.Zero, new Vector2(3f, 3f));
        var (contentB, _) = MakePanel(world, Vector2.Zero, new Vector2(3f, 3f));

        // Each panel is an auto-layout ROOT: the solver owns its position every frame.
        var panelA = new AutoLayoutBuilder(world, viewport)
            .CreateRoot(ScreenAnchor.Center, RenderTargetID.Main)
            .AddSlot(slot => slot.Attach(contentA).MeasureWith(_ => new Vector2(200f, 80f)))
            .Build();
        var panelB = new AutoLayoutBuilder(world, viewport)
            .CreateRoot(ScreenAnchor.Center, RenderTargetID.Main)
            .AddSlot(slot => slot.Attach(contentB).MeasureWith(_ => new Vector2(200f, 80f)))
            .Build();

        var group = MakeGroup(world, panelA, panelB);

        using var intrinsic = new IntrinsicSizingSystem(world);
        using var layout = new AutoLayoutSystem(world, viewport);
        using var panels = new PanelGroupSystem(world);
        using var hierarchy = new HierarchySystem(world);

        void Tick()
        {
            intrinsic.Update(Frame());
            layout.Update(Frame()); // rewrites both roots' positions from scratch
            panels.Update(Frame()); // …then parks the inactive one on top of that
            hierarchy.Update(Frame());
        }

        Tick();
        var solved = panelA.Get<TransformComponent>().Position;
        var solvedContentWorld = contentA.Get<TransformComponent>().WorldPosition;

        group.Get<PanelGroupComponent>().Active = 1;
        Tick();
        var parkedAfterOneFrame = panelA.Get<TransformComponent>().Position;
        for (var i = 0; i < 20; i++) Tick();

        // Parked for 21 frames of re-solved layout: still exactly one park offset away, never 21.
        Assert.Equal(parkedAfterOneFrame, panelA.Get<TransformComponent>().Position);
        Assert.Equal(solved + PanelGroupComponent.DefaultParkOffset, panelA.Get<TransformComponent>().Position);

        group.Get<PanelGroupComponent>().Active = 0;
        Tick();

        Assert.Equal(solved, panelA.Get<TransformComponent>().Position);
        Assert.Equal(solvedContentWorld, contentA.Get<TransformComponent>().WorldPosition);
    }

    // ── Park, don't hide ────────────────────────────────────────────────────────────────────────

    /// <summary>A parked panel is moved, not hidden: it keeps every component it had (nothing is
    /// removed, no <c>VisibleComponent</c> toggling), its subtree moved with it, and it sits outside
    /// any sane viewport. Alive and intact is the whole point — layout, text prep and mesh baking
    /// keep running over it while it waits off-screen.</summary>
    [Fact]
    public void ParkedPanel_KeepsItsComponents_AndItsSubtreeMovesOffScreen()
    {
        using var world = new World();
        using var panels = new PanelGroupSystem(world);
        using var hierarchy = new HierarchySystem(world);

        var (first, firstChild) = MakePanel(world, new Vector2(10f, 20f), new Vector2(5f, 5f));
        var (second, _) = MakePanel(world, Vector2.Zero, Vector2.Zero);
        first.Set<VisibleComponent>();
        firstChild.Set<VisibleComponent>();
        var group = MakeGroup(world, first, second);

        panels.Update(Frame());
        hierarchy.Update(Frame());

        group.Get<PanelGroupComponent>().Active = 1;
        panels.Update(Frame());
        hierarchy.Update(Frame());

        // Not hidden — still alive, still visible-tagged, still parented, still drawable.
        Assert.True(first.IsAlive);
        Assert.True(firstChild.IsAlive);
        Assert.True(first.Has<VisibleComponent>());
        Assert.True(firstChild.Has<VisibleComponent>());

        // Moved: the whole subtree is a park offset away, far outside an 800×600 (or any) viewport.
        Assert.Equal(new Vector2(10f, 20f) + PanelGroupComponent.DefaultParkOffset,
            first.Get<TransformComponent>().Position);
        Assert.Equal(new Vector2(15f, 25f) + PanelGroupComponent.DefaultParkOffset,
            firstChild.Get<TransformComponent>().WorldPosition);
        Assert.True(firstChild.Get<TransformComponent>().WorldPosition.X < -10_000f);
    }

    /// <summary>A group's park offset is applied exactly once, no matter how long the panel stays
    /// parked — the failure mode of a naive "position += offset each frame" park.</summary>
    [Fact]
    public void ParkingIsIdempotent_AcrossManyFrames()
    {
        using var world = new World();
        using var panels = new PanelGroupSystem(world);

        var (first, _) = MakePanel(world, new Vector2(64f, 64f), Vector2.Zero);
        var (second, _) = MakePanel(world, Vector2.Zero, Vector2.Zero);
        var group = MakeGroup(world, first, second);

        panels.Update(Frame());
        group.Get<PanelGroupComponent>().Active = 1;
        for (var i = 0; i < 100; i++) panels.Update(Frame());

        Assert.Equal(new Vector2(64f, 64f) + PanelGroupComponent.DefaultParkOffset,
            first.Get<TransformComponent>().Position);
    }

    /// <summary>A group may carry its own park offset (e.g. straight up, off the top of the
    /// screen); changing it re-parks from the SAME home rather than stacking a second offset.</summary>
    [Fact]
    public void CustomParkOffset_IsHonored_AndChangingItReParksFromTheSameHome()
    {
        using var world = new World();
        using var panels = new PanelGroupSystem(world);

        var (first, _) = MakePanel(world, new Vector2(30f, 40f), Vector2.Zero);
        var (second, _) = MakePanel(world, Vector2.Zero, Vector2.Zero);
        var group = world.CreateEntity();
        group.Set(new PanelGroupComponent
        {
            Members = [first, second],
            Active = 1,
            ParkOffset = new Vector2(0f, -5_000f),
        });

        panels.Update(Frame());
        Assert.Equal(new Vector2(30f, -4_960f), first.Get<TransformComponent>().Position);

        group.Get<PanelGroupComponent>().ParkOffset = new Vector2(-9_000f, 0f);
        panels.Update(Frame());
        Assert.Equal(new Vector2(-8_970f, 40f), first.Get<TransformComponent>().Position);

        group.Get<PanelGroupComponent>().Active = 0;
        panels.Update(Frame());
        Assert.Equal(new Vector2(30f, 40f), first.Get<TransformComponent>().Position);
    }

    // ── "None active" is first class ────────────────────────────────────────────────────────────

    /// <summary>A closed menu is a panel group with no active member — not a hack. Every member
    /// parks, and opening it back on any page restores that page exactly.</summary>
    [Fact]
    public void NoneActive_ParksEveryMember_AndReopeningRestoresThePage()
    {
        using var world = new World();
        using var panels = new PanelGroupSystem(world);

        var (page0, _) = MakePanel(world, new Vector2(-100f, 0f), Vector2.Zero);
        var (page1, _) = MakePanel(world, new Vector2(100f, 0f), Vector2.Zero);
        var group = MakeGroup(world, page0, page1);

        panels.Update(Frame()); // page 0 open

        group.Get<PanelGroupComponent>().Active = PanelGroupComponent.None; // menu closed
        panels.Update(Frame());

        Assert.True(page0.Has<PanelParkedComponent>());
        Assert.True(page1.Has<PanelParkedComponent>());

        group.Get<PanelGroupComponent>().Active = 1; // reopened on page 1
        panels.Update(Frame());

        Assert.Equal(new Vector2(100f, 0f), page1.Get<TransformComponent>().Position);
        Assert.Equal(new Vector2(-100f, 0f) + PanelGroupComponent.DefaultParkOffset,
            page0.Get<TransformComponent>().Position);
    }

    /// <summary>An out-of-range active index is "none active" too (a group whose member array
    /// shrank must not throw, and must not leave a stale panel on screen).</summary>
    [Fact]
    public void OutOfRangeActiveIndex_ParksEveryMember()
    {
        using var world = new World();
        using var panels = new PanelGroupSystem(world);

        var (page0, _) = MakePanel(world, Vector2.Zero, Vector2.Zero);
        var group = MakeGroup(world, page0);
        group.Get<PanelGroupComponent>().Active = 7;

        panels.Update(Frame());

        Assert.True(page0.Has<PanelParkedComponent>());
    }

    // ── A parked panel is inert ─────────────────────────────────────────────────────────────────

    /// <summary>Focus must not walk into a panel the player cannot see: every focusable under a
    /// parked member is disabled, and re-enabled on switch-back. Focusables outside the group are
    /// never touched.</summary>
    [Fact]
    public void FocusablesUnderAParkedPanel_AreDisabled_AndReEnabledOnSwitchBack()
    {
        using var world = new World();
        using var panels = new PanelGroupSystem(world);

        var (first, firstChild) = MakePanel(world, Vector2.Zero, new Vector2(5f, 5f));
        var (second, secondChild) = MakePanel(world, Vector2.Zero, new Vector2(5f, 5f));
        firstChild.Set(new FocusableComponent { Size = new Vector2(80f, 24f) });
        secondChild.Set(new FocusableComponent { Size = new Vector2(80f, 24f) });

        // A focusable that belongs to no panel group (screen chrome) — must stay untouched.
        var chrome = world.CreateEntity();
        chrome.Set(new TransformComponent(Vector2.Zero));
        chrome.Set(new FocusableComponent { Size = new Vector2(40f, 20f), Disabled = false });

        var group = MakeGroup(world, first, second);

        panels.Update(Frame());
        Assert.False(firstChild.Get<FocusableComponent>().Disabled);
        Assert.True(secondChild.Get<FocusableComponent>().Disabled);
        Assert.False(chrome.Get<FocusableComponent>().Disabled);

        group.Get<PanelGroupComponent>().Active = 1;
        panels.Update(Frame());
        Assert.True(firstChild.Get<FocusableComponent>().Disabled);
        Assert.False(secondChild.Get<FocusableComponent>().Disabled);
        Assert.False(chrome.Get<FocusableComponent>().Disabled);

        group.Get<PanelGroupComponent>().Active = 0;
        panels.Update(Frame());
        Assert.False(firstChild.Get<FocusableComponent>().Disabled);
        Assert.True(secondChild.Get<FocusableComponent>().Disabled);
    }

    /// <summary>Groups NEST — a wizard step containing a sub-tab bar, a settings page containing a
    /// paged sub-menu — and a parked ancestor wins over an active descendant. The inner group's
    /// active body is restored at its own local position, but it rides a parked outer member, so it
    /// is off-screen: its focusables must stay out of navigation. Gating at the NEAREST member
    /// ancestor only is the bug this pins — it re-enables the inner panel's controls while the outer
    /// step is parked, and Tab walks off-screen.</summary>
    [Fact]
    public void NestedGroups_AParkedOuterMemberGatesTheInnerGroupsActivePanel()
    {
        using var world = new World();
        using var panels = new PanelGroupSystem(world);
        using var hierarchy = new HierarchySystem(world);

        // Outer group: two wizard steps.
        var (stepA, _) = MakePanel(world, new Vector2(40f, 40f), Vector2.Zero);
        var (stepB, _) = MakePanel(world, new Vector2(40f, 40f), Vector2.Zero);
        var outer = MakeGroup(world, stepA, stepB);

        // Inner group: two sub-tab bodies parented UNDER stepA, each with a focusable control.
        var (innerFirst, innerFirstChild) = MakePanel(world, new Vector2(6f, 6f), new Vector2(2f, 2f));
        var (innerSecond, innerSecondChild) = MakePanel(world, new Vector2(6f, 6f), new Vector2(2f, 2f));
        innerFirst.SetParent(stepA);
        innerSecond.SetParent(stepA);
        innerFirstChild.Set(new FocusableComponent { Size = new Vector2(80f, 24f) });
        innerSecondChild.Set(new FocusableComponent { Size = new Vector2(80f, 24f) });
        var inner = MakeGroup(world, innerFirst, innerSecond); // inner body 0 active

        void Tick()
        {
            panels.Update(Frame());
            hierarchy.Update(Frame());
        }

        // Step A is up: the inner group's own gate applies — body 0 reachable, body 1 parked.
        Tick();
        Assert.False(innerFirstChild.Get<FocusableComponent>().Disabled);
        Assert.True(innerSecondChild.Get<FocusableComponent>().Disabled);

        // Move to step B. Step A parks; the inner group still considers body 0 active and restores
        // it — but it is a descendant of a parked member, hence off-screen and unreachable.
        outer.Get<PanelGroupComponent>().Active = 1;
        Tick();
        Assert.True(innerFirstChild.Get<FocusableComponent>().Disabled);
        Assert.True(innerSecondChild.Get<FocusableComponent>().Disabled);
        Assert.True(innerFirstChild.Get<TransformComponent>().WorldPosition.X < -10_000f);

        // Back on step A, the inner group's own state comes back untouched.
        outer.Get<PanelGroupComponent>().Active = 0;
        inner.Get<PanelGroupComponent>().Active = 1;
        Tick();
        Assert.True(innerFirstChild.Get<FocusableComponent>().Disabled);
        Assert.False(innerSecondChild.Get<FocusableComponent>().Disabled);
    }

    /// <summary>The panel gate composes with the coarser per-tab gate instead of fighting it. Two
    /// things are asserted that only hold because <c>TabSystem</c> really ran: chrome that is inside
    /// the tab but outside every panel group (the demo's pager row) is gated by <c>TabSystem</c>
    /// alone and never touched by <see cref="PanelGroupSystem"/>; and on the group's own tab
    /// <c>TabSystem</c> enables BOTH panel bodies (it only knows the tab), after which the panel gate
    /// refines that to the active panel only. Registration order is load-bearing: the panel gate must
    /// run last, or <c>TabSystem</c>'s coarse re-enable would put a parked panel's controls back into
    /// navigation — which is what the intermediate assertion here shows it doing.</summary>
    [Fact]
    public void ComposedWithTabSystem_TheTabGatesTheChrome_AndThePanelGateRefinesTheTabsBodies()
    {
        using var world = new World();
        using var tabs = new TabSystem(world);
        using var panels = new PanelGroupSystem(world);

        const int otherTab = 0;
        const int panelsTab = 1;
        var (first, firstChild) = MakePanel(world, Vector2.Zero, new Vector2(5f, 5f));
        var (second, secondChild) = MakePanel(world, Vector2.Zero, new Vector2(5f, 5f));
        foreach (var content in new[] { firstChild, secondChild })
        {
            content.Set(new FocusableComponent { Size = new Vector2(80f, 24f) });
            content.Set(new TabContentComponent { TabIndex = panelsTab });
        }

        // Pager chrome: on the same tab, but NOT a member of any group — it stays on screen while
        // every page is parked, so only TabSystem may gate it.
        var chrome = world.CreateEntity();
        chrome.Set(new TransformComponent(Vector2.Zero));
        chrome.Set(new FocusableComponent { Size = new Vector2(40f, 20f) });
        chrome.Set(new TabContentComponent { TabIndex = panelsTab });

        var group = MakeGroup(world, first, second);
        var bar = world.CreateEntity();
        bar.Set(new TabBarComponent { Tabs = [world.CreateEntity(), world.CreateEntity()], Active = otherTab });

        // Another tab is up: TabSystem disables everything tagged for the Panels tab — including the
        // chrome, which no panel group would ever touch. The screen closes the group to match.
        group.Get<PanelGroupComponent>().Active = PanelGroupComponent.None;
        tabs.Update(Frame());
        panels.Update(Frame());
        Assert.True(chrome.Get<FocusableComponent>().Disabled); // TabSystem's write, nobody else's
        Assert.True(firstChild.Get<FocusableComponent>().Disabled);
        Assert.True(secondChild.Get<FocusableComponent>().Disabled);

        // On the group's own tab, TabSystem enables the whole tab — chrome AND both panel bodies,
        // the parked one included: it has no idea panels exist.
        bar.Get<TabBarComponent>().Active = panelsTab;
        group.Get<PanelGroupComponent>().Active = 0;
        tabs.Update(Frame());
        Assert.False(chrome.Get<FocusableComponent>().Disabled);
        Assert.False(firstChild.Get<FocusableComponent>().Disabled);
        Assert.False(secondChild.Get<FocusableComponent>().Disabled); // parked, but tab-enabled

        // …then the panel gate, running last, refines it back down to the active panel and leaves
        // the out-of-group chrome exactly as TabSystem left it.
        panels.Update(Frame());
        Assert.False(chrome.Get<FocusableComponent>().Disabled);
        Assert.False(firstChild.Get<FocusableComponent>().Disabled);
        Assert.True(secondChild.Get<FocusableComponent>().Disabled);
    }

    // ── Degenerate inputs ───────────────────────────────────────────────────────────────────────

    /// <summary>A dead member, or one without a transform, is skipped rather than throwing — a
    /// group outlives an individual panel (screen teardown, a rebuilt page).</summary>
    [Fact]
    public void DeadOrTransformlessMembers_AreSkipped()
    {
        using var world = new World();
        using var panels = new PanelGroupSystem(world);

        var (alive, _) = MakePanel(world, new Vector2(7f, 7f), Vector2.Zero);
        var transformless = world.CreateEntity();
        var dead = world.CreateEntity();
        dead.Set(new TransformComponent(Vector2.Zero));

        var group = MakeGroup(world, alive, transformless, dead);
        group.Get<PanelGroupComponent>().Active = 1; // the transformless one "wins" — nothing to move

        panels.Update(Frame());
        dead.Dispose();
        panels.Update(Frame());

        Assert.Equal(new Vector2(7f, 7f) + PanelGroupComponent.DefaultParkOffset,
            alive.Get<TransformComponent>().Position);
    }
}
