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
        playerInput.Left.Update(state.KeyboardState.IsKeyDown(Keys.Left));
        playerInput.Right.Update(state.KeyboardState.IsKeyDown(Keys.Right));
        playerInput.Up.Update(state.KeyboardState.IsKeyDown(Keys.Up));
        playerInput.Down.Update(state.KeyboardState.IsKeyDown(Keys.Down));
        playerInput.CursorPosition = state.MouseState.Position.ToVector2();
        playerInput.LeftClick.Update(state.MouseState.LeftButton == ButtonState.Pressed);
    }
}