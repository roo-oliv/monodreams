using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.TextureAtlases;

namespace MonoDreams.Component;

public struct CompositeDrawInfo(
    Texture2D texture,
    NinePatchRegion2D ninePatchRegion,
    Point size,
    Color? color = null,
    Enum layer = null)
{
    public Texture2D Texture = texture;
    public NinePatchRegion2D NinePatchRegion = ninePatchRegion;
    public Point Size = size;
    public Color Color = color ?? Color.White;
    public Enum Layer = layer;
}