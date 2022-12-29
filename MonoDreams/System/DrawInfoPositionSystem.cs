using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System;

public sealed class DrawInfoPositionSystem : AEntitySetSystem<GameState>
{
    public DrawInfoPositionSystem(World world, IParallelRunner runner)
        : base(world.GetEntities().With<DrawInfo>().With<Position>().AsSet(), runner) // TODO: use position in everything so we can dispense With<Position>
    { }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var position = ref entity.Get<Position>();
        ref var drawInfo = ref entity.Get<DrawInfo>();

        drawInfo.Destination.X = (int)position.Current.X;
        drawInfo.Destination.Y = (int)position.Current.Y;
    }
}