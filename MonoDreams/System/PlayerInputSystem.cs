using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.State;
using ButtonState = Microsoft.Xna.Framework.Input.ButtonState;

namespace MonoDreams.System;

public class PlayerInputSystem : AComponentSystem<GameState, PlayerInput>
{
    public PlayerInputSystem(World world) : base(world)
    {
    }

    protected override void Update(GameState state, ref PlayerInput playerInput)
    {
        // playerInput.Left.Update(Keyboard.GetState().IsKeyDown(Keys.Left));
        // playerInput.Right.Update(Keyboard.GetState().IsKeyDown(Keys.Right));
        // playerInput.Up.Update(Keyboard.GetState().IsKeyDown(Keys.Up));
        // playerInput.Down.Update(Keyboard.GetState().IsKeyDown(Keys.Down));
        // playerInput.CursorPosition = Mouse.GetState().Position.ToVector2();
        // playerInput.LeftClick.Update( Mouse.GetState().LeftButton == ButtonState.Pressed);
    }
}