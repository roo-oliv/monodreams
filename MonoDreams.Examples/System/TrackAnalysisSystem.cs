using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Examples.Component;
using MonoDreams.State;
using MonoGame.SplineFlower.Spline.Types;

namespace MonoDreams.Examples.System;

[With(typeof(HermiteSpline), typeof(VelocityProfileComponent))]
public class TrackAnalysisSystem(
    World world,
    float maxSpeed = 2000f,
    float maxAcceleration = 100f,
    float maxDeceleration = 400f,
    float frictionCoefficient = 5.0f)
    : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref readonly var spline = ref entity.Get<HermiteSpline>();
        ref readonly var velocityProfileComponent = ref entity.Get<VelocityProfileComponent>();
        velocityProfileComponent.VelocityProfile = CalculateVelocityProfile(spline);
    }

    private float[] CalculateVelocityProfile(HermiteSpline spline, int samples = 1000)
    {
        var velocities = new float[samples];
        var curvatures = new float[samples];
        var maxCorneringSpeeds = new float[samples];
        
        // Step 1: Calculate curvature and maximum cornering speed for each point
        for (var i = 0; i < samples; i++)
        {
            var t = (float)i / (samples - 1);
            curvatures[i] = CalculateCurvature(spline, t);
            
            // Calculate maximum speed for this corner based on curvature
            // v_max = sqrt(friction * g * radius) where radius = 1/curvature -- removed the sqrt for racing similarity
            if (curvatures[i] > 0.001f) // Avoid division by zero
            {
                var radius = 1f / curvatures[i];
                maxCorneringSpeeds[i] = frictionCoefficient * 9.81f * radius;
                maxCorneringSpeeds[i] = MathF.Min(maxCorneringSpeeds[i], maxSpeed);
            }
            else
            {
                maxCorneringSpeeds[i] = maxSpeed;
            }
        }

        // Step 2: Forward pass - acceleration limited
        velocities[0] = maxCorneringSpeeds[0];
        
        for (var i = 1; i < samples; i++)
        {
            var t1 = (float)(i - 1) / (samples - 1);
            var t2 = (float)i / (samples - 1);
            var distance = CalculateDistance(spline, t1, t2);
            
            // Calculate maximum velocity based on acceleration limit
            var maxVelFromAccel = MathF.Sqrt(
                velocities[i - 1] * velocities[i - 1] + 2 * maxAcceleration * distance);
            
            velocities[i] = MathF.Min(maxVelFromAccel, maxCorneringSpeeds[i]);
        }

        // Step 3: Backward pass - deceleration limited
        for (var i = samples - 2; i >= 0; i--)
        {
            var t1 = (float)i / (samples - 1);
            var t2 = (float)(i + 1) / (samples - 1);
            var distance = CalculateDistance(spline, t1, t2);
            
            // Calculate maximum velocity based on deceleration limit
            var maxVelFromDecel = MathF.Sqrt(
                velocities[i + 1] * velocities[i + 1] + 2 * maxDeceleration * distance);
            
            velocities[i] = MathF.Min(velocities[i], maxVelFromDecel);
        }

        return velocities;
    }

    private float CalculateCurvature(HermiteSpline spline, float t)
    {
        const float targetDistance = 5.0f; // Target distance in world units
        
        // Calculate local parameterization density
        var baseDelta = 0.001f;
        var p1 = spline.GetPoint(Math.Max(0, t - baseDelta) * spline.MaxProgress());
        var p2 = spline.GetPoint(t * spline.MaxProgress());
        var p3 = spline.GetPoint(Math.Min(1, t + baseDelta) * spline.MaxProgress());
        
        var localDensity = (Vector2.Distance(p1, p2) + Vector2.Distance(p2, p3)) / (2 * baseDelta);
        
        // Adjust delta to achieve target distance
        var adjustedDelta = localDensity > 0 ? targetDistance / localDensity : baseDelta;
        adjustedDelta = MathHelper.Clamp(adjustedDelta, 0.0001f, 0.1f); // Reasonable bounds
        
        // Recalculate points with adjusted delta
        p1 = spline.GetPoint(Math.Max(0, t - adjustedDelta) * spline.MaxProgress());
        p2 = spline.GetPoint(t * spline.MaxProgress());
        p3 = spline.GetPoint(Math.Min(1, t + adjustedDelta) * spline.MaxProgress());

        // Calculate curvature using the three-point method
        var v1 = p2 - p1;
        var v2 = p3 - p2;
        
        var cross = v1.X * v2.Y - v1.Y * v2.X;
        var dot = Vector2.Dot(v1, v2);
        var v1Mag = v1.Length();
        var v2Mag = v2.Length();
        
        if (v1Mag == 0 || v2Mag == 0) return 0;
        
        var angle = MathF.Atan2(cross, dot);
        var avgDistance = (v1Mag + v2Mag) / 2;
        var curvature = MathF.Abs(angle) / avgDistance;
        
        return curvature;
    }

    // Calculate actual distance between two points on the spline
    private float CalculateDistance(HermiteSpline spline, float t1, float t2)
    {
        const float sampleResolution = 0.001f;
        float distance = 0;
        var steps = (int)MathF.Abs((t2 - t1) / sampleResolution);
        
        for (var i = 0; i < steps; i++)
        {
            var currentT = t1 + (t2 - t1) * i / steps;
            var nextT = t1 + (t2 - t1) * (i + 1) / steps;
            
            var p1 = spline.GetPoint(currentT * spline.MaxProgress());
            var p2 = spline.GetPoint(nextT * spline.MaxProgress());
            
            distance += Vector2.Distance(p1, p2);
        }
        
        return distance;
    }

}