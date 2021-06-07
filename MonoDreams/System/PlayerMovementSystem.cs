using System;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.State;
using Nez;
using Entity = DefaultEcs.Entity;

namespace MonoDreams.System
{
    public class PlayerMovementSystem : AEntitySetSystem<GameState>
    {
        private int WalkSpeed = 900;
        
        public PlayerMovementSystem(World world, IParallelRunner runner)
            : base(world.GetEntities().With<Velocity>().With<PlayerInput>().AsSet(), runner)
        {
            
        }

        protected override void Update(GameState state, in Entity entity)
        {
            ref Velocity velocity = ref entity.Get<Velocity>();
            ref PlayerInput playerInput = ref entity.Get<PlayerInput>();
            if (playerInput.Left.JustActivated)
            {
                velocity.Value.X -= WalkSpeed;
            }
            if (playerInput.Left.JustReleased)
            {
                velocity.Value.X += WalkSpeed;
            }
            if (playerInput.Right.JustActivated)
            {
                velocity.Value.X += WalkSpeed;
            }
            if (playerInput.Right.JustReleased)
            {
                velocity.Value.X -= WalkSpeed;
            }
        }
    }
}