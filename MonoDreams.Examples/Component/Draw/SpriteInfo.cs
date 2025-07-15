using System.ComponentModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;

namespace MonoDreams.Examples.Component.Draw;

public struct SpriteInfo : IComponent
{
    public Texture2D SpriteSheet;
    public Rectangle Source; // Source rectangle in the SpriteSheet
    public Vector2 Size; // Target rendering size on screen
    public Color Color;
    public RenderTargetID Target; // Which RenderTarget this belongs to
    public float LayerDepth; // Or use LayerDepth directly
    public NinePatchInfo? NinePatchData; // Optional
    public void Dispose()
    {
        SpriteSheet?.Dispose();
        GC.SuppressFinalize(this);
    }

    public Vector2 Offset
    {
        get => _offset ?? Vector2.Zero;
        set => _offset = value;
    }

    public ISite? Site { get; set; }
    public event EventHandler? Disposed;
    
    private Vector2? _offset;
}