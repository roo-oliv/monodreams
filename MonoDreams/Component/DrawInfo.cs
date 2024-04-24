using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoDreams.Component;

public struct DrawInfo(
    Texture2D spriteSheet,
    Point? size = null,
    Rectangle? source = null,
    Color? color = null,
    Enum layer = null)
{
    public Texture2D SpriteSheet = spriteSheet;
    public Point Size = size ?? spriteSheet.Bounds.Size;
    public Rectangle Source = source ?? spriteSheet.Bounds;
    public Color Color = color ?? Color.White;
    public Enum Layer = layer;
}