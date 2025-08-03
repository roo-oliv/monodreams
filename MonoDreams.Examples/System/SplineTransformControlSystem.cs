using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Examples.Component.Cursor;
using MonoDreams.State;
using MonoGame.SplineFlower.Spline.Types;

namespace MonoDreams.Examples.System;

[With(typeof(CatMulRomSpline), typeof(CursorInput))]
public class SplineTransformControlSystem(World world) : AEntitySetSystem<GameState>(world)
{
    private static readonly Vector2 VoidVector = new(int.MaxValue, int.MaxValue);

    protected override void Update(GameState state, in Entity entity)
    {
        var spline = entity.Get<CatMulRomSpline>();
        var cursorInput = entity.Get<CursorInput>();

        if (cursorInput.LeftButtonPressed)
        {
            spline.SelectTransform(cursorInput.WorldPosition);
        }

        if (cursorInput.LeftButtonReleased)
        {
            spline.SelectTransform(VoidVector);
        }

        if (spline.SelectedTransform != null)
        {
            spline.TranslateSelectedTransform(cursorInput.WorldPosition - spline.SelectedTransform.Position);
        }
        
        // spline.GetAllTangents.Last().SetPosition(spline.GetAllTangents.First().Position);
    }
}