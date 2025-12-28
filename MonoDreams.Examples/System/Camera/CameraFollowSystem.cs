using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Camera;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Camera;

public class CameraFollowSystem : ISystem<GameState>
{
    private readonly MonoDreams.Component.Camera _camera;
    private readonly EntitySet _targetEntities;
    private Entity? _currentTarget;

    public bool IsEnabled { get; set; } = true;

    public CameraFollowSystem(World world, MonoDreams.Component.Camera camera)
    {
        _camera = camera;
        _targetEntities = world.GetEntities()
            .With<CameraFollowTarget>()
            .With<Transform>()
            .AsSet();
    }

    public void Update(GameState state)
    {
        // Find the active target (for now, just use the first one)
        Entity? activeTarget = null;
        foreach (var entity in _targetEntities.GetEntities())
        {
            var followTarget = entity.Get<CameraFollowTarget>();
            if (followTarget.IsActive)
            {
                activeTarget = entity;
                break;
            }
        }

        if (activeTarget == null) return;

        var target = activeTarget.Value;
        var followComponent = target.Get<CameraFollowTarget>();
        var targetTransform = target.Get<Transform>();

        // Calculate desired camera position (target position)
        var desiredPosition = targetTransform.Position;
        var currentCameraPosition = _camera.Position;
        
        // Calculate the distance between camera and target
        var distance = desiredPosition - currentCameraPosition;
        
        // Apply maximum distance constraints
        var clampedDistance = new Vector2(
            MathHelper.Clamp(distance.X, -followComponent.MaxDistanceX, followComponent.MaxDistanceX),
            MathHelper.Clamp(distance.Y, -followComponent.MaxDistanceY, followComponent.MaxDistanceY)
        );
        
        // Calculate the target position with distance constraints
        var constrainedTarget = currentCameraPosition + clampedDistance;
        
        // Frame-rate independent exponential smoothing
        // Using exp decay: smoothFactor = 1 - exp(-speed * deltaTime)
        // This ensures consistent behavior regardless of frame rate and never overshoots
        float smoothFactorX = 1f - (float)Math.Exp(-followComponent.DampingX * state.Time);
        float smoothFactorY = 1f - (float)Math.Exp(-followComponent.DampingY * state.Time);

        var newPosition = new Vector2(
            MathHelper.Lerp(currentCameraPosition.X, constrainedTarget.X, smoothFactorX),
            MathHelper.Lerp(currentCameraPosition.Y, constrainedTarget.Y, smoothFactorY)
        );

        // Snap to target when very close to avoid micro-jitter
        const float snapThreshold = 0.01f;
        if (Math.Abs(newPosition.X - constrainedTarget.X) < snapThreshold)
            newPosition.X = constrainedTarget.X;
        if (Math.Abs(newPosition.Y - constrainedTarget.Y) < snapThreshold)
            newPosition.Y = constrainedTarget.Y;

        _camera.Position = newPosition;
    }
    
    public void Dispose()
    {
        _targetEntities?.Dispose();
        GC.SuppressFinalize(this);
    }
}
