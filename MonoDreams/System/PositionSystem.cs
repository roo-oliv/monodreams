using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System;

public sealed class PositionSystem : AEntitySetSystem<GameState>
{
    public PositionSystem(World world, IParallelRunner runner)
        : base(world.GetEntities().With<Position>().AsSet(), runner)
    { }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var position = ref entity.Get<Position>();
        position.LastLocation.X = position.CurrentLocation.X;
        position.LastLocation.Y = position.CurrentLocation.Y;
        position.CurrentLocation.X = position.NextLocation.X;
        position.CurrentLocation.Y = position.NextLocation.Y;
        position.LastOrientation = position.CurrentOrientation;
        position.CurrentOrientation = position.NextOrientation;
    }
}