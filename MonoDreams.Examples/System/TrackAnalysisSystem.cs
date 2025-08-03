using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Examples.Component;
using MonoDreams.State;
using MonoGame.SplineFlower.Spline.Types;

namespace MonoDreams.Examples.System;

[With(typeof(CatMulRomSpline), typeof(VelocityProfileComponent))]
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
        ref readonly var spline = ref entity.Get<CatMulRomSpline>();
        ref var velocityProfileComponent = ref entity.Get<VelocityProfileComponent>();
        (velocityProfileComponent.VelocityProfile, velocityProfileComponent.DistanceProfile) = CalculateVelocityProfile(spline);
        velocityProfileComponent.TotalTrackLength = velocityProfileComponent.DistanceProfile.Length > 0 ? 
            velocityProfileComponent.DistanceProfile[^1] : 0;

        // Calculate track statistics
        if (velocityProfileComponent.VelocityProfile.Length <= 0) return;
        velocityProfileComponent.MaxSpeed = velocityProfileComponent.VelocityProfile.Max();
        velocityProfileComponent.MinSpeed = velocityProfileComponent.VelocityProfile.Min();
        velocityProfileComponent.AverageSpeed = velocityProfileComponent.VelocityProfile.Average();

        // Calculate lap time based on velocity profile
        // velocityProfileComponent.LapTime = CalculateLapTime(velocityProfileComponent.VelocityProfile, velocityProfileComponent.DistanceProfile);

        // Analyze overtaking opportunities
        velocityProfileComponent.OvertakingOpportunities = AnalyzeOvertakingOpportunities(
            velocityProfileComponent.VelocityProfile, 
            velocityProfileComponent.DistanceProfile, 
            spline);

        velocityProfileComponent.StatsCalculated = true;
    }

    private (float[] velocities, float[] distances) CalculateVelocityProfile(CatMulRomSpline spline, int samples = 1000)
    {
        var velocities = new float[samples];
        var distances = new float[samples];
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

        // Calculate cumulative distances along the spline
        distances[0] = 0f;
        for (var i = 1; i < samples; i++)
        {
            var t1 = (float)(i - 1) / (samples - 1);
            var t2 = (float)i / (samples - 1);
            var segmentDistance = CalculateDistance(spline, t1, t2);
            distances[i] = distances[i - 1] + segmentDistance;
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

        return (velocities, distances);
    }

    private float CalculateCurvature(CatMulRomSpline spline, float t)
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
    private float CalculateDistance(CatMulRomSpline spline, float t1, float t2)
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

    private List<OvertakingOpportunity> AnalyzeOvertakingOpportunities(float[] velocities, float[] distances, CatMulRomSpline spline)
    {
        if (velocities.Length <= 10 || distances.Length <= 10)
            return [];

        var opportunities = new List<OvertakingOpportunity>();
        var samples = velocities.Length;

        // Parameters for finding overtaking opportunities
        var minStraightLength = 100f;            // Minimum straight section length in world units
        var minSpeedThreshold = 0.4f;            // Minimum speed to be considered high-speed (as a fraction of max speed)
        var minSpeedDifferential = 200f;         // Minimum speed differential for good braking zone
        var searchWindowSize = 80;                 // Number of samples to look ahead for deceleration

        var maxSpeed = 1000f;
        var highSpeedThreshold = maxSpeed * minSpeedThreshold;

        var straightStartIndex = -1;

        // Find high-speed sections followed by deceleration zones
        for (var i = 0; i < samples - searchWindowSize; i++)
        {
            // Start of a potential straight section
            if (straightStartIndex == -1 && velocities[i] >= highSpeedThreshold)
            {   
                straightStartIndex = i;
            }
            // We're already tracking a straight section
            else if (straightStartIndex != -1)
            {
                // If speed drops below threshold, we've reached the end of the straight
                if (velocities[i] < highSpeedThreshold) 
                {
                    // Calculate straight section length
                    var straightLength = distances[i] - distances[straightStartIndex];

                    // Find minimum speed within our search window ahead
                    var minSpeedAhead = float.MaxValue;
                    var minSpeedIndex = -1;
                    for (var j = i; j < Math.Min(i + searchWindowSize, samples); j++)
                    {   
                        if (velocities[j] < minSpeedAhead)
                        {   
                            minSpeedAhead = velocities[j];
                            minSpeedIndex = j;
                        }
                    }

                    // Calculate speed differential
                    var speedDifferential = velocities[i-1] - minSpeedAhead; // Use last high-speed point

                    // If this is a good overtaking opportunity
                    if (straightLength >= minStraightLength && speedDifferential >= minSpeedDifferential)
                    {
                        // Calculate the quality score based on straight length and speed differential
                        var lengthFactor = MathF.Min(1.0f, straightLength / 400f);  // Normalize length, cap at 400 units
                        var speedFactor = MathF.Min(1.0f, speedDifferential / 500f); // Normalize speed diff, cap at 500 units/s
                        var quality = (lengthFactor * 0.7f) + (speedFactor * 0.3f);  // 70% length, 30% speed diff importance

                        // Create the overtaking opportunity
                        var opportunity = new OvertakingOpportunity
                        {
                            StartPercentage = (float)straightStartIndex / (samples - 1),
                            EndPercentage = (float)minSpeedIndex / (samples - 1),
                            StraightLength = straightLength,
                            EntrySpeed = velocities[straightStartIndex],
                            ExitSpeed = minSpeedAhead,
                            SpeedDifferential = speedDifferential,
                            Quality = quality
                        };

                        opportunities.Add(opportunity);
                    }

                    // Reset straight start index to look for the next straight
                    straightStartIndex = -1;
                }
            }
        }

        // Handle the case where the track ends with a straight section
        if (straightStartIndex != -1 && straightStartIndex < samples - 10)
        {
            var endIndex = samples - 1;
            var straightLength = distances[endIndex] - distances[straightStartIndex];

            // For a track that ends in a straight, we can consider the beginning of the track
            // for the braking zone (assuming the track is a loop)
            var minSpeedAhead = velocities.Take(Math.Min(50, samples / 10)).Min();
            var minSpeedIndex = Array.IndexOf(velocities, minSpeedAhead);

            var speedDifferential = velocities[endIndex] - minSpeedAhead;

            if (straightLength >= minStraightLength && speedDifferential >= minSpeedDifferential)
            {
                var lengthFactor = MathF.Min(1.0f, straightLength / 400f);
                var speedFactor = MathF.Min(1.0f, speedDifferential / 500f);
                var quality = (lengthFactor * 0.7f) + (speedFactor * 0.3f);

                opportunities.Add(new OvertakingOpportunity
                {
                    StartPercentage = (float)straightStartIndex / (samples - 1),
                    EndPercentage = (float)minSpeedIndex / (samples - 1),
                    StraightLength = straightLength,
                    EntrySpeed = velocities[straightStartIndex],
                    ExitSpeed = minSpeedAhead,
                    SpeedDifferential = speedDifferential,
                    Quality = quality
                });
            }
        }

        // Merge nearby or overlapping opportunities
        opportunities = MergeNearbyOpportunities(opportunities, (float)0.1);

        // Sort opportunities by quality (best first)
        return opportunities.OrderByDescending(o => o.Quality).ToList();
    }

    private List<OvertakingOpportunity> MergeNearbyOpportunities(List<OvertakingOpportunity> opportunities, float threshold)
    {
        if (opportunities.Count <= 1)
            return opportunities;

        // Sort by start percentage to make merging easier
        var sortedOpportunities = opportunities.OrderBy(o => o.StartPercentage).ToList();
        var result = new List<OvertakingOpportunity>();

        var current = sortedOpportunities[0];

        for (var i = 1; i < sortedOpportunities.Count; i++)
        {
            var next = sortedOpportunities[i];

            // If the next opportunity is close to or overlaps with the current one
            if (next.StartPercentage - current.EndPercentage <= threshold || 
                next.StartPercentage <= current.EndPercentage) // Overlap
            {
                // Merge them by taking the earliest start and latest end
                current.EndPercentage = Math.Max(current.EndPercentage, next.EndPercentage);
                current.StraightLength = Math.Max(current.StraightLength, next.StraightLength);
                current.SpeedDifferential = Math.Max(current.SpeedDifferential, next.SpeedDifferential);
                current.Quality = Math.Max(current.Quality, next.Quality);
            }
            else
            {
                // No overlap, add the current opportunity to results and move to the next one
                result.Add(current);
                current = next;
            }
        }

        // Add the last opportunity
        result.Add(current);

        return result;
    }

    private float CalculateLapTime(float[] velocityProfile, float[] distanceProfile)
    {
        if (velocityProfile.Length <= 1 || distanceProfile.Length <= 1 || velocityProfile.Length != distanceProfile.Length)
            return 0f;

        float totalTime = 0f;

        // Calculate time for each segment based on average velocity and distance
        for (int i = 1; i < velocityProfile.Length; i++)
        {
            // Get segment distance
            float segmentDistance = distanceProfile[i] - distanceProfile[i - 1];

            // Get average velocity for this segment (using trapezoidal approximation)
            float avgVelocity = (velocityProfile[i] + velocityProfile[i - 1]) / 2f;

            // Calculate time for this segment (t = d/v) and add to total
            // Avoid division by zero
            if (avgVelocity > 0.001f)
            {
                totalTime += segmentDistance / avgVelocity;
            }
        }

        return totalTime;
    }
}