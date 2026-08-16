#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Navigation;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// Edit-time camera navigation: <b>pan</b> (middle-mouse drag) and <b>zoom</b> (scroll wheel). The
/// <b>frame-scene</b> action — centre + zoom-fit on all renderable content — is the public
/// <see cref="FrameScene"/> method, TRIGGERED by the editor-shortcut table (Home) or the
/// <c>view:frame</c> op, not a keyboard predicate on this system (UX3-E consolidated the editor
/// keyboard bindings into <c>EditorShortcuts</c>). This is the system that makes off-origin levels
/// reachable — without it the editor camera is pinned and a level whose content sits at, say,
/// ~(1275,-530) is simply off-screen.
///
/// <para><b>Edit-guarded, registered RunNormally.</b> Like the other editor systems it is pre-registered
/// in both modes but no-ops in <see cref="RunMode.Play"/> (in Play the camera follows the player via
/// <c>CameraFollowSystem</c>; this system must not fight it). It drives <c>Camera.Position</c>/<c>Zoom</c>
/// directly — the same camera the draw stack reads — per the §9 interaction matrix ("in Edit the editor
/// drives the camera").</para>
///
/// <para><b>Ordering vs <c>CursorPositionSystem</c>.</b> This system is registered in the Update phase
/// <b>before</b> <c>CursorPositionSystem</c>, so the camera mutation it makes this frame is the camera
/// state <c>CursorPositionSystem</c> then reads when it derives the cursor's world position — keeping
/// the cursor's <c>WorldPosition</c> consistent with the panned/zoomed camera within the same frame
/// (no one-frame lag between the camera moving and the cursor's world coordinate catching up). Pan
/// itself is computed from the cursor's <b>virtual</b> position (pre-camera), so panning never feeds
/// back on itself.</para>
/// </summary>
public sealed class CameraNavSystem : ISystem<GameState>
{
    // Defaults chosen here (autonomous): a 1.1× geometric zoom step gives a smooth ~10%/notch feel;
    // 0.25–4.0 is a sane editor range (4× in for pixel work, 4× out for an overview); frame-scene fits
    // with a 10% margin so content isn't flush against the screen edge.
    public const float DefaultZoomStep = 1.1f;
    public const float DefaultMinZoom = 0.25f;
    public const float DefaultMaxZoom = 4.0f;
    private const float FrameMargin = 0.9f;

    private readonly World _world;
    private readonly MonoDreams.Component.Camera _camera;
    private readonly EntitySet _cursorSet;
    private readonly EntitySet _contentSet;
    private readonly float _zoomStep;
    private readonly float _minZoom;
    private readonly float _maxZoom;

    // Pan state: the cursor's virtual position last frame, valid only while the middle button is held.
    private bool _panning;
    private Vector2 _lastPanVirtual;

    public bool IsEnabled { get; set; } = true;

    public CameraNavSystem(World world, MonoDreams.Component.Camera camera,
        float zoomStep = DefaultZoomStep, float minZoom = DefaultMinZoom, float maxZoom = DefaultMaxZoom)
    {
        _world = world;
        _camera = camera;
        _zoomStep = zoomStep;
        _minZoom = minZoom;
        _maxZoom = maxZoom;
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();
        // Renderable content = sprite entities (the editable level geometry). Mesh-only overlays
        // (gizmo handles) have no SpriteInfoComponent and are excluded — frame-scene targets content.
        _contentSet = world.GetEntities().With<SpriteInfoComponent>().With<TransformComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        // Edit-guarded: inert in Play (CameraFollowSystem owns the camera there).
        if (state.RunMode != RunMode.Edit)
        {
            _panning = false;
            return;
        }

        if (!TryGetCursor(out var cursor)) { _panning = false; return; }

        Pan(cursor);
        Zoom(cursor);
    }

