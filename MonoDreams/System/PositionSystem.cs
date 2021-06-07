using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System
{
    public sealed class PositionSystem : AEntitySetSystem<GameState>
    {
        public PositionSystem(World world, IParallelRunner runner)
            : base(world.GetEntities().With<Position>().With<DrawInfo>().AsSet(), runner)
        { }

        protected override void Update(GameState state, in Entity entity)
        {
            Position position = entity.Get<Position>();
            ref DrawInfo drawInfo = ref entity.Get<DrawInfo>();

            drawInfo.Destination.X = position.Value.X;
            drawInfo.Destination.Y = position.Value.Y;
        }
    }
}