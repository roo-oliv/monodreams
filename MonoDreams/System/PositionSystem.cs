using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System;

public sealed class PositionSystem : AEntitySetSystem<GameState>
{
    public PositionSystem(World world, IParallelRunner runner)
        : base(world.GetEntities().With<DynamicBody>().With<DrawInfo>().AsSet(), runner)
    { }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var body = ref entity.Get<DynamicBody>();
        ref var drawInfo = ref entity.Get<DrawInfo>();

        drawInfo.Destination.X = (int)body.NextPosition.X;
        drawInfo.Destination.Y = (int)body.NextPosition.Y;

        body.LastPosition = body.CurrentPosition;
        body.CurrentPosition = body.NextPosition;
    }
}