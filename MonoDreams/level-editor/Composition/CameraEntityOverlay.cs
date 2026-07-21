#nullable enable
using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// The editor affordances that let the designer SEE and RETURN TO the scene camera (CM). Under the
/// camera-as-entity model the authored camera is an ordinary <c>core.Camera</c> scene entity
/// (<see cref="CameraComponent"/> + <see cref="TransformComponent"/>), not a special editor rig — so this
/// class owns NO camera state. It owns only a standalone <b>glyph overlay entity</b> (like
/// <see cref="EditorGrid"/>) and reads the scene's camera entity to (a) draw its frustum glyph while the
/// free VIEW differs from it and (b) snap the VIEW back onto it (<c>view:camera</c>).
///
/// <para><b>The glyph.</b> While the free VIEW differs from the camera entity (position/zoom epsilon —
/// <see cref="CameraEntityGlyph"/>), the overlay draws the camera's frustum world-rect (virtual
/// resolution ÷ the camera's <see cref="CameraComponent.Zoom"/>, centred on its <c>WorldPosition</c>) as
/// bounds + the X of corner diagonals (Blender's off-camera glyph), through the SAME
/// <see cref="OverlayProjection"/> path the gizmo/proxy overlays use, clipped to the game viewport, in the
/// <see cref="UI.EditorTheme.CameraGlyph"/> role. When the view matches the camera ("you ARE the camera")
/// the glyph hides (the mesh empties). Naturally inert in a prefab context (a prefab has no camera entity,
/// so the emitter finds none).</para>
///
/// <para><b>The overlay entity</b> carries <see cref="EditorInfrastructureComponent"/> (survives a
/// transport Restart, excluded from the scene tree/save) + a mesh <see cref="DrawComponent"/> on the
/// native-resolution <c>RenderTargetID.Editor</c> target (identity <c>WorldMatrix</c>, NO
/// <c>VisibleComponent</c> — the chrome rule). It is NEVER <c>SceneObjectComponent</c>-tagged. The camera
/// ENTITY itself is scene content (moved/edited/deleted like any entity) — this overlay merely visualizes
/// it.</para>
/// </summary>
public sealed class CameraEntityOverlay
{
    /// <summary>Glyph stroke thickness in VIRTUAL pixels (aspect-fit scaled to screen pixels by the
    /// emission, never zoom-compensated — the overlay-size convention).</summary>
    private const float GlyphPixelThickness = 2f;

    private readonly Camera _camera; // the free editor VIEW (also the source of the immutable virtual size)
    private readonly ViewportManager? _viewportManager;
    // The scene's camera entity (there is exactly one — the reader ensures it). Read, never owned.
    private readonly EntitySet _cameraEntities;
    // UX3-D gate (default permissive → back-compat): when it returns false the glyph is hidden entirely
    // ("Camera" overlay off, or the Game-mode sandbox) — the view/camera divergence rule applies only when on.
    private readonly Func<bool>? _glyphVisible;
    private readonly Entity _glyph;

    public CameraEntityOverlay(World world, Camera camera, ViewportManager? viewportManager = null,
        Func<bool>? glyphVisible = null)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _viewportManager = viewportManager;
        _glyphVisible = glyphVisible;
        _cameraEntities = world.GetEntities()
            .With<CameraComponent>().With<TransformComponent>().AsSet();

