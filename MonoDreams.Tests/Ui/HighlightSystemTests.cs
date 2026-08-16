using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.UI;
using Xunit;

namespace MonoDreams.Tests.Ui;

/// <summary>
/// Guards the ui premise "A highlight is an overlay the system owns: it follows the target's drawn
/// bounds, re-derives its depth every frame, and dies with its target" — the three invariants an
/// ad-hoc glow re-discovers as three bugs (it drifts off its target, it ends up under a sibling
/// after a z restack, it orphans when the target despawns).
///
/// One test per load-bearing claim:
/// - FOLLOW: the outline is rebuilt from the target's own drawn bounds, so it tracks a move, a
///   scale and a text label's measured width — on sprites, text and buttons alike.
/// - DEPTH: the overlay's LayerDepth is the target's CURRENT depth plus the offset, re-read every
///   frame (a z restack keeps the glow glued to its target).
/// - LIFETIME: disposing the target — or just removing the component — disposes the overlay, both
///   through the system's own sweep and through the ChildOfComponent orphan cascade.
/// - the outline is always OPAQUE (the mesh path composites premultiplied alpha), and the pulse
///   lives in the RGB channels.
///
/// Pure logic: a <see cref="World"/>, hand-built entities and a <see cref="GameState"/>. No
/// GraphicsDevice, no textures, no fonts — the derivation reads the PREPARED
/// <see cref="DrawComponent"/>, which is exactly what makes it measurable without a device.
/// </summary>
public class HighlightSystemTests
{
    /// <summary>Decimal places for float comparisons (the repo's Assert.Equal precision idiom).</summary>
    private const int Precision = 3;

    private static GameState NewState(float totalSeconds = 0f) =>
        new(new GameTime(TimeSpan.FromSeconds(totalSeconds), TimeSpan.FromSeconds(1 / 60f)));

