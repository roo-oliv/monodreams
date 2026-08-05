using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Level;
using MonoDreams.Extension;
using MonoDreams.State;
using MonoDreams.System.Level;
using Xunit;

namespace MonoDreams.Tests.Rendering;

/// <summary>
/// Guards the level-loading premise "Scene layers are entities; member draw order derives from
/// (layer order, within-layer key)" and the rendering premise "Layer-depth ownership pipeline"
/// (the optional <c>SceneLayerSystem</c> stage between <c>SpritePrepSystem</c> and
/// <c>YSortSystem</c>).
///
/// The load-bearing claims, one test each:
/// - REORDERING layers reorders every member's final depth and writes NOTHING to member data
///   (a reorder is a one-line scene diff, not a churn of every member row).
/// - within-layer order follows the member's SOURCE <c>SpriteInfoComponent.LayerDepth</c>, read as
///   a 0..1 key into the layer's slice of <c>BandMin..BandMax</c>.
/// - a HIDDEN layer draws its members fully transparent (post-prep colour zero — never a
///   <c>VisibleComponent</c> fight with <c>CullingSystem</c>).
/// - a SCREEN-SPACE layer is organizational only: excluded from the band slicing, its members'
///   depths untouched, and it consumes no slice slot from the world layers.
/// - entities on NO layer pass through bit-identical (legacy scenes / code-built HUD overlays).
/// - equal <c>Order</c> ties break by name, ORDINAL, so the order is deterministic.
///
/// Pure logic: a <see cref="World"/>, hand-built entities, and a default-constructed
/// <see cref="GameState"/> (the system reads no state fields). No rendering, no
/// <c>GraphicsDevice</c>.
/// </summary>
public class SceneLayerSystemTests
{
    private static GameState NewState() => new(new GameTime());

    /// <summary>A layer entity: nothing but <c>SceneLayerComponent</c> + its name-carrying
    /// <c>EntityInfoComponent</c> (the layer's name IS its EntityInfo name).</summary>
    private static Entity NewLayer(
        World world, int order, string name,
        bool visible = true, bool locked = false, bool screenSpace = false)
    {
        var layer = world.CreateEntity();
        layer.Set(new EntityInfoComponent("Layer", name));
        layer.Set(new SceneLayerComponent
        {
            Order = order,
            Visible = visible,
            Locked = locked,
            ScreenSpace = screenSpace,
        });
        return layer;
    }

