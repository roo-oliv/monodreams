using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Examples.Component;
using MonoDreams.State;
using MonoGame.SplineFlower.Spline.Types;

namespace MonoDreams.Examples.System.Draw;

[With(typeof(HermiteSpline))]
public class BoundaryIntersectionSystem(World world) : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref readonly var spline = ref entity.Get<HermiteSpline>();
        ref var boundaryComponent = ref world.GetEntities().With<LevelBoundaryComponent>().AsEnumerable().First().Get<LevelBoundaryComponent>();

        if (boundaryComponent.TrackEvaluated && !boundaryComponent.IsTrackInsideBoundary)
        {
            // If the track is not inside the boundary, update the velocity profile
            if (entity.Has<VelocityProfileComponent>())
            {
                ref var velocityProfile = ref entity.Get<VelocityProfileComponent>();
                // Set speeds to zero to indicate an invalid track
                velocityProfile.MaxSpeed = 0;
                velocityProfile.MinSpeed = 0;
                velocityProfile.AverageSpeed = 0;
                // Clear overtaking opportunities
                velocityProfile.OvertakingOpportunities.Clear();
            }
        }
    }
}
