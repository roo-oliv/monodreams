using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System;

public sealed class CameraBoundaryPositionSystem : AEntitySetSystem<GameState>
{
    public CameraBoundaryPositionSystem(World world, IParallelRunner runner)
        : base(world.GetEntities().With<CameraBoundary>().With<Position>().AsSet(), runner)
    { }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var position = ref entity.Get<Position>();
        ref var cameraBoundary = ref entity.Get<CameraBoundary>();

        cameraBoundary.Boundary.X = (int)position.Current.X - 5;
        cameraBoundary.Boundary.Y = (int)position.Current.Y - 5;
    }
}