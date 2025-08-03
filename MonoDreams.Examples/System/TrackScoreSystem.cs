using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

[With(typeof(TrackScoreComponent))]
public class TrackScoreSystem(World world) : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        var boundary = world.GetEntities().With<LevelBoundaryComponent>().AsEnumerable().FirstOrDefault().Get<LevelBoundaryComponent>();
        var track = world.GetEntities().With<VelocityProfileComponent>().AsEnumerable().FirstOrDefault();
        var gradeDisplay = world.GetEntities().With<TrackGradeDisplayComponent>().AsEnumerable().FirstOrDefault().Get<TrackGradeDisplayComponent>();
        var scoreComponent = track.Get<TrackScoreComponent>();
        if (track == null || !track.Has<VelocityProfileComponent>())
            return;

        var velocityProfile = track.Get<VelocityProfileComponent>();
        if (!velocityProfile.StatsCalculated)
            return;

        var isValidTrack = boundary.IntersectionPoints.Count == 0;
        var topSpeed = isValidTrack ? (int)velocityProfile.MaxSpeed : 0;
        var overtakingSpots = isValidTrack ? velocityProfile.OvertakingOpportunityCount : 0;
        scoreComponent.UpdateScore(topSpeed, overtakingSpots);
        foreach (var grade in gradeDisplay.Displays.Keys)
        {
            gradeDisplay.Displays[grade].label.Get<DynamicText>().Color = Color.DarkSlateGray;
            ref var threshold = ref gradeDisplay.Displays[grade].threshold.Get<DynamicText>();
            threshold.Color = Color.DarkSlateGray;
            threshold.TextContent = scoreComponent.ScoreThresholds[3-(int)grade].ToString();
        }
        if (scoreComponent.CurrentGrade.HasValue)
        {
            var (label, threshold) = gradeDisplay.Displays[scoreComponent.CurrentGrade.Value];
            label.Get<DynamicText>().Color = Color.White;
            threshold.Get<DynamicText>().Color = Color.White;
        }
    }
}
