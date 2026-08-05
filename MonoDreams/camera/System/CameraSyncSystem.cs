using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
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
    private readonly bool _pixelSnap;

    public bool IsEnabled { get; set; } = true;

    /// <param name="world">The world whose camera entity drives the adapter.</param>
    /// <param name="camera">The render adapter this system writes each frame in Play.</param>
    /// <param name="pixelSnap">
    /// Round the camera's position to whole world pixels on its way to the adapter. This is the third of
    /// the THREE snaps a <b>hard-snap retro</b> look needs — an integer zoom, snapped sprite AND text
    /// positions (<c>SpritePrepSystem</c>/<c>TextPrepSystem</c>'s <c>pixelPerfectRendering</c>), and a
    /// snapped camera (this flag) — after which every art pixel is an exact NxN block of screen pixels at
    /// all times; the cost is that following advances in whole world pixels. The equally first-class
    /// alternative is <b>smooth-scroll pixel art</b>: leave this off, let the camera move freely at output
    /// resolution, and displace the composed frame by the sub-virtual-pixel remainder at composite/blit
    /// time (a screen-composition concern outside this system) — buttery movement, crisp pixels, cut border
    /// pixels. What is broken is the MIX: the view transform is <c>(world - camera) * zoom + centre</c>, so
    /// a fractional camera with the zoom applied INSIDE it samples art at fractional positions and the
    /// whole world shimmers and crawls whenever the camera moves — snapping the sprites alone just moves
    /// the fraction, because the subtraction reintroduces it. Pick a style and be consistent. Defaults to
    /// <c>false</c>, so an existing game renders byte-identically.
    /// </param>
    public CameraSyncSystem(World world, MonoDreams.Component.Camera camera, bool pixelSnap = false)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _pixelSnap = pixelSnap;
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
            var position = transform.WorldPosition;
            // The snap lives on the adapter COPY only — the entity's eased position stays fractional and
            // smooth, so follow easing is unaffected and the authored/inspectable value is untouched.
            _camera.Position = _pixelSnap
                ? new Vector2(MathF.Round(position.X), MathF.Round(position.Y))
                : position;
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
