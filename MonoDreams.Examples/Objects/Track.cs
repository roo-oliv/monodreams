using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Cursor;
using MonoGame.SplineFlower.Spline.Types;
using CursorController = MonoDreams.Examples.Component.Cursor.CursorController;
using Transform = MonoDreams.Component.Transform;

namespace MonoDreams.Examples.Objects;

public static class Track
{
    public static Entity Create(World world)
    {
        var entity = world.CreateEntity();
        
        var points = new Vector2[]
        {
            new(-150, -100),
            new(50, -100),
            new(100, 0),
            new(0, 100),
            new(50, 0),
            new(-150, 0),
            // new(-100, 100),
            // new(-50, 100),
        };
        
        var spline = new CatMulRomSpline(
            points.Select(p => new MonoGame.SplineFlower.Transform(p))
            .Concat([new MonoGame.SplineFlower.Transform(Vector2.Zero)])
            .ToArray())
        {
            Loop = true,
        };
        
        // for (var i = 0; i < points.Length; i++)
        // {
        //     var currentTangent = spline.GetAllTangents[i];
        //     var nextPointIndex = i == points.Length - 1 ? 0 : i + 1;
        //     var desiredPosition = (points[i] + points[nextPointIndex]) / 2;
        //     var currentPosition = currentTangent.Position;
        //     var translationNeeded = desiredPosition - currentPosition;
        //     
        //     spline.SelectTransform(currentPosition);
        //     if (spline.SelectedTransform?.IsTangent == true)
        //     {
        //         spline.TranslateSelectedTransform(translationNeeded);
        //     }
        // }
        // spline.SelectedTransform = null;
        
        entity.Set(spline);
        var transform = new Transform(Vector2.Zero);
        entity.Set(transform);
        entity.Set(new CursorController());
        entity.Set(new CursorInput());
        entity.Set(new VelocityProfileComponent());
        entity.Set(new TrackScoreComponent([300, 400, 500, 600]));

        var borderEntity = world.CreateEntity();
        borderEntity.Set(transform);
        entity.Set(borderEntity);
        
        return entity;
    }
}