    /// <summary>A just-prepped layer member: what <c>SpritePrepSystem</c> leaves behind
    /// (<c>DrawComponent.LayerDepth</c> seeded from the SOURCE sprite depth, colour copied from the
    /// sprite) plus the <c>ChildOf</c> link that makes it a member of <paramref name="parent"/>.</summary>
    private static Entity NewMember(World world, Entity parent, float sourceDepth, Color? color = null)
    {
        var tint = color ?? Color.White;
        var e = world.CreateEntity();
        e.Set(new SpriteInfoComponent
        {
            LayerDepth = sourceDepth,
            Color = tint,
            Target = RenderTargetID.Main,
        });
        e.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.Main,
            LayerDepth = sourceDepth,
            Color = tint,
        });
        e.SetParent(parent);
        return e;
    }

    private static float FinalDepth(Entity member) => member.Get<DrawComponent>().LayerDepth;
    private static float SourceDepth(Entity member) => member.Get<SpriteInfoComponent>().LayerDepth;

    /// <summary>Bit pattern of a float — "byte-identical", not "close enough".</summary>
    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

    // ── 1. Reordering layers reorders members, with ZERO writes to member components ───────────────

    [Fact]
    public void ReorderingLayers_ReordersMembers_WithZeroWritesToMemberData()
    {
        using var world = new World();
        var layerA = NewLayer(world, order: 0, name: "A");
        var layerB = NewLayer(world, order: 1, name: "B");

        // Same within-layer key on both, so ONLY the layer order can decide who draws in front.
        var memberA = NewMember(world, layerA, sourceDepth: 0.5f);
        var memberB = NewMember(world, layerB, sourceDepth: 0.5f);

        // The authored member data, captured before the system ever runs.
        var authoredA = Bits(SourceDepth(memberA));
        var authoredB = Bits(SourceDepth(memberB));
        var authoredColorA = memberA.Get<SpriteInfoComponent>().Color;
        var authoredColorB = memberB.Get<SpriteInfoComponent>().Color;

        using var system = new SceneLayerSystem(world);
        system.Update(NewState());

        // A (order 0) is behind B (order 1).
        Assert.True(FinalDepth(memberA) < FinalDepth(memberB),
            $"A ({FinalDepth(memberA)}) should draw behind B ({FinalDepth(memberB)})");

        // Reorder: swap the two layers' Order. This is the entire edit — no member is touched.
        layerA.Get<SceneLayerComponent>().Order = 1;
        layerB.Get<SceneLayerComponent>().Order = 0;

        system.Update(NewState());

        // The relation flipped purely from the layer edit.
        Assert.True(FinalDepth(memberB) < FinalDepth(memberA),
            $"after the reorder B ({FinalDepth(memberB)}) should draw behind A ({FinalDepth(memberA)})");

        // ...and the members' SOURCE data is byte-identical to what was authored: a reorder is a
        // one-line scene diff, never a rewrite of every member row.
        Assert.Equal(authoredA, Bits(SourceDepth(memberA)));
        Assert.Equal(authoredB, Bits(SourceDepth(memberB)));
        Assert.Equal(authoredColorA, memberA.Get<SpriteInfoComponent>().Color);
        Assert.Equal(authoredColorB, memberB.Get<SpriteInfoComponent>().Color);
    }

    // ── 2. Within-layer order follows the member's SOURCE LayerDepth (the within-layer key) ───────

    [Fact]
    public void WithinLayerOrder_FollowsMemberSourceLayerDepth_InsideTheLayerSlice()
    {
        using var world = new World();
        var layer = NewLayer(world, order: 0, name: "Only");
        var back = NewMember(world, layer, sourceDepth: 0.2f);
        var front = NewMember(world, layer, sourceDepth: 0.8f);
        var clamped = NewMember(world, layer, sourceDepth: 1.4f); // out of range → clamped to the key 1

        using var system = new SceneLayerSystem(world);
        system.Update(NewState());

        var (min, width) = SceneLayerSystem.Slice(0, 1);
        Assert.Equal(SceneLayerSystem.BandMin, min);
        Assert.Equal(SceneLayerSystem.BandMax - SceneLayerSystem.BandMin, width, 6);

        // Order preserved...
        Assert.True(FinalDepth(back) < FinalDepth(front));

        // ...both inside the layer's slice...
        Assert.InRange(FinalDepth(back), min, min + width);
        Assert.InRange(FinalDepth(front), min, min + width);

        // ...at exactly sliceMin + key * sliceWidth.
        Assert.Equal(min + 0.2f * width, FinalDepth(back), 6);
        Assert.Equal(min + 0.8f * width, FinalDepth(front), 6);

        // An out-of-range key clamps to the slice's front edge rather than escaping the band.
        Assert.Equal(min + width, FinalDepth(clamped), 6);
        Assert.Equal(SceneLayerSystem.BandMax, FinalDepth(clamped), 6);
    }

    // ── 3. A hidden layer draws its members fully transparent (post-prep colour zero) ─────────────

    [Fact]
    public void HiddenLayer_DrawsMembersTransparent_VisibleLayerKeepsItsPreppedColor()
    {
        var prepped = new Color(12, 34, 56, 200);

        using var world = new World();
        var hidden = NewLayer(world, order: 0, name: "Hidden", visible: false);
        var shown = NewLayer(world, order: 1, name: "Shown");
        var hiddenMember = NewMember(world, hidden, sourceDepth: 0.5f, color: prepped);
        var shownMember = NewMember(world, shown, sourceDepth: 0.5f, color: prepped);

        using var system = new SceneLayerSystem(world);
        system.Update(NewState());

        // Hidden ⇒ fully transparent. The depth is still remapped (only the colour hides it).
        Assert.Equal(Color.Transparent, hiddenMember.Get<DrawComponent>().Color);
        var (min, width) = SceneLayerSystem.Slice(0, 2);
        Assert.Equal(min + 0.5f * width, FinalDepth(hiddenMember), 6);

        // The visible layer's member keeps exactly the colour prep gave it.
        Assert.Equal(prepped, shownMember.Get<DrawComponent>().Color);

        // Hiding never touches the member's SOURCE data (unhiding restores from prep next frame).
        Assert.Equal(prepped, hiddenMember.Get<SpriteInfoComponent>().Color);
    }

    // ── 4. A screen-space layer is excluded from the band slicing and consumes no slot ────────────

    [Fact]
    public void ScreenSpaceLayer_IsExcluded_MembersUntouched_AndConsumesNoSliceSlot()
    {
        using var world = new World();
        var worldLayer = NewLayer(world, order: 0, name: "World");
        var hudLayer = NewLayer(world, order: 1, name: "Hud", screenSpace: true);

        var worldMember = NewMember(world, worldLayer, sourceDepth: 0.5f);
        var hudMember = NewMember(world, hudLayer, sourceDepth: 0.5f);

        // The HUD member's authored final depth, above the band — a code-built overlay's own value.
        hudMember.Get<DrawComponent>().LayerDepth = 0.95f;
        var authoredHudDepth = Bits(FinalDepth(hudMember));
        var authoredHudColor = hudMember.Get<DrawComponent>().Color;

        using var system = new SceneLayerSystem(world);
        system.Update(NewState());

        // The screen-space member is untouched — depth AND colour, bit-identical.
        Assert.Equal(authoredHudDepth, Bits(FinalDepth(hudMember)));
        Assert.Equal(authoredHudColor, hudMember.Get<DrawComponent>().Color);

        // The world layer got the WHOLE band (Slice(0, 1)) — the screen-space layer took no slot.
        var (soleMin, soleWidth) = SceneLayerSystem.Slice(0, 1);
        Assert.Equal(soleMin + 0.5f * soleWidth, FinalDepth(worldMember), 6);

        // Distinguishing assertion: had the screen-space layer been counted, the slice would be
        // half as wide and the member's depth would land elsewhere.
        var (twoMin, twoWidth) = SceneLayerSystem.Slice(0, 2);
        Assert.NotEqual(twoMin + 0.5f * twoWidth, FinalDepth(worldMember), 6);
    }

    // ── 5. Entities on no layer pass through bit-identical ────────────────────────────────────────

    [Fact]
    public void LayerlessEntity_PassesThroughUntouched_EvenBesideARealLayer()
    {
        using var world = new World();

        // A plain (non-layer) parent — the ancestor walk must find no SceneLayerComponent.
        var plainParent = world.CreateEntity();
        plainParent.Set(new EntityInfoComponent("Prop", "Crate"));

        var orphanColor = new Color(9, 8, 7, 6);
        var orphan = NewMember(world, plainParent, sourceDepth: 0.37f, color: orphanColor);
        orphan.Get<DrawComponent>().LayerDepth = 0.37f;
        var authoredOrphanDepth = Bits(FinalDepth(orphan));

        // A real layer + member in the SAME world, so the system does not early-return.
        var layer = NewLayer(world, order: 0, name: "Ground");
        var member = NewMember(world, layer, sourceDepth: 0.37f);

        using var system = new SceneLayerSystem(world);
        system.Update(NewState());

        // The layerless entity is bit-identical...
        Assert.Equal(authoredOrphanDepth, Bits(FinalDepth(orphan)));
        Assert.Equal(orphanColor, orphan.Get<DrawComponent>().Color);
        // ...while the real member (same authored depth) WAS remapped into its slice.
        var (min, width) = SceneLayerSystem.Slice(0, 1);
        Assert.Equal(min + 0.37f * width, FinalDepth(member), 6);
        Assert.NotEqual(authoredOrphanDepth, Bits(FinalDepth(member)));
    }

    [Fact]
    public void ZeroLayerWorld_RendersByteIdentical()
    {
        using var world = new World();

        // A parented, prepped sprite tree with no SceneLayerComponent anywhere: a legacy scene.
        var root = world.CreateEntity();
        root.Set(new EntityInfoComponent("Prop", "Root"));
        var a = NewMember(world, root, sourceDepth: 0.11f, color: new Color(1, 2, 3, 4));
        var b = NewMember(world, a, sourceDepth: 0.62f, color: new Color(5, 6, 7, 8));

        var beforeA = (Depth: Bits(FinalDepth(a)), Color: a.Get<DrawComponent>().Color);
        var beforeB = (Depth: Bits(FinalDepth(b)), Color: b.Get<DrawComponent>().Color);

        using var system = new SceneLayerSystem(world);
        system.Update(NewState());

        Assert.Equal(beforeA.Depth, Bits(FinalDepth(a)));
        Assert.Equal(beforeA.Color, a.Get<DrawComponent>().Color);
        Assert.Equal(beforeB.Depth, Bits(FinalDepth(b)));
        Assert.Equal(beforeB.Color, b.Get<DrawComponent>().Color);
    }

    // ── Membership walks the whole ChildOf ancestor chain (a prefab instance's sprites) ───────────

    [Fact]
    public void Membership_WalksTheChildOfAncestorChain_NearestLayerAncestorWins()
    {
        using var world = new World();
        var outer = NewLayer(world, order: 0, name: "Outer");
        var inner = NewLayer(world, order: 1, name: "Inner");

        // A prefab instance root parented to the OUTER layer; its sprite is a grandchild.
        var instanceRoot = world.CreateEntity();
        instanceRoot.Set(new EntityInfoComponent("Prefab", "Instance"));
        instanceRoot.SetParent(outer);
        var grandchild = NewMember(world, instanceRoot, sourceDepth: 0.5f);

        // A sprite whose nearest layer ancestor is INNER, even though OUTER is further up.
        var nested = world.CreateEntity();
        nested.Set(new EntityInfoComponent("Prefab", "Nested"));
        nested.SetParent(inner);
        inner.SetParent(outer); // inner itself lives under outer
        var nestedSprite = NewMember(world, nested, sourceDepth: 0.5f);

        Assert.Equal(outer, SceneLayerSystem.OwningLayer(grandchild));
        Assert.Equal(inner, SceneLayerSystem.OwningLayer(nestedSprite));

        using var system = new SceneLayerSystem(world);
        system.Update(NewState());

        var (outerMin, outerWidth) = SceneLayerSystem.Slice(0, 2);
        var (innerMin, innerWidth) = SceneLayerSystem.Slice(1, 2);
        Assert.Equal(outerMin + 0.5f * outerWidth, FinalDepth(grandchild), 6);
        Assert.Equal(innerMin + 0.5f * innerWidth, FinalDepth(nestedSprite), 6);
    }

    // ── 6. Equal Order ties break by name, ORDINAL, so the order is deterministic ─────────────────

    [Fact]
    public void EqualOrder_TiesBreakByName_Ordinal()
    {
        using var world = new World();
        var foreground = NewLayer(world, order: 2, name: "Foreground");
        var background = NewLayer(world, order: 2, name: "Background");

        var ordered = SceneLayerSystem.OrderedLayers(world);

        Assert.Equal(2, ordered.Count);
        Assert.Equal("Background", SceneLayerSystem.LayerName(ordered[0]));
        Assert.Equal("Foreground", SceneLayerSystem.LayerName(ordered[1]));
        Assert.Equal(background, ordered[0]);
        Assert.Equal(foreground, ordered[1]);

        // ORDINAL, not culture-aware: 'Z' (0x5A) sorts before 'a' (0x61). A culture-aware compare
        // would put "apple" first, and the order would then vary by machine locale.
        using var ordinalWorld = new World();
        NewLayer(ordinalWorld, order: 0, name: "apple");
        NewLayer(ordinalWorld, order: 0, name: "Zebra");

        var ordinal = SceneLayerSystem.OrderedLayers(ordinalWorld);
        Assert.Equal("Zebra", SceneLayerSystem.LayerName(ordinal[0]));
        Assert.Equal("apple", SceneLayerSystem.LayerName(ordinal[1]));

        // Order still dominates the name.
        using var orderWorld = new World();
        NewLayer(orderWorld, order: 5, name: "Aaa");
        NewLayer(orderWorld, order: 1, name: "Zzz");
        var byOrder = SceneLayerSystem.OrderedLayers(orderWorld);
        Assert.Equal("Zzz", SceneLayerSystem.LayerName(byOrder[0]));
        Assert.Equal("Aaa", SceneLayerSystem.LayerName(byOrder[1]));
    }

    // ── The band the layers slice stays inside the documented 0.05..0.9 range ─────────────────────

    [Fact]
    public void Slices_TileTheBand_WithoutGapsOrOverflow()
    {
        Assert.Equal(0.05f, SceneLayerSystem.BandMin);
        Assert.Equal(0.9f, SceneLayerSystem.BandMax);

        for (var count = 1; count <= 5; count++)
        {
            var (firstMin, width) = SceneLayerSystem.Slice(0, count);
            Assert.Equal(SceneLayerSystem.BandMin, firstMin, 6);

            for (var i = 1; i < count; i++)
            {
                var (min, w) = SceneLayerSystem.Slice(i, count);
                Assert.Equal(width, w, 6);                                  // even slices
                var (prevMin, prevWidth) = SceneLayerSystem.Slice(i - 1, count);
                Assert.Equal(prevMin + prevWidth, min, 6);                  // contiguous, no gaps
            }

            var (lastMin, lastWidth) = SceneLayerSystem.Slice(count - 1, count);
            Assert.Equal(SceneLayerSystem.BandMax, lastMin + lastWidth, 6); // never overflows the band
        }
    }
}