    private void Pan(in CursorInputComponent cursor)
    {
        // Over the editor chrome / letterbox margins the virtual position is frozen, so a pan
        // there can't track anyway — re-anchor instead, preventing a delta spike when the cursor
        // re-enters the viewport mid-drag.
        if (cursor.OutsideViewport)
        {
            _panning = false;
            return;
        }

        // Track the virtual cursor across frames so the pan delta is in virtual (pre-camera) pixels,
        // independent of the screen→virtual letterbox scale and correct for the injected headless cursor.
        if (cursor.MiddleButton)
        {
            if (!_panning)
            {
                _panning = true; // first frame of the drag: anchor, no movement yet
            }
            else
            {
                var virtualDelta = cursor.VirtualPosition - _lastPanVirtual;
                _camera.Position = CameraNav.Pan(_camera.Position, virtualDelta, _camera.Zoom);
            }
            _lastPanVirtual = cursor.VirtualPosition;
        }
        else
        {
            _panning = false;
        }
    }

    private void Zoom(in CursorInputComponent cursor)
    {
        // Scrolling over the chrome margins belongs to the chrome (e.g. the Wave-8 panel), not
        // the world camera.
        if (cursor.OutsideViewport) return;
        if (cursor.ScrollWheelDelta == 0) return;
        // MonoGame's ScrollWheelValue moves in 120-unit notches; sign = direction (up/in is positive).
        var notches = cursor.ScrollWheelDelta / 120;
        if (notches == 0) notches = Math.Sign(cursor.ScrollWheelDelta); // sub-notch deltas still step once
        _camera.Zoom = CameraNav.Zoom(_camera.Zoom, notches, _zoomStep, _minZoom, _maxZoom);
    }

    /// <summary>
    /// Centres (and zoom-fits) the camera on the AABB of all renderable content. No content → no-op
    /// (the camera is left exactly where it was). This is the "jump to the level" affordance for
    /// off-origin levels. Public because the trigger lives in the editor-shortcut table (Home) and the
    /// <c>view:frame</c> op, both of which gate on Edit before calling it (this method does not
    /// re-check the run mode — call it only in Edit).
    /// </summary>
    public void FrameScene()
    {
        var bounds = ComputeContentBounds();
        if (bounds is not { } aabb) return; // no content: do nothing (leave the camera untouched)

        _camera.Position = CameraNav.Center(aabb);
        if (aabb.Width > 0 && aabb.Height > 0)
            _camera.Zoom = CameraNav.FitZoom(aabb, _camera.LayoutWidth, _camera.LayoutHeight,
                FrameMargin, _minZoom, _maxZoom);
    }

    /// <summary>
    /// Centres the VIEW on the current selection (the Entities-panel focus button + the
    /// <c>view:selected</c> op — Unity's F / Blender's numpad-period): position only, zoom kept (a
    /// focus, not a fit — the designer's zoom level is deliberate). Reads the selection's sprite
    /// world-quad centre when it renders, else its transform WORLD position (a child focuses where
    /// it actually sits). No selection → no-op. Returns whether something was focused.
    /// </summary>
    public bool FrameSelected()
    {
        using var selected = _world.GetEntities()
            .With<Component.SelectedComponent>().With<TransformComponent>().AsSet();
        foreach (var e in selected.GetEntities())
        {
            if (e.Has<SpriteInfoComponent>())
            {
                var quad = GizmoTransform.SpriteWorldQuad(e.Get<TransformComponent>(), e.Get<SpriteInfoComponent>());
                var center = Vector2.Zero;
                foreach (var corner in quad) center += corner;
                _camera.Position = center / quad.Length;
            }
            else
            {
                _camera.Position = e.Get<TransformComponent>().WorldPosition;
            }
            return true;
        }
        return false;
    }

    private Rectangle? ComputeContentBounds()
    {
        var quads = new List<Vector2[]>();
        foreach (var e in _contentSet.GetEntities())
            quads.Add(GizmoTransform.SpriteWorldQuad(e.Get<TransformComponent>(), e.Get<SpriteInfoComponent>()));
        return CameraNav.ContentBounds(quads);
    }

    private bool TryGetCursor(out CursorInputComponent cursor)
    {
        foreach (var e in _cursorSet.GetEntities())
        {
            cursor = e.Get<CursorInputComponent>();
            return true;
        }
        cursor = default;
        return false;
    }

    public void Dispose()
    {
        _cursorSet.Dispose();
        _contentSet.Dispose();
        GC.SuppressFinalize(this);
    }
}
