using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Message;
using MonoDreams.State;

namespace MonoDreams.System;

public sealed class SizeSystem : AEntitySetSystem<GameState>
{
    private readonly World _world;

    public SizeSystem(World world, IParallelRunner runner)
        : base(world.GetEntities().With((in Size p) => p.HasUpdates).AsSet(), runner)
    {
        _world = world;
    }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var size = ref entity.Get<Size>();
        size.LastSize.X = size.CurrentSize.X;
        size.LastSize.Y = size.CurrentSize.Y;
        size.CurrentSize.X = size.NextSize.X;
        size.CurrentSize.Y = size.NextSize.Y;
        _world.Publish(new SizeChangeMessage(entity));
    }
}
