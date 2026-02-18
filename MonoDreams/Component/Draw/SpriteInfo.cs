using System;
using System.ComponentModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;

namespace MonoDreams.Component.Draw;

public struct SpriteInfo() : IComponent
{
    public Texture2D SpriteSheet = null;
    public Rectangle Source = default; // Source rectangle in the SpriteSheet
    public Vector2 Size = default; // Target rendering size on screen
    public Color Color = default;
    public RenderTargetID Target = RenderTargetID.Main; // Which RenderTarget this belongs to
    public float LayerDepth = 0; // Or use LayerDepth directly
    public NinePatchInfo? NinePatchData = null; // Optional
    public Vector2 Origin = Vector2.Zero; // Origin point in sprite coordinates (pixels from top-left)

    public void Dispose()
    {
        SpriteSheet?.Dispose();
        GC.SuppressFinalize(this);
    }

    public Vector2 Offset = Vector2.Zero;

    public ISite? Site { get; set; } = null;
    public event EventHandler? Disposed = null;
}
