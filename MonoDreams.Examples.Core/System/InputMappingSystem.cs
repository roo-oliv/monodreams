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

        // Tab / Shift-Tab — the UI's ordinal navigation pair (UIFocusSystem's next/prev). Shift-Tab
        // is a CHORD, which the (action, key) table above cannot express: it ORs single keys per
        // action, so mapping Tab twice would fire next AND prev on the same frame. One keyboard read
        // here drives both, under the same two gates the base honours (a scripted/injected run and a
        // modal that owns the keyboard must silence these too, or the menu would navigate itself).
        if (!SkipHardwareRead && ShouldSuppressInput?.Invoke() != true)
        {
            var keyboard = Keyboard.GetState();
            var shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            var tab = keyboard.IsKeyDown(Keys.Tab);
            InputState.MenuNext.Update(tab && !shift, state);
            InputState.MenuPrevious.Update(tab && shift, state);
        }
        // Debug convenience: reload Level_0 on Grab (K). This is an ADDITIVE load — SceneReaderSystem never
        // sweeps, so publishing it onto a world that already holds content STACKS a duplicate copy. That is
        // a corruption footgun while EDITING (a save then persists the duplicates — a double-load), so it is
        // gated OUT of Edit mode (PF-F). It stays a Play-only debug reload; the editor's own scene switches
        // all go through the transport's survivor-sparing sweep.
        if (state.RunMode != RunMode.Edit && InputState.Grab.Pressed(state))
        {
            world.Publish(new LoadLevelRequest("Level_0"));
        }
    }
}