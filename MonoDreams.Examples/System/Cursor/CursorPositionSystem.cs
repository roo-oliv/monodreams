using Flecs.NET.Core;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Cursor;
using CursorController = MonoDreams.Examples.Component.Cursor.CursorController;
using static MonoDreams.Examples.System.SystemPhase;

namespace MonoDreams.Examples.System.Cursor;

public static class CursorPositionSystem
{
    public static void Register(World world)
    {
        world.System<CursorController, CursorInput, Position>()
            .Kind(LogicPhase)
            .Each((ref CursorController controller, ref CursorInput input, ref Position position) =>
            {
                // Update cursor position based on input
                position.Current = input.WorldPosition + controller.HotSpot;
            });
    }
}
