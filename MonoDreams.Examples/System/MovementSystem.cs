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
        world.System<InputControlled, Velocity>()
            .Kind(Ecs.OnUpdate)
            .Each((ref InputControlled _, ref Velocity velocity) =>
            {
                var totalTime = gameState.TotalTime;

                // Reset velocity
                velocity.Current = Vector2.Zero;

                // Set velocity based on input
                if (InputState.Left.Pressed(totalTime)) velocity.Current.X -= Constants.MaxWalkVelocity;
                if (InputState.Right.Pressed(totalTime)) velocity.Current.X += Constants.MaxWalkVelocity;
                if (InputState.Up.Pressed(totalTime)) velocity.Current.Y -= Constants.MaxWalkVelocity;
                if (InputState.Down.Pressed(totalTime)) velocity.Current.Y += Constants.MaxWalkVelocity;
            });
    }
}