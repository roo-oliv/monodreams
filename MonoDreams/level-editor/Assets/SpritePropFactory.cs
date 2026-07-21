#nullable enable
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.UI;

namespace MonoDreams.LevelEditor.Assets;

/// <summary>
/// The generic sprite-prop factory (island-authoring plan §3.2): turns an
/// <see cref="AssetCatalogEntry"/> + a screen-supplied <see cref="PaletteBand"/> into the standard
/// renderable entity stack — <c>EntityInfoComponent</c> (asset-derived name),
/// <c>TransformComponent</c>, <c>SpriteInfoComponent</c> (the <c>file:</c> AssetKey, the region's
/// <c>Source</c> rect for sliced entries, and the SOURCE sort fields for the band),
/// <c>DrawComponent</c>. The island is mostly art, not gameplay prototypes, so every catalog entry
/// is instantly placeable with zero game code; game factories (<c>IEntityFactory</c>) join the
/// same palette later via the screen-supplied palette model.
///
/// <para><b>Feet-origin convention (premise).</b> On a Y-sorted band the sprite's
/// <c>Origin</c> is its bottom-center (in source pixels) and <c>YSortOffset</c> is 0: the entity's
/// <c>Position</c> IS where it <i>stands</i> — the sprite renders with its feet at the transform
/// position, and <c>YSortSystem</c> (which sorts by <c>WorldPosition.Y + YSortOffset</c>) sorts by
/// that same feet line, so the player walks behind a tree when above it with no per-prop tuning.
/// Non-Y-sorted (ground) bands keep the default top-left origin; their within-band order is
/// authored (bring-forward/send-back, Slice 2).</para>
///
/// <para><b>Authoring path == runtime path.</b> This is a plain builder over the world — the same
/// call shape <c>CreateEntityCommand</c>'s <c>Func&lt;World, Entity&gt;</c> wraps for the one-undo-step
/// placement, and the same stack the scene reader reconstructs on load. The command (not this
/// factory) tags <c>SceneObjectComponent</c>; the ghost preview deliberately bypasses the command
/// and never gets the tag.</para>
/// </summary>
public static class SpritePropFactory
{
    /// <summary>The fallback source size when the texture is unavailable (a headless test, or a
    /// whole-PNG entry whose file is missing and whose placeholder carries no meaningful size).</summary>
    public const int FallbackSizePixels = 32;

    /// <summary>The <c>EntityInfoComponent.Type</c> every placed sprite prop carries.</summary>
    public const string EntityInfoType = "Prop";

    /// <summary>
    /// Builds the standard sprite-prop stack at <paramref name="position"/> (the feet point on a
    /// Y-sorted band, the top-left otherwise — see the class doc). <paramref name="texture"/> is
    /// the lazily-loaded texture for the entry (nullable: headless tests run textureless; the
    /// sprite then skips rendering until rehydrated). <paramref name="rotation"/> (radians) orients
    /// the prop — the palette's ghost-rotate (Q/E) passes the armed rotation so straight/curve road
    /// pieces and props land oriented (Slice 4); 0 for the common axis-aligned case.
    /// </summary>
    public static Entity Create(World world, AssetCatalogEntry entry, PaletteBand band,
        Vector2 position, Texture2D? texture, float rotation = 0f)
    {
        var source = SourceRect(entry, texture);

        var entity = world.CreateEntity();
        entity.Set(new EntityInfoComponent(EntityInfoType, entry.Label));
        entity.Set(new TransformComponent(position, rotation));
        entity.Set(new SpriteInfoComponent
        {
            SpriteSheet = texture,
            AssetKey = entry.AssetKey, // the file: key — what the scene serializes + rehydrates
            Source = source,
            Size = new Vector2(source.Width, source.Height),
            Color = EditorTheme.NeutralTint,
            Target = RenderTargetID.Main,
            // SOURCE sort fields per the band (never the derived DrawComponent.LayerDepth):
            LayerDepth = band.LayerDepth,
            YSortOffset = 0f,
            Origin = band.YSorted ? FeetOrigin(source) : Vector2.Zero,
        });
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.Main,
        });
        // No VisibleComponent here: CullingSystem owns it (adds it when the prop enters the
        // camera view, next draw frame). No SceneObjectComponent: the placement path's
        // CreateEntityCommand tags the root; the ghost must never carry it.
        return entity;
    }

    /// <summary>The feet-origin (bottom-center, in source pixels) for a Y-sorted prop.</summary>
    public static Vector2 FeetOrigin(Rectangle source) => new(source.Width / 2f, source.Height);

    /// <summary>The entry's source rectangle: the sliced region when present, else the whole
    /// texture, else a <see cref="FallbackSizePixels"/> square.</summary>
    public static Rectangle SourceRect(AssetCatalogEntry entry, Texture2D? texture)
    {
        if (entry.Region.HasValue) return entry.Region.Value;
        if (texture != null) return new Rectangle(0, 0, texture.Width, texture.Height);
        return new Rectangle(0, 0, FallbackSizePixels, FallbackSizePixels);
    }
}
