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
        // Get track entity with velocity profile
        var trackEntity = world.GetEntities().With<VelocityProfileComponent>().AsEnumerable().FirstOrDefault();
        if (trackEntity == null || !trackEntity.Has<VelocityProfileComponent>())
            return;

        var velocityProfile = trackEntity.Get<VelocityProfileComponent>();
        if (!velocityProfile.StatsCalculated)
            return;

        // Get the stat component to determine which stat to display
        ref readonly var statComponent = ref entity.Get<StatComponent>();
        ref var dynamicText = ref entity.Get<DynamicText>();

        // Update the text content based on stat type
        var statLabel = GetStatLabel(statComponent.Type);
        var statValue = GetStatValue(statComponent.Type, velocityProfile);

        // Format with one decimal place
        dynamicText.TextContent = $"{statLabel}: {statValue:D}";
        dynamicText.VisibleCharacterCount = dynamicText.TextContent.Length;
        dynamicText.IsRevealed = true;
    }

    private string GetStatLabel(StatType statType)
    {
        return statType switch
        {
            StatType.MaxSpeed => "Max Speed",
            StatType.MinSpeed => "Min Speed",
            StatType.AverageSpeed => "Avg Speed",
            _ => "Unknown Stat"
        };
    }

    private int GetStatValue(StatType statType, VelocityProfileComponent velocityProfile)
    {
        return statType switch
        {
            StatType.MaxSpeed => (int)velocityProfile.MaxSpeed,
            StatType.MinSpeed => (int)velocityProfile.MinSpeed,
            StatType.AverageSpeed => (int)velocityProfile.AverageSpeed,
            _ => 0
        };
    }
}
