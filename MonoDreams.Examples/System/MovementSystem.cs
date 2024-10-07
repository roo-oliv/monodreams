using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.Component.Input;
using MonoDreams.Component.Physics;
using MonoDreams.Examples.Input;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

public class MovementSystem(World world, IParallelRunner parallelRunner)
    : AEntitySetSystem<GameState>(world.GetEntities().With<Position>().With<Velocity>().With<InputControlled>().AsSet(), parallelRunner)
{
    protected override void Update(GameState state, in Entity entity)
    {
        if (InputState.Left.Pressed(state)) entity.Get<Position>().Current.X -= Constants.MaxWalkVelocity * state.Time;
        if (InputState.Right.Pressed(state)) entity.Get<Position>().Current.X += Constants.MaxWalkVelocity * state.Time;
        if (InputState.Jump.Pressed(state))
        {
            entity.Get<Velocity>().Current.Y = Constants.JumpVelocity;
        }
    }
}