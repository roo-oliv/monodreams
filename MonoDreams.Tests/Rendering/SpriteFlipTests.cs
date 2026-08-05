using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.State;
using MonoDreams.System.Draw;
using Xunit;

namespace MonoDreams.Tests.Rendering;

/// <summary>
/// Guards the rendering premise "Sprite facing/orientation is a flip flag, not mirrored art".
/// A sprite's facing is <c>SpriteInfoComponent.FlipHorizontally</c> / <c>FlipVertically</c>, copied by
/// <c>SpritePrepSystem</c> into the <c>DrawComponent</c> and OR-composed into <c>SpriteEffects</c> by
/// <see cref="MasterRenderSystem.ComputeSpriteEffects"/> — so no sheet bakes a mirrored row for the
/// other facing. Two things the tests pin: the defaults compose to <see cref="SpriteEffects.None"/>
/// (unflagged sprites render byte-identical to before the flags existed), and the scene serializer
/// writes the keys ONLY when true (every committed pre-flip <c>.mdscene</c> stays byte-identical, which
/// the canonical-serialization fixed-point tests depend on).
///
/// Pure logic — a <see cref="World"/> and hand-built entities, no live <c>GraphicsDevice</c>.
/// </summary>
public class SpriteFlipTests
{
    // ---- (a) ComputeSpriteEffects: the four flag combinations ----

    [Fact]
    public void ComputeSpriteEffects_NoFlags_IsNone_TheByteIdenticalDefault()
    {
        // The load-bearing case: an unflagged sprite must submit exactly what the renderer submitted
        // before the flags existed (SpriteEffects.None), so existing content is unchanged.
        var dc = SpriteDraw();
        Assert.False(dc.FlipHorizontally);
        Assert.False(dc.FlipVertically);
        Assert.Equal(SpriteEffects.None, MasterRenderSystem.ComputeSpriteEffects(dc));
    }

    [Fact]
    public void ComputeSpriteEffects_HorizontalOnly_IsFlipHorizontally()
    {
        var dc = SpriteDraw();
        dc.FlipHorizontally = true;
        Assert.Equal(SpriteEffects.FlipHorizontally, MasterRenderSystem.ComputeSpriteEffects(dc));
    }

    [Fact]
    public void ComputeSpriteEffects_VerticalOnly_IsFlipVertically()
    {
        var dc = SpriteDraw();
        dc.FlipVertically = true;
        Assert.Equal(SpriteEffects.FlipVertically, MasterRenderSystem.ComputeSpriteEffects(dc));
    }

    [Fact]
    public void ComputeSpriteEffects_BothFlags_OrsTheTwoAxes()
    {
        var dc = SpriteDraw();
        dc.FlipHorizontally = true;
        dc.FlipVertically = true;
        Assert.Equal(SpriteEffects.FlipHorizontally | SpriteEffects.FlipVertically,
            MasterRenderSystem.ComputeSpriteEffects(dc));
    }

    // ---- (b) Both flags default to false on a fresh component ----

    [Fact]
    public void FreshComponents_DefaultBothFlagsFalse()
    {
        var sprite = new SpriteInfoComponent();
        Assert.False(sprite.FlipHorizontally);
        Assert.False(sprite.FlipVertically);

        var draw = new DrawComponent();
        Assert.False(draw.FlipHorizontally);
        Assert.False(draw.FlipVertically);
    }

    // ---- (c) SpritePrepSystem copies both flags, and perturbs nothing else ----

    [Fact]
    public void SpritePrepSystem_CopiesBothFlags_ToTheDrawComponent()
    {
        using var world = new World();
        var entity = NewSpriteEntity(world, flipH: true, flipV: true);

        RunPrep(world);

        var draw = entity.Get<DrawComponent>();
        Assert.True(draw.FlipHorizontally);
        Assert.True(draw.FlipVertically);
    }

    [Fact]
    public void SpritePrepSystem_UnflaggedSprite_LeavesBothFlagsFalse()
    {
        using var world = new World();
        var entity = NewSpriteEntity(world, flipH: false, flipV: false);
        // Start from a DrawComponent that was flipped on a previous frame: the copy must CLEAR it,
        // not leave a stale flip on a sprite whose facing changed back.
        entity.Get<DrawComponent>().FlipHorizontally = true;
        entity.Get<DrawComponent>().FlipVertically = true;

        RunPrep(world);

        var draw = entity.Get<DrawComponent>();
        Assert.False(draw.FlipHorizontally);
        Assert.False(draw.FlipVertically);
    }

    [Fact]
    public void SpritePrepSystem_Flips_DoNotPerturbTheTransformFields()
    {
        // A flip mirrors the pixels INSIDE the destination rect — it must not move the drawn quad, so
        // every transform-ish field prep writes must be identical with the flags on and off. (This is
        // what keeps the drawn quad equal to GizmoTransform.SpriteWorldQuad's hit-test quad.)
        using var unflippedWorld = new World();
        var unflipped = NewSpriteEntity(unflippedWorld, flipH: false, flipV: false);
        RunPrep(unflippedWorld);
        var expected = unflipped.Get<DrawComponent>();

        using var flippedWorld = new World();
        var flipped = NewSpriteEntity(flippedWorld, flipH: true, flipV: true);
        RunPrep(flippedWorld);
        var actual = flipped.Get<DrawComponent>();

        Assert.Equal(expected.Position, actual.Position);
        Assert.Equal(expected.Rotation, actual.Rotation);
        Assert.Equal(expected.Origin, actual.Origin);
        Assert.Equal(expected.Scale, actual.Scale);
        Assert.Equal(expected.Size, actual.Size);
        Assert.Equal(expected.SourceRectangle, actual.SourceRectangle);
        Assert.Equal(expected.Color, actual.Color);
        Assert.Equal(expected.LayerDepth, actual.LayerDepth);
        // ...and the draw scale (hence the drawn quad's extent) is the same too.
        Assert.Equal(MasterRenderSystem.ComputeSpriteScale(expected), MasterRenderSystem.ComputeSpriteScale(actual));
    }

