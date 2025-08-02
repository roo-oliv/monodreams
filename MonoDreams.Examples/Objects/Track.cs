using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Cursor;
using MonoGame.SplineFlower;
using MonoGame.SplineFlower.Spline;
using MonoGame.SplineFlower.Spline.Types;
using CursorController = MonoDreams.Examples.Component.Cursor.CursorController;
using Transform = MonoDreams.Component.Transform;

namespace MonoDreams.Examples.Objects;

public static class Track
{
    public static Entity Create(
        World world, Texture2D trackTexture)
    {
        var entity = world.CreateEntity();
        
        var spline = new HermiteSpline([
            new MonoGame.SplineFlower.Transform(new Vector2(0, 0)),
            new MonoGame.SplineFlower.Transform(new Vector2(100, 100)),
            new MonoGame.SplineFlower.Transform(new Vector2(200, 80)),
            new MonoGame.SplineFlower.Transform(new Vector2(300, 150)),
            new MonoGame.SplineFlower.Transform(new Vector2(150, 0)),
            new MonoGame.SplineFlower.Transform(new Vector2(10, 200)),
        ])
        {
            Loop = true,
            PolygonStripeTexture = trackTexture,
            PolygonStripeWidth = 10f,
        };
        entity.Set(spline);
        entity.Set(new Transform(Vector2.Zero));
        entity.Set(new CursorController());
        entity.Set(new CursorInput());
        entity.Set(new VelocityProfileComponent());
        
        return entity;
    }
}