        // The standalone glyph overlay entity (chrome, not scene content).
        _glyph = world.CreateEntity();
        _glyph.Set(new EditorInfrastructureComponent()); // survives a transport Restart, hidden from the tree
        _glyph.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Editor,
            LayerDepth = UI.EditorTheme.Depths.CameraGlyph,
            WorldMatrix = Matrix.Identity,
            Vertices = Array.Empty<Microsoft.Xna.Framework.Graphics.VertexPositionColor>(),
            Indices = Array.Empty<int>(),
        });
        // NO VisibleComponent — the chrome rule (the Editor pass renders every matching entity, and its
        // presence would pull the mesh into MeshPrepSystem, which overwrites the identity WorldMatrix the
        // screen-baked glyph vertices require).
    }

    /// <summary>The glyph overlay entity — exposed for tests.</summary>
    public Entity GlyphEntity => _glyph;

    /// <summary>The scene camera entity, or a dead entity when the scene has none (a prefab context).</summary>
    private bool TryGetCameraEntity(out Entity camera)
    {
        foreach (var e in _cameraEntities.GetEntities())
            if (e.IsAlive) { camera = e; return true; }
        camera = default;
        return false;
    }

    /// <summary>The <c>view:camera</c> op / the header nav-corner button: snap the free VIEW onto the
    /// scene camera entity (<c>Camera := camera-entity state</c>) — the back-to-camera-view affordance.
    /// After this the view matches the camera, so the glyph hides. No-op (logs) when the scene has no
    /// camera entity (a prefab context).</summary>
    public void SnapViewToCameraEntity()
    {
        if (!TryGetCameraEntity(out var camera))
        {
            Logger.Warning("[level-editor] view:camera — no camera entity to snap the view to.");
            return;
        }
        ref readonly var t = ref camera.Get<TransformComponent>();
        _camera.Position = t.WorldPosition;
        _camera.Zoom = camera.Get<CameraComponent>().Zoom;
        _camera.Rotation = t.WorldRotation;
        Logger.Info("[level-editor] view:camera — snapped the editor view to the scene camera entity.");
    }

    /// <summary>
    /// Emits (or hides) the scene camera's frustum glyph for this frame, in screen pixels on the Editor
    /// target — called from the draw-phase overlay pass (<c>EditorOverlayPrepSystem</c>) after the camera
    /// is final. Hidden (mesh emptied) outside Edit, when the "Camera" overlay toggle / Game-mode sandbox
    /// gate is off, when the scene has no camera entity, or when the view matches the camera; otherwise the
    /// frustum world-rect (bounds + the X of corner diagonals) projected through
    /// <see cref="OverlayProjection"/> and clipped to the game viewport.
    /// </summary>
    public void EmitGlyph(GameState state)
    {
        if (!_glyph.IsAlive) return;
        ref var draw = ref _glyph.Get<DrawComponent>();

        if (state.RunMode != RunMode.Edit
            || !(_glyphVisible?.Invoke() ?? true)
            || !TryGetCameraEntity(out var camera))
        {
            Park(ref draw);
            return;
        }

        var cameraPos = camera.Get<TransformComponent>().WorldPosition;
        var cameraZoom = camera.Get<CameraComponent>().Zoom;
        if (CameraEntityGlyph.ViewMatchesCamera(_camera.Position, _camera.Zoom, cameraPos, cameraZoom))
        {
            // "You ARE the camera" — the view sits on the camera entity, so the frustum glyph hides.
            Park(ref draw);
            return;
        }

        var projection = OverlayProjection.For(RenderTargetID.Main, _camera, _viewportManager);
        var world = CameraEntityGlyph.FrustumWorldCorners(
            cameraPos, cameraZoom, _camera.VirtualWidth, _camera.VirtualHeight);
        var s = new Vector2[4];
        for (var i = 0; i < 4; i++) s[i] = projection.ToScreen(world[i]);
        var thickness = projection.ToScreenSize(GlyphPixelThickness);

        var mesh = OverlayMeshClip.ClipToRect(
            new CompositeMeshGenerator()
                .Add(new PolygonOutlineMeshGenerator(s, thickness, UI.EditorTheme.CameraGlyph, closed: true))
                .Add(new LineMeshGenerator(s[0], s[2], thickness, UI.EditorTheme.CameraGlyph)) // TL→BR
                .Add(new LineMeshGenerator(s[1], s[3], thickness, UI.EditorTheme.CameraGlyph)) // TR→BL
                .Generate(),
            projection.Viewport);

        draw.Type = DrawElementType.Mesh;
        draw.Vertices = mesh.Vertices;
        draw.Indices = mesh.Indices;
        draw.PrimitiveType = mesh.PrimitiveType;
        draw.WorldMatrix = Matrix.Identity;
        draw.Target = RenderTargetID.Editor;
        draw.LayerDepth = UI.EditorTheme.Depths.CameraGlyph;
    }

    /// <summary>Park the glyph (empty mesh — MasterRenderSystem skips an invalid/empty mesh).</summary>
    private static void Park(ref DrawComponent draw)
    {
        draw.Vertices = Array.Empty<Microsoft.Xna.Framework.Graphics.VertexPositionColor>();
        draw.Indices = Array.Empty<int>();
    }
}
