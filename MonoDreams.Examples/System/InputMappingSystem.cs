using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Examples.Input;
using MonoDreams.Examples.Message.Level;
using MonoDreams.Input;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

public static class InputMappingSystem
{
    public static List<(AInputState inputState, Keys)> InputMapping =>
    [
        (InputState.Up, Keys.W),
        (InputState.Down, Keys.S),
        (InputState.Left, Keys.A),
        (InputState.Right, Keys.D),
        (InputState.Jump, Keys.Space),
        (InputState.Grab, Keys.LeftShift),
        (InputState.Grab, Keys.RightShift),
        (InputState.Grab, Keys.K),
        (InputState.Exit, Keys.Escape)
    ];

    public static void Register(World world, DefaultEcs.World defaultEcsWorld, GameState gameState)
    {
        world.System<InputState>()
            .Kind(Ecs.PreUpdate)
            .Each((ref InputState _) =>
            {
                foreach (var (input, key) in InputMapping)
                {
                    input.Update(Keyboard.GetState().IsKeyDown(key), gameState.TotalTime);
                }
                
                if (InputState.Grab.Pressed(gameState.TotalTime))
                {
                    defaultEcsWorld.Publish(new LoadLevelRequest("Level_0"));
                }
            });
    }

    // public override void Update(GameState state)
    // {
    //     if (InputState.Grab.Pressed(state))
    //     {
    //         world.Publish(new LoadLevelRequest("Level_0"));
    //     }
    // }
}