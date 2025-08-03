using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

[With(typeof(StatComponent), typeof(DynamicText))]
public class TrackScoreDrawPrepSystem(World world) : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        var track = world.GetEntities().With<VelocityProfileComponent>().AsEnumerable().FirstOrDefault();
        var trackScore = track.Get<TrackScoreComponent>();

        ref readonly var statComponent = ref entity.Get<StatComponent>();
        ref var dynamicText = ref entity.Get<DynamicText>();

        var statValue = GetStatValue(statComponent.Type, trackScore);
        dynamicText.TextContent = statValue;
        dynamicText.VisibleCharacterCount = dynamicText.TextContent.Length;
        dynamicText.IsRevealed = true;
    }

    private string GetStatValue(StatType statType, TrackScoreComponent trackScore)
    {
        return statType switch
        {
            StatType.TopSpeed => $"{trackScore.TopSpeed:D}",
            StatType.OvertakingSpots => $"{trackScore.OvertakingSpots:D}",
            StatType.Score => $"{trackScore.Score:D}",
            StatType.Grade => trackScore.CurrentGrade?.ToString() ?? "",
            _ => ""
        };
    }
}
