using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;

namespace MonoDreams.Objects;

public static class StaticImage
{
    public static Entity Create(RenderTarget2D renderTarget, World world, Vector2 location, Texture2D texture, Point? size = null, Rectangle? source = null, Color? color = null, Enum drawLayer = null)
    {
        var entity = world.CreateEntity();
        entity.Set(new Transform(location));
        entity.Set(new DrawInfo(renderTarget, texture, size, source, color, drawLayer));
        return entity;
    }
}