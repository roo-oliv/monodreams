#nullable enable
using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// The editor's <b>camera rig</b> (UX2-E): a standalone entity that holds the AUTHORED game-camera
/// state, split from the free editor VIEW (the shared <see cref="Camera"/> the viewport looks through,
/// which <c>CameraNavSystem</c> keeps driving unchanged). It is composition infrastructure the
/// <see cref="EditorOverlay"/> owns — like <see cref="EditorTransport"/> / <c>EditorHistory</c> — not a
/// per-frame pipeline system: it owns the rig entity, re-syncs it from <c>scene.camera</c> on every
/// load, captures it back for Save, snaps the view to it (the <c>view:camera</c> op), and emits its
/// frustum glyph in the draw-phase overlay pass.
///
/// <para><b>The rig entity.</b> A <see cref="CameraRigComponent"/> (zoom + rotation) + a
/// <see cref="TransformComponent"/> whose <c>Position</c> is the camera centre (so the ordinary gizmo
/// moves it via a <c>TransformEditCommand</c> — the write-back target is the rig's own transform, so no
/// new proxy machinery is needed) + <see cref="EditorInfrastructureComponent"/> (survives a transport
/// Restart, hidden from the Entities tree) + a mesh <see cref="DrawComponent"/> on the native-resolution
/// <c>RenderTargetID.Editor</c> target (the glyph visual, identity <c>WorldMatrix</c>, NO
/// <c>VisibleComponent</c> per the chrome rule). It is NEVER <c>SceneObjectComponent</c>-tagged, so it
/// never enters <c>entities[]</c> (pre-mortem #4); <c>CullingSystem</c> ignores it (no
/// <c>SpriteInfoComponent</c>).</para>
///
/// <para><b>The glyph.</b> While the view differs from the rig (position/zoom epsilon —
/// <see cref="CameraRigGlyph"/>), the overlay draws the rig's frustum world-rect (virtual resolution ÷
/// rig zoom, centred on the rig) as bounds + the X of corner diagonals (Blender's off-camera glyph),
/// through the SAME <see cref="OverlayProjection"/> path the gizmo/proxy overlays use, clipped to the
/// game viewport, in the <see cref="UI.EditorTheme.CameraGlyph"/> role. When the view matches the rig
/// ("you ARE the camera") the glyph hides (the mesh empties).</para>
/// </summary>
public sealed class EditorCameraRig
{
    /// <summary>Glyph stroke thickness in VIRTUAL pixels (aspect-fit scaled to screen pixels by the
    /// emission, never zoom-compensated — the overlay-size convention).</summary>
    private const float GlyphPixelThickness = 2f;

    private readonly World _world;
    private readonly Camera _camera; // the free editor VIEW (also the source of the immutable virtual size)
    private readonly ViewportManager? _viewportManager;
    private readonly Entity _rig;

