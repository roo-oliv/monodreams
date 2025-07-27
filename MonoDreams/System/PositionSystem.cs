using Flecs.NET.Core;
using MonoDreams.Component;

namespace MonoDreams.System;

public static class PositionSystem
{
    public static void Register(World world)
    {
        world.System<Position>()
            .Kind(Ecs.OnUpdate)
            .Each((ref Position position) =>
            {
                position.LastDelta = position.Delta;
                position.Last = position.Current;
            });
    }
}
