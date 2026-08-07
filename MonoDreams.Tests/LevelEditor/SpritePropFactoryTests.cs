#nullable enable
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Assets;
using MonoDreams.LevelEditor.Component;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the generic <see cref="SpritePropFactory"/> (island-authoring plan §3.2): it builds the
/// standard renderable stack from a catalog entry + a screen-supplied band, writes the SOURCE sort
/// fields (never a derived depth), and applies the <b>feet-origin convention</b> on a Y-sorted band
/// (Origin = bottom-center in source pixels, YSortOffset = 0 — the entity's Position IS where it
/// stands, so YSortSystem sorts by the feet line). No GraphicsDevice — the texture is nullable and
/// sliced entries carry their own Source rect.
///
/// <para>The drop-a-PNG wave adds one branch: a folded <c>.anim</c> entry
/// (<see cref="AssetCatalogEntry.IsSequence"/>) also gets a <c>SpriteAnimationComponent</c> whose
/// frames are the folder's PNGs as <c>file:</c> keys, full-texture and duration-defaulted; a static
/// entry gets none.</para>
/// </summary>
public class SpritePropFactoryTests
{
    private static AssetCatalogEntry WholeEntry() =>
        new("Island/props/tree01.png", regionName: null, region: null, label: "tree01", folder: "props");

    private static AssetCatalogEntry SlicedEntry(Rectangle region) =>
        new("Island/props/sheet.png", "trunk", region, label: "sheet#trunk", folder: "props");

    /// <summary>A folded <c>.anim</c> folder entry: three frames in play order, the first of which
    /// is also the entry's own path (its frame-0 / thumbnail texture).</summary>
    private static AssetCatalogEntry SequenceEntry() =>
        new("Island/fx/Torch.anim/1.png", regionName: null, region: null, label: "Torch", folder: "fx",
            sequenceFrames: new[]
            {
                "Island/fx/Torch.anim/1.png",
                "Island/fx/Torch.anim/2.png",
                "Island/fx/Torch.anim/10.png",
            });

    [Fact]
    public void SpritePropStandardStackTest()
    {
        using var world = new World();
        var band = new PaletteBand("Ground", LayerDepth: 0.9f, YSorted: false);

        var entity = SpritePropFactory.Create(world, WholeEntry(), band, new Vector2(120, 80), texture: null);

        // The standard renderable stack (VisibleComponent is CullingSystem-owned; SceneObjectComponent
        // is CreateEntityCommand-owned — neither is the factory's to set).
        Assert.True(entity.Has<EntityInfoComponent>());
        Assert.True(entity.Has<TransformComponent>());
        Assert.True(entity.Has<SpriteInfoComponent>());
        Assert.True(entity.Has<DrawComponent>());
        Assert.False(entity.Has<SceneObjectComponent>());

        var info = entity.Get<EntityInfoComponent>();
        Assert.Equal(SpritePropFactory.EntityInfoType, info.Type);
        Assert.Equal("tree01", info.Name);

        Assert.Equal(new Vector2(120, 80), entity.Get<TransformComponent>().Position);

        var sprite = entity.Get<SpriteInfoComponent>();
        Assert.Equal("file:Island/props/tree01.png", sprite.AssetKey); // the file: key serializes
        Assert.Equal(RenderTargetID.Main, sprite.Target);
        Assert.Equal(0.9f, sprite.LayerDepth); // SOURCE sort field = the band depth
        Assert.Equal(0f, sprite.YSortOffset);
        Assert.Equal(Vector2.Zero, sprite.Origin); // non-Y-sorted band: top-left origin

        Assert.Equal(DrawElementType.Sprite, entity.Get<DrawComponent>().Type);
    }

