using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Input;
using MonoDreams.Component.Physics;
using MonoDreams.Examples.Input;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

public static class MovementSystem
{
    public static void Register(World world, GameState gameState)
    {
        world.System<InputControlled, Position, Velocity>()
            .Kind(Ecs.OnUpdate)
            .Each(( ref InputControlled _, ref Position position, ref Velocity _) =>
            {
                var totalTime = gameState.TotalTime;
                var displacement = Constants.MaxWalkVelocity * gameState.Time;
                
                if (InputState.Left.Pressed(totalTime)) position.Current.X -= displacement;
                if (InputState.Right.Pressed(totalTime)) position.Current.X += displacement;
                if (InputState.Up.Pressed(totalTime)) position.Current.Y -= displacement;
                if (InputState.Down.Pressed(totalTime)) position.Current.Y += displacement;
            });
    }
}