using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

[With(typeof(StatComponent), typeof(DynamicText))]
public class TrackStatsReportSystem(World world) : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        var boundary = world.GetEntities().With<LevelBoundaryComponent>().AsEnumerable().FirstOrDefault().Get<LevelBoundaryComponent>();
        var track = world.GetEntities().With<VelocityProfileComponent>().AsEnumerable().FirstOrDefault();
        if (track == null || !track.Has<VelocityProfileComponent>())
            return;

        var velocityProfile = track.Get<VelocityProfileComponent>();
        if (!velocityProfile.StatsCalculated)
            return;

        // Get the stat component to determine which stat to display
        ref readonly var statComponent = ref entity.Get<StatComponent>();
        ref var dynamicText = ref entity.Get<DynamicText>();

        // Update the text content based on stat type
        var statValue = GetStatValue(statComponent.Type, velocityProfile, boundary);

        // Format with one decimal place
        dynamicText.TextContent = $"{statValue:D}";
        dynamicText.VisibleCharacterCount = dynamicText.TextContent.Length;
        dynamicText.IsRevealed = true;
    }

    private int GetStatValue(StatType statType, VelocityProfileComponent velocityProfile, LevelBoundaryComponent boundary)
    {
        var isValid = boundary.IntersectionPoints.Count == 0;
        return statType switch
        {
            StatType.TopSpeed => isValid ? (int)velocityProfile.MaxSpeed : 0,
            StatType.MinSpeed => isValid ? (int)velocityProfile.MinSpeed : 0,
            StatType.AverageSpeed => isValid ? (int)velocityProfile.AverageSpeed : 0,
            StatType.OvertakingSpots => isValid ? velocityProfile.OvertakingOpportunityCount : 0,
            StatType.BestOvertakingQuality => isValid ? (int)(velocityProfile.BestOvertakingQuality * 100) : 0,
            StatType.Score => isValid ? (int)velocityProfile.MaxSpeed * velocityProfile.OvertakingOpportunityCount : 0,
            _ => 0
        };
    }
}