    public EditorCameraRig(World world, Camera camera, ViewportManager? viewportManager = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _viewportManager = viewportManager;

        // Materialize the rig, initialized to the current view (a sensible default before any scene
        // load re-syncs it from the file — "the authored camera starts where the view is").
        _rig = world.CreateEntity();
        _rig.Set(new EditorInfrastructureComponent()); // survives a transport Restart, hidden from the tree
        _rig.Set(new TransformComponent(camera.Position));
        _rig.Set(new CameraRigComponent(camera.Zoom, camera.Rotation));
        _rig.Set(new DrawComponent
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

    /// <summary>The rig entity — exposed for the delete-guard (<see cref="Owns"/>) and tests.</summary>
    public Entity Entity => _rig;

    /// <summary>Whether <paramref name="entity"/> is the rig (the delete command path refuses it).</summary>
    public bool Owns(Entity entity) => entity.IsAlive && _rig.IsAlive && entity.Equals(_rig);

    /// <summary>The rig position (camera centre) — the gizmo edits it as an ordinary transform.</summary>
    public Vector2 Position => _rig.IsAlive ? _rig.Get<TransformComponent>().Position : Vector2.Zero;

    /// <summary>The authored camera zoom held on the rig.</summary>
    public float Zoom => _rig.IsAlive ? _rig.Get<CameraRigComponent>().Zoom : 1f;

    /// <summary>The authored camera rotation held on the rig.</summary>
    public float Rotation => _rig.IsAlive ? _rig.Get<CameraRigComponent>().Rotation : 0f;

    /// <summary>
    /// Re-syncs the rig's STATE from a loaded <c>scene.camera</c> (its identity is unchanged — the rig
    /// entity survives Restart/reload/switch). Null (a legacy camera-less scene) leaves the rig as-is.
    /// Wired to <c>SceneReaderSystem</c>'s rig seam, so every load rebuilds the rig from the file.
    /// </summary>
    public void SyncFromScene(SceneCameraData? camera)
    {
        if (camera == null || !_rig.IsAlive) return;
        _rig.Get<TransformComponent>().Position = new Vector2(
            camera.Position.Length > 0 ? camera.Position[0] : 0f,
            camera.Position.Length > 1 ? camera.Position[1] : 0f);
        _rig.Get<CameraRigComponent>() = new CameraRigComponent(camera.Zoom, camera.Rotation);
    }

    /// <summary>
    /// The rig's state as a throwaway <see cref="Camera"/> for <c>SceneWriter.BuildScene</c> — so Save
    /// captures <c>scene.camera</c> FROM THE RIG, never the live VIEW (moving the view must never change
    /// what Save writes). The virtual size comes from the shared camera (immutable), so the produced
    /// <c>scene.camera</c> matches what a freshly loaded rig would round-trip byte-identically.
    /// </summary>
    public Camera AsCamera()
    {
        var camera = new Camera(_camera.VirtualWidth, _camera.VirtualHeight)
        {
            Position = Position,
            Rotation = Rotation,
        };
        camera.Zoom = Zoom; // the setter clamps to >= 0.1 (rig zoom is a sane 0.25..4.0, so unaffected)
        return camera;
    }

    /// <summary>The <c>view:camera</c> op / the header nav-corner button: snap the free VIEW onto the rig
    /// (<c>Camera := rig state</c>) — the back-to-camera-view affordance. After this the view matches the
    /// rig, so the glyph hides.</summary>
    public void SnapViewToRig()
    {
        _camera.Position = Position;
        _camera.Zoom = Zoom;
        _camera.Rotation = Rotation;
        Logger.Info("[level-editor] view:camera — snapped the editor view to the camera rig.");
    }

    /// <summary>
    /// Emits (or hides) the rig's frustum glyph for this frame, in screen pixels on the Editor target —
    /// called from the draw-phase overlay pass (<c>EditorOverlayPrepSystem</c>) after the camera is
    /// final. Hidden (mesh emptied) outside Edit or when the view matches the rig; otherwise the frustum
    /// world-rect (bounds + the X of corner diagonals) projected through <see cref="OverlayProjection"/>
    /// and clipped to the game viewport.
    /// </summary>
    public void EmitGlyph(GameState state)
    {
        if (!_rig.IsAlive) return;
        ref var draw = ref _rig.Get<DrawComponent>();

        if (state.RunMode != RunMode.Edit
            || CameraRigGlyph.ViewMatchesRig(_camera.Position, _camera.Zoom, Position, Zoom))
        {
            // Park the glyph (empty mesh — MasterRenderSystem skips an invalid/empty mesh): outside Edit,
            // or "you ARE the camera".
            draw.Vertices = Array.Empty<Microsoft.Xna.Framework.Graphics.VertexPositionColor>();
            draw.Indices = Array.Empty<int>();
            return;
        }

        var projection = OverlayProjection.For(RenderTargetID.Main, _camera, _viewportManager);
        var world = CameraRigGlyph.FrustumWorldCorners(Position, Zoom, _camera.VirtualWidth, _camera.VirtualHeight);
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
}
