#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.LevelEditor.UI;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// The editor's world-space reference <b>grid</b> (UX3-D §3): composition infrastructure the
/// <see cref="EditorOverlay"/> owns (like <see cref="CameraEntityOverlay"/>), not a per-frame pipeline
/// system — it owns ONE mesh entity and bakes the frame's grid into it from the draw-phase overlay
/// pass (<c>EditorOverlayPrepSystem</c>), through the SAME <see cref="OverlayProjection"/> path the
/// gizmo/proxy/glyph overlays use (screen-baked on the native <c>RenderTargetID.Editor</c> target,
/// clipped to the game viewport, identity <c>WorldMatrix</c>, NO <c>VisibleComponent</c>).
///
/// <para><b>Spacing is the snap step.</b> The grid draws lines at the SHARED grid quantum
/// (<see cref="GizmoStateComponent.GridStep"/>, read through the injected <c>spacing</c> accessor),
/// so the displayed grid is the grid things snap to — there is no second spacing value.</para>
///
/// <para><b>Bounded + gated.</b> The visible-world line count is bounded by <see cref="GridGeometry"/>
/// (Full → MajorOnly → None; pre-mortem #5). The grid is hidden outside <see cref="RunMode.Edit"/>
/// (like the camera glyph) and whenever the injected <c>visible</c> gate is false (ShowGrid off, or the
/// Game-mode sandbox — "the sandbox looks like the game"). It occupies the LOWEST overlay depth
/// (<see cref="EditorTheme.Depths.Grid"/>), beneath every other overlay + the opaque panels.</para>
/// </summary>
public sealed class EditorGrid
{
    /// <summary>Minor line stroke thickness in VIRTUAL pixels (aspect-fit scaled to screen, never
    /// zoom-compensated — the overlay-size convention).</summary>
    private const float MinorPixelThickness = 1f;

    /// <summary>Major (every-5th) line stroke thickness in VIRTUAL pixels — a touch heavier so the
    /// cadence reads.</summary>
    private const float MajorPixelThickness = 1.5f;

    private readonly Camera _camera;
    private readonly ViewportManager? _viewportManager;
    private readonly Func<float> _spacing; // reads GizmoStateComponent.GridStep — the ONE grid quantum
    private readonly Func<bool> _visible;  // ShowGrid && ViewMode != Game
    private readonly Entity _grid;

    public EditorGrid(World world, Camera camera, ViewportManager? viewportManager,
        Func<float> spacing, Func<bool> visible)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _viewportManager = viewportManager;
        _spacing = spacing ?? throw new ArgumentNullException(nameof(spacing));
        _visible = visible ?? throw new ArgumentNullException(nameof(visible));

