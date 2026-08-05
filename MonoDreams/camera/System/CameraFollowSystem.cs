using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System.Camera;

/// <summary>
/// "Ease the camera onto THIS entity instead of the active follow target", or <see cref="Release"/> to hand
/// it back to whatever <see cref="CameraFollowTargetComponent"/> is active.
///
/// <para><b>A directed pan is not a follow.</b> A look-at ignores the target's max-distance LEASH — which
/// exists to keep the camera near a moving player, not to meter out a deliberate move across the level — and
/// keeps its damping and its bounds. Without that, pointing the camera at something on the far side of the
/// world crawls there at the leash's per-frame cap (200px x 12.5% = 25px a frame, i.e. seconds of nothing),
/// and a cutscene that has to wait for the camera is a cutscene nobody writes twice.</para>
///
/// <para>Entity-typed and engine-level on purpose: a boss reveal, a cutscene and a death cam are one request,
/// and routing it through the follow system keeps a SINGLE writer of the camera entity's position — a game
/// system shoving the transform around itself would race this one every frame.</para>
/// </summary>
public readonly record struct CameraLookAt(Entity Target, bool Release = false);

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
    private readonly IDisposable _lookAtSubscription;

    /// <summary>The <see cref="CameraLookAt"/> subject, or a dead handle for "none". Held as a plain entity
    /// rather than a flag on some component so that a subject which is disposed mid-flight (a boss that dies
    /// while the camera is on him) falls back to the follow target on its own, instead of parking the view on
    /// a corpse's last coordinates until something remembers to release it.</summary>
    private Entity _lookAt;

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
        _lookAtSubscription = world.Subscribe<CameraLookAt>(OnLookAt);
    }

    private void OnLookAt(in CameraLookAt request) => _lookAt = request.Release ? default : request.Target;

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

        // A look-at overrides only WHERE the camera is going; the damping and the bounds still come from the
        // active follow target, so a pan cannot change the camera's feel and handing control back cannot make
        // it lurch. Still gated on there being an active target at all — a look-at is a detour from a follow,
        // not a replacement for one.
        var lookingAt = _lookAt.IsAlive && _lookAt.Has<TransformComponent>();
        if (lookingAt) targetTransform = _lookAt.Get<TransformComponent>();

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

        // Apply maximum distance constraints — except under a look-at, where the leash is exactly the wrong
        // instinct: it is there to stop the camera racing ahead of a moving player, and applying it to a
        // deliberate pan across the level meters the move out at 25px a frame.
        var clampedDistance = lookingAt ? distance : new Vector2(
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
        _lookAtSubscription?.Dispose();
        _targetEntities?.Dispose();
        _cameraEntities?.Dispose();
        GC.SuppressFinalize(this);
    }
}
