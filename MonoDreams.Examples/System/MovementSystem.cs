using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.Component.Input;
using MonoDreams.Examples.Input;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

public class MovementSystem(World world, IParallelRunner parallelRunner)
    : AEntitySetSystem<GameState>(world.GetEntities().With<Position>().With<InputControlled>().AsSet(), parallelRunner)
{
    protected override void Update(GameState state, in Entity entity)
    {
        if (InputState.Left.Pressed(state)) entity.Get<Position>().Current.X -= Constants.MaxWalkVelocity * state.Time;
        if (InputState.Right.Pressed(state)) entity.Get<Position>().Current.X += Constants.MaxWalkVelocity * state.Time;
        if (InputState.Up.Pressed(state)) entity.Get<Position>().Current.Y -= Constants.MaxWalkVelocity * state.Time;
        if (InputState.Down.Pressed(state)) entity.Get<Position>().Current.Y += Constants.MaxWalkVelocity * state.Time;
    }
}