        _grid = world.CreateEntity();
        _grid.Set(new EditorInfrastructureComponent()); // survives a transport Restart, hidden from the tree
        _grid.Set(new TransformComponent()); // identity — vertices are baked in screen space
        _grid.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Editor,
            LayerDepth = EditorTheme.Depths.Grid,
            WorldMatrix = Matrix.Identity,
            Vertices = Array.Empty<VertexPositionColor>(),
            Indices = Array.Empty<int>(),
        });
        // NO VisibleComponent — the chrome rule (its presence would pull the mesh into MeshPrepSystem,
        // which overwrites the identity WorldMatrix the screen-baked vertices require).
    }

    /// <summary>The grid entity — exposed for tests.</summary>
    public Entity Entity => _grid;

    /// <summary>
    /// Bakes (or hides) the grid for this frame. Hidden (empty mesh) outside Edit or when the visible
    /// gate is false; otherwise the world-space lines at the shared spacing across the VISIBLE world
    /// range, projected + clipped to the game viewport, every 5th line stronger.
    /// </summary>
    public void EmitGrid(GameState state)
    {
        if (!_grid.IsAlive) return;
        ref var draw = ref _grid.Get<DrawComponent>();

        if (state.RunMode != RunMode.Edit || !_visible())
        {
            Park(ref draw);
            return;
        }

        var spacing = _spacing();
        if (spacing <= 0f)
        {
            Park(ref draw);
            return;
        }

        var (left, top, right, bottom) = VisibleWorldAabb();
        var plan = GridGeometry.Plan(left, top, right, bottom, spacing);
        if (plan.LineCount == 0)
        {
            Park(ref draw);
            return;
        }

        var projection = OverlayProjection.For(RenderTargetID.Main, _camera, _viewportManager);
        var vertices = new List<VertexPositionColor>(plan.LineCount * 4);
        var indices = new List<int>(plan.LineCount * 6);
        var offset = 0;

        // Vertical lines: constant world X, spanning the visible Y range.
        foreach (var line in plan.VerticalLines)
            AddLine(vertices, indices, ref offset, projection,
                new Vector2(line.Coordinate, top), new Vector2(line.Coordinate, bottom), line.Major);
        // Horizontal lines: constant world Y, spanning the visible X range.
        foreach (var line in plan.HorizontalLines)
            AddLine(vertices, indices, ref offset, projection,
                new Vector2(left, line.Coordinate), new Vector2(right, line.Coordinate), line.Major);

        var mesh = OverlayMeshClip.ClipToRect(
            new MeshData(vertices.ToArray(), indices.ToArray()), projection.Viewport);

        draw.Type = DrawElementType.Mesh;
        draw.Vertices = mesh.Vertices;
        draw.Indices = mesh.Indices;
        draw.PrimitiveType = mesh.PrimitiveType;
        draw.WorldMatrix = Matrix.Identity;
        draw.Target = RenderTargetID.Editor;
        draw.LayerDepth = EditorTheme.Depths.Grid;
    }

    private void AddLine(List<VertexPositionColor> vertices, List<int> indices, ref int offset,
        in OverlayProjection projection, Vector2 worldA, Vector2 worldB, bool major)
    {
        var color = major ? EditorTheme.GridMajor : EditorTheme.GridMinor;
        var thickness = projection.ToScreenSize(major ? MajorPixelThickness : MinorPixelThickness);
        LineMeshGenerator.AddLine(vertices, indices,
            projection.ToScreen(worldA), projection.ToScreen(worldB), thickness, color, ref offset);
    }

    /// <summary>The visible world-space AABB — the axis-aligned bounds of the camera's four projected
    /// virtual-screen corners (rotation-safe: an off-axis view still yields a covering AABB, and lines
    /// outside the true viewport are clipped by <see cref="OverlayMeshClip"/>).</summary>
    private (float Left, float Top, float Right, float Bottom) VisibleWorldAabb()
    {
        var c0 = _camera.VirtualScreenToWorld(new Vector2(0f, 0f));
        var c1 = _camera.VirtualScreenToWorld(new Vector2(_camera.VirtualWidth, 0f));
        var c2 = _camera.VirtualScreenToWorld(new Vector2(_camera.VirtualWidth, _camera.VirtualHeight));
        var c3 = _camera.VirtualScreenToWorld(new Vector2(0f, _camera.VirtualHeight));
        var left = MathF.Min(MathF.Min(c0.X, c1.X), MathF.Min(c2.X, c3.X));
        var right = MathF.Max(MathF.Max(c0.X, c1.X), MathF.Max(c2.X, c3.X));
        var top = MathF.Min(MathF.Min(c0.Y, c1.Y), MathF.Min(c2.Y, c3.Y));
        var bottom = MathF.Max(MathF.Max(c0.Y, c1.Y), MathF.Max(c2.Y, c3.Y));
        return (left, top, right, bottom);
    }

    private static void Park(ref DrawComponent draw)
    {
        draw.Vertices = Array.Empty<VertexPositionColor>();
        draw.Indices = Array.Empty<int>();
    }
}
