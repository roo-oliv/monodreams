using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System.Camera;

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
            .With<CameraFollowTargetComponent>()
            .With<TransformComponent>()
            .AsSet();
    }

    public void Update(GameState state)
    {
        // Find the active target (for now, just use the first one)
        Entity? activeTarget = null;
        foreach (var entity in _targetEntities.GetEntities())
        {
            var followTarget = entity.Get<CameraFollowTargetComponent>();
            if (followTarget.IsActive)
            {
                activeTarget = entity;
                break;
            }
        }

        if (activeTarget == null) return;

        var target = activeTarget.Value;
        var followComponent = target.Get<CameraFollowTargetComponent>();
        var targetTransform = target.Get<TransformComponent>();

        // Calculate desired camera position (target position). Clamp the *target*
        // to the optional follow bounds here, before smoothing, so the camera always
        // eases toward an in-bounds goal — including easing smoothly back inside when
        // it starts outside the bounds (e.g. control handed back from an unbounded
        // target). Clamping the resolved position after smoothing instead would
        // hard-cap X/Y each frame and snap the camera to the edge in that case.
        var desiredPosition = targetTransform.Position;
        if (followComponent.Bounds is { } bounds)
        {
            desiredPosition.X = MathHelper.Clamp(desiredPosition.X, bounds.Left, bounds.Right);
            desiredPosition.Y = MathHelper.Clamp(desiredPosition.Y, bounds.Top, bounds.Bottom);
        }
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
