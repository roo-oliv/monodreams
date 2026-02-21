using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Examples.Component.Runner;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Runner;

[With(typeof(ScoreDisplay), typeof(DynamicText))]
public class ScoreDisplaySystem(World world) : AEntitySetSystem<GameState>(world)
{
    private readonly EntitySet _players = world.GetEntities().With<RunnerState>().AsSet();

    protected override void Update(GameState state, in Entity entity)
    {
        var players = _players.GetEntities();
        if (players.Length == 0) return;

        var runnerState = players[0].Get<RunnerState>();
        ref var text = ref entity.Get<DynamicText>();
        text.TextContent = $"Score: {runnerState.Score}";
    }
}
