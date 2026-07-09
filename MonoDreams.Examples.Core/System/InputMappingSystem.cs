using DefaultEcs;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Examples.Input;
using MonoDreams.Message.Level;
using MonoDreams.Input;
using MonoDreams.State;
using MonoDreams.System.Input;

namespace MonoDreams.Examples.System;

public class InputMappingSystem(World world) : AKeyboardInputHandlingSystem
{
    public override List<(AInputState inputState, Keys)> InputMapping =>
    [
        (InputState.Up, Keys.W),
        (InputState.Down, Keys.S),
        (InputState.Left, Keys.A),
        (InputState.Right, Keys.D),
        (InputState.Jump, Keys.Space),
        (InputState.Grab, Keys.K),
        (InputState.Orb, Keys.LeftShift),
        (InputState.Orb, Keys.RightShift),
        (InputState.Exit, Keys.Escape),
        (InputState.Interact, Keys.E),
        // Editor global shortcuts (Delete / Home-frame / Z-undo / Y-redo) moved to the EditorShortcuts
        // chord table (UX3-E) — read off the raw keyboard by EditorShortcutSystem, not mapped here.
        // Palette ghost-rotate (Edit-only). Q is free; E doubles as Interact — safe by mode (in
        // Edit the game's Interact is frozen; in Play the palette is inert), the Unity/Godot
        // rotate-before-place gesture.
        (InputState.RotateCcw, Keys.Q),
        (InputState.RotateCw, Keys.E)
    ];

    public override void Update(GameState state)
    {
        base.Update(state);
        if (InputState.Grab.Pressed(state))
        {
            world.Publish(new LoadLevelRequest("Level_0"));
        }
    }
}