    [Fact]
    public void FeetOriginOnYSortedBandTest()
    {
        using var world = new World();
        var band = new PaletteBand("Props", LayerDepth: 0.45f, YSorted: true);
        var region = new Rectangle(32, 0, 48, 64);

        var entity = SpritePropFactory.Create(world, SlicedEntry(region), band, Vector2.Zero, texture: null);

        var sprite = entity.Get<SpriteInfoComponent>();
        // Feet-origin convention: bottom-center in SOURCE pixels, YSortOffset 0 — the transform
        // position is the feet line the Y-sort keys on.
        Assert.Equal(new Vector2(24f, 64f), sprite.Origin);
        Assert.Equal(0f, sprite.YSortOffset);
        Assert.Equal(0.45f, sprite.LayerDepth);
    }

    [Fact]
    public void SlicedEntrySourceRectTest()
    {
        using var world = new World();
        var band = new PaletteBand("Detail", LayerDepth: 0.7f, YSorted: false);
        var region = new Rectangle(8, 16, 24, 40);

        var entity = SpritePropFactory.Create(world, SlicedEntry(region), band, Vector2.Zero, texture: null);

        var sprite = entity.Get<SpriteInfoComponent>();
        Assert.Equal(region, sprite.Source); // the region IS the source rect (serialized on the sprite)
        Assert.Equal(new Vector2(24, 40), sprite.Size);
        Assert.Equal("file:Island/props/sheet.png#trunk", sprite.AssetKey);
    }

    [Fact]
    public void WholeEntryWithoutTextureFallsBackToNominalSizeTest()
    {
        using var world = new World();
        var band = new PaletteBand("Ground", LayerDepth: 0.9f, YSorted: false);

        var entity = SpritePropFactory.Create(world, WholeEntry(), band, Vector2.Zero, texture: null);

        // No region + no texture (headless / missing file): a nominal visible square, never 0×0.
        var sprite = entity.Get<SpriteInfoComponent>();
        Assert.Equal(new Rectangle(0, 0, SpritePropFactory.FallbackSizePixels, SpritePropFactory.FallbackSizePixels),
            sprite.Source);
        Assert.Equal(new Vector2(SpritePropFactory.FallbackSizePixels), sprite.Size);
    }

    // ---- Animation-folder entries also get a SpriteAnimationComponent ----

    [Fact]
    public void SequenceEntryBuildsASpriteAnimationOfFileKeyedFramesTest()
    {
        using var world = new World();
        var band = new PaletteBand("Props", LayerDepth: 0.45f, YSorted: true);

        var entity = SpritePropFactory.Create(world, SequenceEntry(), band, Vector2.Zero, texture: null);

        Assert.True(entity.Has<SpriteAnimationComponent>());
        var frames = entity.Get<SpriteAnimationComponent>().Frames;
        Assert.Equal(3, frames.Length);

        // The frame keys are the folder's PNGs composed into `file:` keys, IN PLAY ORDER — the same
        // scheme the sprite itself serializes, so a frame resolves through the same loader ladder.
        Assert.Equal("file:Island/fx/Torch.anim/1.png", frames[0].AssetKey);
        Assert.Equal("file:Island/fx/Torch.anim/2.png", frames[1].AssetKey);
        Assert.Equal("file:Island/fx/Torch.anim/10.png", frames[2].AssetKey);

        // Rectangle.Empty = "the whole frame texture": one PNG per frame, sizes only known once the
        // frame's texture loads. Duration 0 defers to the component's DefaultFrameDuration.
        foreach (var frame in frames)
        {
            Assert.Equal(Rectangle.Empty, frame.Source);
            Assert.Equal(0f, frame.Duration);
        }

        // The authored sprite still holds frame 0 (what the editor shows while the animator is frozen).
        Assert.Equal("file:Island/fx/Torch.anim/1.png", entity.Get<SpriteInfoComponent>().AssetKey);
    }

    [Fact]
    public void StaticEntryGetsNoSpriteAnimationTest()
    {
        using var world = new World();
        var band = new PaletteBand("Ground", LayerDepth: 0.9f, YSorted: false);

        var entity = SpritePropFactory.Create(world, WholeEntry(), band, Vector2.Zero, texture: null);

        // A plain PNG prop must not pay for an animator: no component, nothing to serialize.
        Assert.False(entity.Has<SpriteAnimationComponent>());
    }
}
