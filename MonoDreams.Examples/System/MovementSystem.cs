using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.Component.Input;
using MonoDreams.Examples.Input;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

public class MovementSystem(World world, IParallelRunner parallelRunner)
    : AEntitySetSystem<GameState>(world.GetEntities().With<Transform>().With<InputControlled>().AsSet(), parallelRunner)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var transform = ref entity.Get<Transform>();
        if (InputState.Left.Pressed(state)) transform.Position.X -= Constants.MaxWalkVelocity * state.Time;
        if (InputState.Right.Pressed(state)) transform.Position.X += Constants.MaxWalkVelocity * state.Time;
        if (InputState.Up.Pressed(state)) transform.Position.Y -= Constants.MaxWalkVelocity * state.Time;
        if (InputState.Down.Pressed(state)) transform.Position.Y += Constants.MaxWalkVelocity * state.Time;
    }
}