using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component.Input;
using MonoDreams.Component.Physics.VelocityBased;
using MonoDreams.Examples.Input;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

public class MovementSystem(World world, IParallelRunner parallelRunner)
    : AEntitySetSystem<GameState>(world.GetEntities().With<Velocity>().With<InputControlled>().AsSet(), parallelRunner)
{
    protected override void Update(GameState state, in Entity entity)
    {
        if (InputState.Left.Pressed(state)) entity.Get<Velocity>().Current.X -= Constants.MaxWalkVelocity;
        if (InputState.Right.Pressed(state)) entity.Get<Velocity>().Current.X += Constants.MaxWalkVelocity;
    }
}