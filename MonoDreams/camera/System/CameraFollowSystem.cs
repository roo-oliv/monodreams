using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System.Camera;

/// <summary>
/// Eases the scene camera ENTITY toward the active follow target. Under the camera-as-entity model (CM)
/// this system writes the camera <b>entity's</b> <see cref="TransformComponent.Position"/> — NOT the live
/// <see cref="MonoDreams.Component.Camera"/> adapter. <c>CameraSyncSystem</c> (registered right after this)
/// is the only writer of the adapter in Play, copying the entity's pose into it. Because the follow now
/// lands on the entity, follow state is live-inspectable in the editor (it moves the camera entity like
/// any entity).
///
/// <para>Freeze-gated in Edit exactly as before (the editor owns the free view there). When there is no
/// camera entity (a screen that never created one) or no active target, this is a no-op.</para>
/// </summary>
public class CameraFollowSystem : ISystem<GameState>
{
    private readonly EntitySet _targetEntities;
    private readonly EntitySet _cameraEntities;

    public bool IsEnabled { get; set; } = true;

    public CameraFollowSystem(World world)
    {
        _targetEntities = world.GetEntities()
            .With<CameraFollowTargetComponent>()
            .With<TransformComponent>()
            .AsSet();
        _cameraEntities = world.GetEntities()
            .With<CameraComponent>()
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

        // The camera ENTITY is what we move (CM). No camera entity ⇒ nothing to follow.
        Entity? cameraEntity = null;
        foreach (var entity in _cameraEntities.GetEntities()) { cameraEntity = entity; break; }
        if (cameraEntity == null) return;
        var cameraTransform = cameraEntity.Value.Get<TransformComponent>();

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
        var currentCameraPosition = cameraTransform.Position;

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

        cameraTransform.Position = newPosition;
    }

    public void Dispose()
    {
        _targetEntities?.Dispose();
        _cameraEntities?.Dispose();
        GC.SuppressFinalize(this);
    }
}
