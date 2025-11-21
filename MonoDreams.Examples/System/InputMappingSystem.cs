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

    public static void Register(World world, GameState gameState)
    {
        world.System<InputState>()
            .Kind(Ecs.PreUpdate)
            .Each((ref InputState _) =>
            {
                foreach (var (input, key) in InputMapping)
                {
                    input.Update(Keyboard.GetState().IsKeyDown(key), gameState.TotalTime);
                }

                // Note: Level loading functionality will be re-added when level loading systems
                // (LevelLoadRequestSystem, LDtkTileParserSystem, etc.) are migrated to Flecs
            });
    }
}