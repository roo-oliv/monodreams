using System;
using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System.Camera;

/// <summary>
/// Copies the scene camera ENTITY's pose into the shared <see cref="MonoDreams.Component.Camera"/> render
/// adapter every frame: the entity's <c>Transform.WorldPosition</c> / <c>WorldRotation</c> and its
/// <see cref="CameraComponent.Zoom"/> become the adapter's <c>Position</c> / <c>Rotation</c> / <c>Zoom</c>.
/// The <see cref="MonoDreams.Component.Camera"/> class is now a pure render adapter (the draw stack reads
/// its view matrix); this system is the <b>only writer of the adapter in Play</b>, so the camera-as-entity
/// model needs ZERO rendering-module changes.
///
/// <para><b>Play-only (Freeze in Edit).</b> Register this wrapped in a <c>GatedSystem</c> with
/// <c>EditTimeBehavior.Freeze</c> at every site (CM pre-mortem #2): in Edit the live <see cref="Camera"/>
/// is the editor's FREE VIEW that <c>CameraNavSystem</c> drives, so syncing it from the camera entity
/// would clobber the editor's pan/zoom every frame. In Edit the camera entity is just data — moved and
/// edited like any entity. When the editor is not composed, <c>RunMode</c> is always <c>Play</c> and the
/// gate is a pass-through, so a shipped game syncs every frame.</para>
///
/// <para>It follows the first camera entity it enumerates (there is exactly one per scene — the writer
/// refuses a second and the reader ensures one exists). Reading <c>WorldPosition</c>/<c>WorldRotation</c>
/// (not the local <c>Position</c>) keeps it correct if the camera is ever parented; for a root camera they
/// equal the local values. It runs after <c>CameraFollowSystem</c> (which writes the entity) so the
/// adapter reflects this frame's followed pose.</para>
/// </summary>
public sealed class CameraSyncSystem : ISystem<GameState>
{
    private readonly MonoDreams.Component.Camera _camera;
    private readonly EntitySet _cameraEntities;

    public bool IsEnabled { get; set; } = true;

    public CameraSyncSystem(World world, MonoDreams.Component.Camera camera)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _cameraEntities = world.GetEntities()
            .With<CameraComponent>()
            .With<TransformComponent>()
            .AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        foreach (var entity in _cameraEntities.GetEntities())
        {
            var transform = entity.Get<TransformComponent>();
            _camera.Position = transform.WorldPosition;
            _camera.Rotation = transform.WorldRotation;
            _camera.Zoom = entity.Get<CameraComponent>().Zoom;
            return; // exactly one camera per scene
        }
    }

    public void Dispose()
    {
        _cameraEntities.Dispose();
        GC.SuppressFinalize(this);
    }
}
