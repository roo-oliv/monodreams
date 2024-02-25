using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Renderer;
using MonoDreams.State;

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
        playerInput.Jump.Update(state.KeyboardState.IsKeyDown(Keys.Space));
        playerInput.CursorPosition = state.MouseState.Position.ToVector2();
    }
}