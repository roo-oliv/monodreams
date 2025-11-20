using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Camera;
using MonoDreams.State;
using static MonoDreams.Examples.System.SystemPhase;

namespace MonoDreams.Examples.System.Camera;

public static class CameraFollowSystem
{
    public static void Register(World world, MonoDreams.Component.Camera camera, GameState gameState)
    {
        world.System<CameraFollowTarget, Position>()
            .Kind(CameraPhase)
            .Each((Entity entity, ref CameraFollowTarget followTarget, ref Position targetPosition) =>
            {
                // Only process active targets
                if (!followTarget.IsActive) return;

                // Calculate desired camera position (target position)
                var desiredPosition = targetPosition.Current;
                var currentCameraPosition = camera.Position;

                // Calculate the distance between camera and target
                var distance = desiredPosition - currentCameraPosition;

                // Apply maximum distance constraints
                var clampedDistance = new Vector2(
                    MathHelper.Clamp(distance.X, -followTarget.MaxDistanceX, followTarget.MaxDistanceX),
                    MathHelper.Clamp(distance.Y, -followTarget.MaxDistanceY, followTarget.MaxDistanceY)
                );

                // Calculate the target position with distance constraints
                var constrainedTarget = currentCameraPosition + clampedDistance;

                // Apply damping for smooth movement
                var dampedX = MathHelper.Lerp(currentCameraPosition.X, constrainedTarget.X,
                    followTarget.DampingX * gameState.Time);
                var dampedY = MathHelper.Lerp(currentCameraPosition.Y, constrainedTarget.Y,
                    followTarget.DampingY * gameState.Time);

                // Update camera position
                camera.Position = new Vector2(dampedX, dampedY);
            });
    }
}
