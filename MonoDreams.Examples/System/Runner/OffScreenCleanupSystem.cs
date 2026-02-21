using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Examples.Runner;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Runner;

[With(typeof(EntityInfo), typeof(Transform))]
public class OffScreenCleanupSystem(World world) : AEntitySetSystem<GameState>(world)
{
    private readonly List<Entity> _toDispose = new();

    protected override void PreUpdate(GameState state)
    {
        _toDispose.Clear();
    }

    protected override void Update(GameState state, in Entity entity)
    {
        var info = entity.Get<EntityInfo>();
        // Only clean up spawned entities (collectibles and obstacles), not walls or player
        if (info.Type is not ("Collectible" or "Obstacle")) return;

        ref readonly var transform = ref entity.Get<Transform>();
        if (transform.Position.X < RunnerConstants.CleanupX)
        {
            _toDispose.Add(entity);
        }
    }

    protected override void PostUpdate(GameState state)
    {
        foreach (var entity in _toDispose)
        {
            if (entity.IsAlive) entity.Dispose();
        }
    }
}
