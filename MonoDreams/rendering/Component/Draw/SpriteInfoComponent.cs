using System;
using System.ComponentModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;

namespace MonoDreams.Component.Draw;

public struct SpriteInfoComponent() : IComponent
{
    public Texture2D SpriteSheet = null;
    public Rectangle Source = default; // Source rectangle in the SpriteSheet
    public Vector2 Size = default; // Target rendering size on screen
    public Color Color = default;
    public RenderTargetID Target = RenderTargetID.Main; // Which RenderTarget this belongs to
    public float LayerDepth = 0; // Or use LayerDepth directly
    public NinePatchInfo? NinePatchData = null; // Optional
    public Vector2 Origin = Vector2.Zero; // Origin point in sprite coordinates (pixels from top-left)

    /// Draw the source rect MIRRORED left-to-right (SpriteEffects.FlipHorizontally). Art drawn facing one way
    /// then flipped for the other is the cheapest possible facing: no second row baked into the sheet, no second
    /// texture, and a hand-drawn PNG can be dropped in and used as-is.
    /// The flip mirrors the pixels INSIDE the destination rect, so Origin keeps being measured from the source's
    /// left edge: a sprite whose body is centred in its cell needs no origin adjustment between facings; one
    /// drawn off-centre in its cell needs a per-facing origin.
    public bool FlipHorizontally = false;

    /// Draw the source rect MIRRORED top-to-bottom (SpriteEffects.FlipVertically) — the vertical twin of
    /// <see cref="FlipHorizontally"/>, for anything whose orientation is up/down (a hanging vs standing prop,
    /// a tumbling pickup, a ceiling variant of a floor decal) without baking a second row into the sheet.
    /// The flip mirrors the pixels INSIDE the destination rect, so Origin keeps being measured from the
    /// source's top edge: a sprite whose body is centred vertically in its cell needs no origin adjustment
    /// between orientations; one drawn off-centre needs a per-orientation origin.
    /// The two flags OR together — both set mirrors both axes.
    public bool FlipVertically = false;

    public float YSortDepthBias = 0f; // Applied after Y-sort interpolation for deterministic front/back ordering
    public float YSortOffset = 0f; // Y offset added to WorldPosition.Y when computing Y-sort depth (e.g. collider bottom)

    /// <summary>
    /// Content key used to load <see cref="SpriteSheet"/> (e.g. "Atlas/TX Player"), or <c>null</c>.
    /// A live <see cref="Texture2D"/> cannot be serialized, so the level-editor scene serializer
    /// persists this string and rehydrates the texture on load via <c>ContentManager.Load</c>.
    /// Additive and optional (default <c>null</c>) — existing construction sites are unaffected;
    /// a factory or loader sets it alongside <see cref="SpriteSheet"/> when it loads a texture.
    /// </summary>
    public string? AssetKey = null;

    public void Dispose()
    {
        SpriteSheet?.Dispose();
        GC.SuppressFinalize(this);
    }

    public Vector2 Offset = Vector2.Zero;

    public ISite? Site { get; set; } = null;
    public event EventHandler? Disposed = null;
}