    /// <summary>A just-prepped sprite: what <c>SpritePrepSystem</c> leaves behind for a source-rect
    /// sprite drawn 1:1 at <paramref name="position"/>.</summary>
    private static Entity NewSprite(World world, Vector2 position, Vector2 size, float depth = 0.5f)
    {
        var entity = world.CreateEntity();
        entity.Set(new TransformComponent(position));
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.Main,
            Position = position,
            SourceRectangle = new Rectangle(0, 0, (int)size.X, (int)size.Y),
            Size = size,
            Scale = Vector2.One,
            LayerDepth = depth,
        });
        entity.Set<VisibleComponent>();
        return entity;
    }

    /// <summary>The axis-aligned world bounds of the outline mesh the system built.</summary>
    private static (Vector2 min, Vector2 max) OverlayBounds(Entity target)
    {
        var overlay = target.Get<HighlightComponent>().Overlay;
        Assert.True(overlay.IsAlive);
        var draw = overlay.Get<DrawComponent>();
        Assert.True(draw.HasValidMesh);
        Assert.Equal(Matrix.Identity, draw.WorldMatrix);

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var vertex in draw.Vertices!)
        {
            var point = new Vector2(vertex.Position.X, vertex.Position.Y);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return (min, max);
    }

    private static void AssertHugs(Entity target, Vector2 topLeft, Vector2 size, float padding, float thickness)
    {
        var (min, max) = OverlayBounds(target);
        // A stroked outline extends half its thickness outside the padded box.
        var expand = padding + thickness / 2f;
        Assert.Equal(topLeft.X - expand, min.X, Precision);
        Assert.Equal(topLeft.Y - expand, min.Y, Precision);
        Assert.Equal(topLeft.X + size.X + expand, max.X, Precision);
        Assert.Equal(topLeft.Y + size.Y + expand, max.Y, Precision);
    }

    // ─── follow ──────────────────────────────────────────────────────────────────

    [Fact]
    public void OutlineHugsTheSpritesDrawnBounds()
    {
        using var world = new World();
        var system = new HighlightSystem(world);
        var sprite = NewSprite(world, new Vector2(100, 50), new Vector2(32, 24));
        sprite.Set(new HighlightComponent { Padding = 4f, Thickness = 2f });

        system.Update(NewState());

        AssertHugs(sprite, new Vector2(100, 50), new Vector2(32, 24), padding: 4f, thickness: 2f);
    }

    [Fact]
    public void OutlineFollowsTheTargetWhenItMovesAndScales()
    {
        using var world = new World();
        var system = new HighlightSystem(world);
        var sprite = NewSprite(world, new Vector2(100, 50), new Vector2(32, 24));
        sprite.Set(new HighlightComponent { Padding = 0f, Thickness = 2f });

        system.Update(NewState());
        AssertHugs(sprite, new Vector2(100, 50), new Vector2(32, 24), padding: 0f, thickness: 2f);

        // What the prep systems write next frame: the sprite moved and doubled its world scale.
        var draw = sprite.Get<DrawComponent>();
        draw.Position = new Vector2(140, 25);
        draw.Scale = new Vector2(2f, 2f);

        system.Update(NewState());

        // The drawn quad is (Size / source) · Scale — the outline is rebuilt from the same product.
        AssertHugs(sprite, new Vector2(140, 25), new Vector2(64, 48), padding: 0f, thickness: 2f);
    }

    [Fact]
    public void OutlineHugsATextLabelsMeasuredExtent()
    {
        using var world = new World();
        var system = new HighlightSystem(world);

        // What TextPrepSystem leaves behind: the MEASURED extent in Size, the composed
        // (world × text) scale in Scale. No BitmapFont needed to re-derive the box.
        var label = world.CreateEntity();
        label.Set(new TransformComponent(new Vector2(10, 20)));
        label.Set(new DrawComponent
        {
            Type = DrawElementType.Text,
            Target = RenderTargetID.UI,
            Text = "Click this",
            Position = new Vector2(10, 20),
            Size = new Vector2(80, 12),
            Scale = new Vector2(2f, 2f),
            LayerDepth = 0.3f,
        });
        label.Set(new HighlightComponent { Padding = 2f, Thickness = 1f });

        system.Update(NewState());

        AssertHugs(label, new Vector2(10, 20), new Vector2(160, 24), padding: 2f, thickness: 1f);
    }

    [Fact]
    public void OutlineHugsAButtonPreparedByButtonMeshPrepSystem()
    {
        using var world = new World();
        var buttonPrep = new ButtonMeshPrepSystem(world);
        var system = new HighlightSystem(world);

        var button = world.CreateEntity();
        button.Set(new TransformComponent(new Vector2(200, 100)));
        button.Set(new SimpleButtonComponent
        {
            Size = new Vector2(120, 40),
            LineThickness = 2f,
            Color = Color.White,
            Target = RenderTargetID.UI,
        });
        button.Set(new HighlightComponent { Padding = 0f, Thickness = 2f });

        var state = NewState();
        buttonPrep.Update(state);
        system.Update(state);

        // The button's own outline is stroked ±1 around its rect, so its drawn mesh spans
        // (199,99)..(321,141) — the highlight hugs what is actually drawn.
        AssertHugs(button, new Vector2(199, 99), new Vector2(122, 42), padding: 0f, thickness: 2f);
    }

    [Fact]
    public void ExplicitSizeHighlightsAnEntityThatDrawsNothing()
    {
        using var world = new World();
        var system = new HighlightSystem(world);

        var hotspot = world.CreateEntity();
        hotspot.Set(new TransformComponent(new Vector2(5, 7)));
        hotspot.Set(new HighlightComponent
        {
            Size = new Vector2(50, 30),
            Padding = 0f,
            Thickness = 2f,
            FallbackLayerDepth = 0.9f,
        });

        system.Update(NewState());

        AssertHugs(hotspot, new Vector2(5, 7), new Vector2(50, 30), padding: 0f, thickness: 2f);
        var overlay = hotspot.Get<HighlightComponent>().Overlay;
        Assert.Equal(0.9f, overlay.Get<DrawComponent>().LayerDepth, Precision);
        Assert.Equal(RenderTargetID.Main, overlay.Get<DrawComponent>().Target);
    }

    // ─── depth ───────────────────────────────────────────────────────────────────

    [Fact]
    public void DepthIsRederivedEveryFrameSoAZRestackKeepsTheGlowGlued()
    {
        using var world = new World();
        var system = new HighlightSystem(world);
        var sprite = NewSprite(world, Vector2.Zero, new Vector2(10, 10), depth: 0.2f);
        sprite.Set(new HighlightComponent { LayerDepthOffset = 0.001f });

        system.Update(NewState());
        var overlay = sprite.Get<HighlightComponent>().Overlay;
        Assert.Equal(0.201f, overlay.Get<DrawComponent>().LayerDepth, Precision);

        // The restack: something re-sorted the scene and the target now draws far in front.
        sprite.Get<DrawComponent>().LayerDepth = 0.8f;
        system.Update(NewState());

        Assert.Equal(0.801f, overlay.Get<DrawComponent>().LayerDepth, Precision);
    }

    [Fact]
    public void OverlayInheritsTheTargetsRenderTargetAndVisibility()
    {
        using var world = new World();
        var system = new HighlightSystem(world);
        var sprite = NewSprite(world, Vector2.Zero, new Vector2(10, 10));
        sprite.Set(new HighlightComponent());

        system.Update(NewState());
        var overlay = sprite.Get<HighlightComponent>().Overlay;
        Assert.Equal(RenderTargetID.Main, overlay.Get<DrawComponent>().Target);
        Assert.True(overlay.Has<VisibleComponent>());

        // A tab / culling hide removes the tag from the target; the glow must go with it.
        sprite.Remove<VisibleComponent>();
        sprite.Get<DrawComponent>().Target = RenderTargetID.HUD;
        system.Update(NewState());

        Assert.False(overlay.Has<VisibleComponent>());
        Assert.Equal(RenderTargetID.HUD, overlay.Get<DrawComponent>().Target);
    }

    // ─── lifetime ────────────────────────────────────────────────────────────────

    [Fact]
    public void OverlayIsDisposedWhenTheTargetIsDisposed()
    {
        using var world = new World();
        var system = new HighlightSystem(world);
        var sprite = NewSprite(world, Vector2.Zero, new Vector2(10, 10));
        sprite.Set(new HighlightComponent());

        system.Update(NewState());
        var overlay = sprite.Get<HighlightComponent>().Overlay;
        Assert.True(overlay.IsAlive);

        sprite.Dispose();
        system.Update(NewState());

        Assert.False(overlay.IsAlive);
    }

    [Fact]
    public void OverlayIsDisposedWhenTheHighlightComponentIsRemoved()
    {
        using var world = new World();
        var system = new HighlightSystem(world);
        var sprite = NewSprite(world, Vector2.Zero, new Vector2(10, 10));
        sprite.Set(new HighlightComponent());

        system.Update(NewState());
        var overlay = sprite.Get<HighlightComponent>().Overlay;

        sprite.Remove<HighlightComponent>();
        system.Update(NewState());

        Assert.False(overlay.IsAlive);
        Assert.True(sprite.IsAlive);
    }

    [Fact]
    public void OverlayIsParentedSoTheHierarchyCascadeAlsoReapsIt()
    {
        using var world = new World();
        var system = new HighlightSystem(world);
        var hierarchy = new HierarchySystem(world);
        var sprite = NewSprite(world, Vector2.Zero, new Vector2(10, 10));
        sprite.Set(new HighlightComponent());

        system.Update(NewState());
        var overlay = sprite.Get<HighlightComponent>().Overlay;
        Assert.Equal(sprite, overlay.Get<ChildOfComponent>().Parent);

        // No HighlightSystem update this time: the structural parent link alone must reap it.
        sprite.Dispose();
        hierarchy.Update(NewState());

        Assert.False(overlay.IsAlive);
    }

    [Fact]
    public void DisposingTheSystemRemovesEveryOverlay()
    {
        using var world = new World();
        var system = new HighlightSystem(world);
        var a = NewSprite(world, Vector2.Zero, new Vector2(10, 10));
        var b = NewSprite(world, new Vector2(50, 0), new Vector2(10, 10));
        a.Set(new HighlightComponent());
        b.Set(new HighlightComponent());

        system.Update(NewState());
        var overlayA = a.Get<HighlightComponent>().Overlay;
        var overlayB = b.Get<HighlightComponent>().Overlay;

        system.Dispose();

        Assert.False(overlayA.IsAlive);
        Assert.False(overlayB.IsAlive);
    }

    [Fact]
    public void ATargetThatDrawsNothingThisFrameGetsAnEmptyOutlineNotAStaleOne()
    {
        using var world = new World();
        var system = new HighlightSystem(world);

        var label = world.CreateEntity();
        label.Set(new TransformComponent(Vector2.Zero));
        label.Set(new DrawComponent
        {
            Type = DrawElementType.Text,
            Text = "visible",
            Size = new Vector2(40, 10),
            Scale = Vector2.One,
        });
        label.Set(new HighlightComponent());

        system.Update(NewState());
        var overlay = label.Get<HighlightComponent>().Overlay;
        Assert.True(overlay.Get<DrawComponent>().HasValidMesh);

        // TextPrepSystem clears Text when the label renders nothing this frame.
        label.Get<DrawComponent>().Text = null;
        system.Update(NewState());

        Assert.False(overlay.Get<DrawComponent>().HasValidMesh);
    }

    // ─── pulse ───────────────────────────────────────────────────────────────────

    [Fact]
    public void PulseOscillatesTheRgbChannelsAndNeverTheAlpha()
    {
        var highlight = new HighlightComponent
        {
            Color = new Color(200, 100, 50, 255),
            PulseSpeed = 1f,
            PulseMinIntensity = 0.5f,
        };

        var peak = HighlightSystem.PulseColor(in highlight, 0.25f);   // sin = +1
        var trough = HighlightSystem.PulseColor(in highlight, 0.75f); // sin = -1

        Assert.Equal(new Color(200, 100, 50, 255), peak);
        Assert.Equal(new Color(100, 50, 25, 255), trough);
        // The mesh path composites premultiplied alpha: a faded outline must be darker, never
        // more transparent (see the rendering premise on premultiplied alpha).
        Assert.Equal(255, peak.A);
        Assert.Equal(255, trough.A);
    }

    [Fact]
    public void ZeroPulseSpeedDrawsASteadyOutlineAndUnsetFieldsFallBackToTheDefaults()
    {
        // entity.Set<HighlightComponent>() stores default(T), which bypasses the struct's field
        // initializers — the zero-means-default rule is what keeps that case visible.
        var unset = default(HighlightComponent);

        var color = HighlightSystem.PulseColor(in unset, 12.5f);

        Assert.Equal(HighlightSystem.DefaultColor, color);
    }

    [Fact]
    public void AnUnconfiguredHighlightStillDrawsAVisibleOutline()
    {
        using var world = new World();
        var system = new HighlightSystem(world);
        var sprite = NewSprite(world, Vector2.Zero, new Vector2(10, 10), depth: 0.5f);
        sprite.Set(default(HighlightComponent)); // exactly what entity.Set<HighlightComponent>() stores

        system.Update(NewState());

        var overlay = sprite.Get<HighlightComponent>().Overlay;
        var draw = overlay.Get<DrawComponent>();
        Assert.True(draw.HasValidMesh);
        Assert.Equal(0.5f + HighlightSystem.DefaultLayerDepthOffset, draw.LayerDepth, Precision);
        AssertHugs(sprite, Vector2.Zero, new Vector2(10, 10),
            padding: 0f, thickness: HighlightSystem.DefaultThickness);
    }
}
