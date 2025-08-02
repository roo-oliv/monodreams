using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Examples.Component.Cursor;
using MonoDreams.State;
using MonoGame.SplineFlower.Spline.Types;

namespace MonoDreams.Examples.System;

[With(typeof(HermiteSpline), typeof(CursorInput))]
public class SplineTransformControlSystem(World world) : AEntitySetSystem<GameState>(world)
{
    public bool IsEnabled { get; set; } = true;
    private static readonly Vector2 VoidVector = new(int.MaxValue, int.MaxValue);

    protected override void Update(GameState state, in Entity entity)
    {
        var spline = entity.Get<HermiteSpline>();
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
    }
    
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}