    // ---- (d) Serializer: flags round-trip, and are OMITTED when false (byte-stability) ----

    [Fact]
    public void Serializer_FlippedSprite_RoundTripsBothFlagsTrue()
    {
        using var world = new World();
        var registry = NewEngineRegistry();
        var serializer = new SceneSerializer(registry);

        var e = world.CreateEntity();
        e.Set(new SpriteInfoComponent
        {
            AssetKey = "Atlas/Tiles",
            Source = new Rectangle(0, 0, 16, 16),
            Size = new Vector2(16, 16),
            Target = RenderTargetID.Main,
            FlipHorizontally = true,
            FlipVertically = true,
        });

        // The written body carries both keys as true...
        var json = registry.SerializeEntity(e).Components[EngineComponentSerializers.SpriteInfoKey];
        Assert.True(json.GetProperty("flipHorizontally").GetBoolean());
        Assert.True(json.GetProperty("flipVertically").GetBoolean());

        // ...and a full round-trip onto a fresh world reproduces them.
        var scene = serializer.Serialize(new List<Entity> { e });
        using var freshWorld = new World();
        var loaded = serializer.Deserialize(freshWorld, scene);

        var s = loaded[0].Get<SpriteInfoComponent>();
        Assert.True(s.FlipHorizontally);
        Assert.True(s.FlipVertically);
    }

    [Fact]
    public void Serializer_DefaultSprite_OmitsBothFlipKeys_SoPreFlipScenesStayByteIdentical()
    {
        using var world = new World();
        var registry = NewEngineRegistry();

        var e = world.CreateEntity();
        e.Set(new SpriteInfoComponent { AssetKey = "Atlas/Tiles", Target = RenderTargetID.Main });

        var json = registry.SerializeEntity(e).Components[EngineComponentSerializers.SpriteInfoKey];

        Assert.False(json.TryGetProperty("flipHorizontally", out _));
        Assert.False(json.TryGetProperty("flipVertically", out _));
        var raw = json.GetRawText();
        Assert.DoesNotContain("flipHorizontally", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("flipVertically", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serializer_BodyWithoutTheFlipKeys_ReadsAsUnflipped()
    {
        // A pre-flip `.mdscene` (no flip keys at all) must load as an unflipped sprite.
        using var world = new World();
        var serializer = new SceneSerializer(NewEngineRegistry());

        var scene = new SceneData();
        var entityData = new SceneEntityData();
        entityData.Components[EngineComponentSerializers.SpriteInfoKey] = JsonSerializer.SerializeToElement(
            new { assetKey = "Atlas/Tiles", source = new[] { 0, 0, 16, 16 }, size = new[] { 16f, 16f } });
        scene.Entities.Add(entityData);

        var loaded = serializer.Deserialize(world, scene);

        var s = loaded[0].Get<SpriteInfoComponent>();
        Assert.False(s.FlipHorizontally);
        Assert.False(s.FlipVertically);
    }

    // ---- helpers ----

    private static ComponentSerializerRegistry NewEngineRegistry()
    {
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        return registry;
    }

    private static DrawComponent SpriteDraw() => new()
    {
        Type = DrawElementType.Sprite,
        SourceRectangle = new Rectangle(0, 0, 16, 16),
        Size = new Vector2(16f, 16f),
    };

    /// <summary>
    /// The renderable stack <c>SpritePrepSystem</c> queries: Draw + SpriteInfo + Transform + Visible,
    /// with a rotated, world-scaled, off-centre-origin sprite so the "flips perturb nothing" assertion
    /// covers every field the regular-sprite branch writes.
    /// </summary>
    private static Entity NewSpriteEntity(World world, bool flipH, bool flipV)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(new Vector2(120f, 80f), rotation: 0.4f, scale: new Vector2(2f, 3f)));
        e.Set(new SpriteInfoComponent
        {
            SpriteSheet = StubTexture(),
            Source = new Rectangle(0, 0, 48, 32),
            Size = new Vector2(48f, 32f),
            Color = Color.White,
            Origin = new Vector2(24f, 32f), // feet origin (off-centre in Y)
            Offset = new Vector2(3f, -4f),
            Target = RenderTargetID.Main,
            LayerDepth = 0.42f,
            FlipHorizontally = flipH,
            FlipVertically = flipV,
        });
        e.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main });
        e.Set(new VisibleComponent());
        return e;
    }

    private static void RunPrep(World world)
    {
        // The GraphicsDevice is only used by the nine-patch branch (NinePatchData is null here), so a
        // null device exercises the regular-sprite branch faithfully.
        using var prep = new SpritePrepSystem(world, graphicsDevice: null!, pixelPerfectRendering: false);
        prep.Update(new GameState(new GameTime()));
    }

    /// <summary>
    /// A stand-in <see cref="Texture2D"/>: the regular-sprite branch is gated on
    /// <c>SpriteInfo.SpriteSheet != null</c> but only ever COPIES the reference into the
    /// <c>DrawComponent</c> — it never touches a texture member — and a real texture needs a
    /// <c>GraphicsDevice</c> no unit test has. So fabricate a ctor-less instance (the same
    /// <see cref="RuntimeHelpers.GetUninitializedObject"/> trick the Inspector's component defaults use)
    /// and suppress its finalizer, which would otherwise dereference the null graphics device.
    /// </summary>
    private static Texture2D StubTexture()
    {
        var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
        GC.SuppressFinalize(texture);
        return texture;
    }
}
