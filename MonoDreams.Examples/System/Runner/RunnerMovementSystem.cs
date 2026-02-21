using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Component.Physics;
using MonoDreams.Examples.Component.Runner;
using MonoDreams.Examples.Input;
using MonoDreams.Examples.Runner;
using MonoDreams.State;
using Logger = MonoDreams.State.Logger;

namespace MonoDreams.Examples.System.Runner;

[With(typeof(RunnerState), typeof(Velocity), typeof(Transform), typeof(RigidBody))]
public class RunnerMovementSystem(World world) : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        var runnerState = entity.Get<RunnerState>();
        if (runnerState.IsGameOver) return;

        var velocity = entity.Get<Velocity>();
        ref var transform = ref entity.Get<Transform>();

        // Grounded check: collision resolution zeros Y velocity when landing
        runnerState.IsGrounded = velocity.Current.Y == 0 && velocity.Last.Y >= 0;

        // Conditional drag: only when grounded (touching treadmill)
        velocity.Current.X = runnerState.IsGrounded ? -RunnerConstants.TreadmillDragSpeed : 0f;

        // Player input: run right
        if (InputState.Right.Pressed(state))
        {
            velocity.Current.X += RunnerConstants.PlayerRunSpeed;
        }

        // Jump — manual edge detection since JustPressed requires buffer > 0
        bool jumpPressed = InputState.Jump.Pressed(state);
        if (jumpPressed && !runnerState.JumpHeld && runnerState.IsGrounded)
        {
            velocity.Current.Y = RunnerConstants.PlayerJumpSpeed;
            Logger.Info("Player jumped!");
        }
        runnerState.JumpHeld = jumpPressed;

        // Variable gravity factor (applied before GravitySystem runs)
        var rigidBody = entity.Get<RigidBody>();
        if (velocity.Current.Y < 0f) // ascending
        {
            rigidBody.Gravity = (true, jumpPressed
                ? 1.0f
                : RunnerConstants.JumpCutGravityMultiplier);
        }
        else if (velocity.Current.Y > 0f) // falling
        {
            rigidBody.Gravity = (true, RunnerConstants.FallGravityMultiplier);
        }
        else // grounded
        {
            rigidBody.Gravity = (true, 1.0f);
        }

        // Visual rolling rotation
        transform.Rotate(velocity.Current.X * state.Time * RunnerConstants.PlayerRollSpeed / RunnerConstants.PlayerRadius);

        runnerState.GameTime += state.Time;
    }
}
