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
        var targetPosition = target.Get<Transform>();
        
        // Calculate desired camera position (target position)
        var desiredPosition = targetPosition.CurrentPosition;
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
        
        // Apply damping for smooth movement
        var dampedX = MathHelper.Lerp(currentCameraPosition.X, constrainedTarget.X, 
            followComponent.DampingX * state.Time);
        var dampedY = MathHelper.Lerp(currentCameraPosition.Y, constrainedTarget.Y, 
            followComponent.DampingY * state.Time);
        
        // Update camera position
        _camera.Position = new Vector2(dampedX, dampedY);
    }
    
    public void Dispose()
    {
        _targetEntities?.Dispose();
        GC.SuppressFinalize(this);
    }
}
