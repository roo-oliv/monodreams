using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.State;
using MonoGame.SplineFlower.Spline.Types;

namespace MonoDreams.Examples.System;

[With(typeof(CarComponent))]
public class RaceCarSystem(World world) : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var car = ref entity.Get<CarComponent>();
        ref var transform = ref entity.Get<Transform>();
        ref var track = ref world.GetEntities().With<HermiteSpline>().AsEnumerable().First().Get<HermiteSpline>();
        car.TrackProgress += car.Speed * state.Time;
        if (car.TrackProgress > 1f)
        {
            car.TrackProgress -= 1f;
        }
        var relativeProgress = car.TrackProgress * track.MaxProgress();
        transform.CurrentPosition = track.GetPoint(relativeProgress);
        var direction = track.GetDirection(relativeProgress);
        var rotation = (float)Math.Atan2(direction.X, -direction.Y);
        transform.CurrentRotation = rotation;
    }
}