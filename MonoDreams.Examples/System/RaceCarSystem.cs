using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
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
        ref var track = ref world.GetEntities().With<CatMulRomSpline>().AsEnumerable().First().Get<CatMulRomSpline>();
        
        UpdateCarPosition(track, car, transform, state.Time);
    }
    
    public void UpdateCarPosition(in CatMulRomSpline spline, in CarComponent car, in Transform transform, float deltaTime)
    {
        // Calculate current velocity from Transform position delta
        var currentVelocity = GetCurrentVelocityFromTransform(transform, deltaTime);
        
        // Get target velocity from velocity profile
        var targetVelocity = GetTargetVelocityAtPercentage(car.TrackProgress);
        
        // Calculate acceleration/deceleration needed
        var velocityDifference = targetVelocity - currentVelocity;
        var acceleration = CalculateAcceleration(car, velocityDifference, currentVelocity, deltaTime);
        
        // Calculate new velocity
        var newVelocity = currentVelocity + acceleration * deltaTime;
        newVelocity = MathF.Max(0, newVelocity); // Don't go backwards
        
        // Calculate distance to move this frame
        var distanceToMove = newVelocity * deltaTime;
        
        // Move car along spline
        MoveCarAlongSpline(spline, car, transform, distanceToMove);
    }

    private float GetCurrentVelocityFromTransform(in Transform transform, float deltaTime)
    {
        if (deltaTime <= 0) return 0f;
        
        // Calculate velocity from position delta
        var velocityMagnitude = transform.PositionDelta.Length() / deltaTime;
        return velocityMagnitude;
    }

    private float GetTargetVelocityAtPercentage(float percentage)
    {
        var velocityProfile = world.GetEntities().With<VelocityProfileComponent>().AsEnumerable().First().Get<VelocityProfileComponent>().VelocityProfile;
        var index = percentage * (velocityProfile.Length - 1);
        var lowerIndex = (int)MathF.Floor(index);
        var upperIndex = (int)MathF.Ceiling(index);
        
        lowerIndex = Math.Clamp(lowerIndex, 0, velocityProfile.Length - 1);
        upperIndex = Math.Clamp(upperIndex, 0, velocityProfile.Length - 1);
        
        if (lowerIndex == upperIndex)
        {
            return velocityProfile[lowerIndex];
        }
        
        // Linear interpolation between samples
        var t = index - lowerIndex;
        return velocityProfile[lowerIndex] * (1 - t) + velocityProfile[upperIndex] * t;
    }

    private float CalculateAcceleration(in CarComponent car, float velocityDifference, float currentVelocity, float deltaTime)
    {
        float maxAccelThisFrame = velocityDifference > 0 ? car.MaxAcceleration : car.MaxDeceleration;
        
        // Apply drag force: F_drag = 0.5 * ρ * v² * Cd * A
        // Simplified: drag_accel = k * v² where k is a combined drag constant
        var dragAcceleration = car.DragCoefficient * currentVelocity * currentVelocity / car.Mass;
        
        // Calculate required acceleration, accounting for drag
        var requiredAcceleration = velocityDifference / deltaTime;
        
        if (velocityDifference > 0) // Accelerating
        {
            requiredAcceleration += dragAcceleration; // Need extra to overcome drag
            return MathF.Min(requiredAcceleration, maxAccelThisFrame);
        }
        else // Decelerating
        {
            requiredAcceleration -= dragAcceleration; // Drag helps with braking
            return MathF.Max(requiredAcceleration, -maxAccelThisFrame);
        }
    }

    private void MoveCarAlongSpline(in CatMulRomSpline spline, in CarComponent car, in Transform transform, float distanceToMove)
    {
        if (distanceToMove <= 0) return;
        var velocityProfile = world.GetEntities().With<VelocityProfileComponent>().AsEnumerable().First().Get<VelocityProfileComponent>().VelocityProfile;
        
        // Use iterative approach to find new position
        var currentT = car.TrackProgress;
        var searchStep = 1f / (velocityProfile.Length - 1) * 0.1f; // Small step for search
        var accumulatedDistance = 0f;
        
        var lastPosition = spline.GetPoint(currentT * spline.MaxProgress());
        
        // Search forward along the spline until we've covered the required distance
        while (accumulatedDistance < distanceToMove && currentT < 1f)
        {
            currentT += searchStep;
            currentT = MathF.Min(currentT, 1f);
            
            var nextPosition = spline.GetPoint(currentT * spline.MaxProgress());
            var segmentDistance = Vector2.Distance(lastPosition, nextPosition);
            
            if (accumulatedDistance + segmentDistance >= distanceToMove)
            {
                // Interpolate exact position within this segment
                var remainingDistance = distanceToMove - accumulatedDistance;
                var segmentRatio = segmentDistance > 0 ? remainingDistance / segmentDistance : 0;
                
                // Interpolate between last and next position
                var finalT = (currentT - searchStep) + searchStep * segmentRatio;
                var finalPosition = spline.GetPoint(finalT * spline.MaxProgress());
                var finalDirection = spline.GetDirection(finalT * spline.MaxProgress());
                
                transform.CurrentPosition = finalPosition;
                transform.CurrentRotation = (float)Math.Atan2(finalDirection.X, -finalDirection.Y);
                car.TrackProgress = finalT;
                return;
            }
            
            accumulatedDistance += segmentDistance;
            lastPosition = nextPosition;
        }
        
        // If we reached the end of the track
        if (currentT >= 1f)
        {
            var endPosition = spline.GetPoint(spline.MaxProgress());
            var endDirection = spline.GetDirection(spline.MaxProgress());
            transform.CurrentPosition = endPosition;
            transform.CurrentRotation = (float)Math.Atan2(endDirection.X, -endDirection.Y);
            car.TrackProgress = 0f;
        }
    }
}