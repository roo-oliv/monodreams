using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.Message;
using MonoDreams.State;

namespace MonoDreams.System;

public sealed class PositionSystem : AEntitySetSystem<GameState>
{
    private readonly World _world;

    public PositionSystem(World world, IParallelRunner runner)
        : base(world.GetEntities().With((in Position p) => p.HasUpdates).AsSet(), runner)
    {
        _world = world;
    }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var position = ref entity.Get<Position>();
        position.LastLocation.X = position.CurrentLocation.X;
        position.LastLocation.Y = position.CurrentLocation.Y;
        position.CurrentLocation.X = position.NextLocation.X;
        position.CurrentLocation.Y = position.NextLocation.Y;
        position.LastOrientation = position.CurrentOrientation;
        position.CurrentOrientation = position.NextOrientation;
        _world.Publish(new PositionChangeMessage(entity));
    }
}
