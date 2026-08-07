#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Level;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.LevelEditor.UI;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Tile;

/// <summary>
/// The paint view — the LDtk-IntGrid-style "tiled colored blocks" overlay: every painted cell of
/// every <see cref="TileGridComponent"/> renders as a translucent quad in its value's color, plus a
/// stronger highlight on the cell under the cursor while the paint tool is armed. Composition
/// infrastructure like <see cref="Composition.EditorGrid"/>: ONE mesh entity, screen-baked each
/// frame from the draw-phase overlay pass through <see cref="OverlayProjection"/> (native
/// <c>RenderTargetID.Editor</c>, identity <c>WorldMatrix</c>, no <c>VisibleComponent</c>), clipped
/// to the game viewport. Visible only in Edit and while the injected gate is true (the Paint shelf
/// tab is active, or the paint tool is armed) — toggling to the Assets view hides the logical
/// blocks and shows the world as the player sees it.
/// </summary>
public sealed class TileGridOverlay
{
    /// <summary>The fill alpha of a painted cell's quad (premultiplied at bake).</summary>
    private const float CellAlpha = 0.42f;

    /// <summary>The fill alpha of the cursor cell highlight.</summary>
    private const float CursorAlpha = 0.72f;

    /// <summary>Cell-quad cap per frame — beyond it the overlay draws the visible subset only
    /// (a pathological zoom-out never builds an unbounded mesh).</summary>
    private const int MaxQuads = 6000;

    private readonly World _world;
    private readonly Camera _camera;
    private readonly ViewportManager? _viewportManager;
    private readonly Func<bool> _visible;
    private readonly Func<(bool Armed, byte Value)> _armed;
    private readonly EntitySet _grids;
    private readonly EntitySet _cursorSet;
    private readonly Entity _mesh;

    public TileGridOverlay(World world, Camera camera, ViewportManager? viewportManager,
        Func<bool> visible, Func<(bool Armed, byte Value)> armed)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _viewportManager = viewportManager;
        _visible = visible ?? throw new ArgumentNullException(nameof(visible));
        _armed = armed ?? throw new ArgumentNullException(nameof(armed));
        _grids = world.GetEntities().With<TileGridComponent>().With<TransformComponent>().AsSet();
        _cursorSet = world.GetEntities().With<CursorInputComponent>().AsSet();

        _mesh = world.CreateEntity();
        _mesh.Set(new EditorInfrastructureComponent());
        _mesh.Set(new TransformComponent());
        _mesh.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Editor,
            LayerDepth = EditorTheme.Depths.Grid + 0.01f, // just above the reference grid, under gizmos
            WorldMatrix = Matrix.Identity,
            Vertices = Array.Empty<VertexPositionColor>(),
            Indices = Array.Empty<int>(),
        });
    }

    /// <summary>Bakes (or hides) the paint view for this frame.</summary>
    public void EmitCells(GameState state)
    {
        if (!_mesh.IsAlive) return;
        ref var draw = ref _mesh.Get<DrawComponent>();

        if (state.RunMode != RunMode.Edit || !_visible())
        {
            Park(ref draw);
            return;
        }

        var projection = OverlayProjection.For(RenderTargetID.Main, _camera, _viewportManager);
        var (left, top, right, bottom) = VisibleWorldAabb();
        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();
        var offset = 0;
        var quads = 0;

        foreach (var grid in _grids.GetEntities())
        {
            var data = grid.Get<TileGridComponent>();
            var anchor = grid.Get<TransformComponent>().WorldPosition;
            var cell = Math.Max(1f, data.CellSize);

            foreach (var kv in data.Cells)
            {
                if (quads >= MaxQuads) break;
                var value = data.FindValue(kv.Value);
                if (value == null) continue;

                var (x, y) = TileGridComponent.Unpack(kv.Key);
                var worldTopLeft = anchor + new Vector2(x * cell, y * cell);
                if (worldTopLeft.X > right || worldTopLeft.Y > bottom ||
                    worldTopLeft.X + cell < left || worldTopLeft.Y + cell < top) continue;

                AddQuad(vertices, indices, ref offset, projection, worldTopLeft, cell,
                    Premultiply(value.Color, CellAlpha));
                quads++;
            }
        }

        // Cursor cell highlight while the paint tool is armed (the brush preview).
        var (armed, armedValue) = _armed();
        if (armed)
        {
            foreach (var cursor in _cursorSet.GetEntities())
            {
                ref readonly var input = ref cursor.Get<CursorInputComponent>();
                if (input.OutsideViewport) break;
                foreach (var grid in _grids.GetEntities())
                {
                    var data = grid.Get<TileGridComponent>();
                    var anchor = grid.Get<TransformComponent>().WorldPosition;
                    var cell = Math.Max(1f, data.CellSize);
                    var (cx, cy) = data.CellAt(input.WorldPosition, anchor);
                    var color = armedValue != 0 && data.FindValue(armedValue) is { } v
                        ? Premultiply(v.Color, CursorAlpha)
                        : Premultiply(EditorTheme.Warning, CursorAlpha); // eraser reads warning-tinted
                    AddQuad(vertices, indices, ref offset, projection,
                        anchor + new Vector2(cx * cell, cy * cell), cell, color);
                    break; // one grid drives the brush preview
                }
                break; // single cursor
            }
        }

        if (offset == 0)
        {
            Park(ref draw);
            return;
        }

        var mesh = OverlayMeshClip.ClipToRect(
            new MeshData(vertices.ToArray(), indices.ToArray()), projection.Viewport);
        draw.Type = DrawElementType.Mesh;
        draw.Vertices = mesh.Vertices;
        draw.Indices = mesh.Indices;
        draw.PrimitiveType = mesh.PrimitiveType;
        draw.WorldMatrix = Matrix.Identity;
        draw.Target = RenderTargetID.Editor;
    }

    private static void AddQuad(List<VertexPositionColor> vertices, List<int> indices, ref int offset,
        in OverlayProjection projection, Vector2 worldTopLeft, float cell, Color color)
    {
        var a = projection.ToScreen(worldTopLeft);
        var b = projection.ToScreen(worldTopLeft + new Vector2(cell, 0));
        var c = projection.ToScreen(worldTopLeft + new Vector2(cell, cell));
        var d = projection.ToScreen(worldTopLeft + new Vector2(0, cell));
        vertices.Add(new VertexPositionColor(new Vector3(a, 0f), color));
        vertices.Add(new VertexPositionColor(new Vector3(b, 0f), color));
        vertices.Add(new VertexPositionColor(new Vector3(c, 0f), color));
        vertices.Add(new VertexPositionColor(new Vector3(d, 0f), color));
        indices.Add(offset); indices.Add(offset + 1); indices.Add(offset + 2);
        indices.Add(offset); indices.Add(offset + 2); indices.Add(offset + 3);
        offset += 4;
    }

    /// <summary>Straight-alpha value color → premultiplied overlay color (the mesh path blends
    /// premultiplied; a straight-alpha translucent renders near-additive).</summary>
    private static Color Premultiply(Color color, float alpha) => new(
        (byte)(color.R * alpha), (byte)(color.G * alpha), (byte)(color.B * alpha), (byte)(255 * alpha));

    private (float Left, float Top, float Right, float Bottom) VisibleWorldAabb()
    {
        var c0 = _camera.VirtualScreenToWorld(new Vector2(0f, 0f));
        var c1 = _camera.VirtualScreenToWorld(new Vector2(_camera.VirtualWidth, 0f));
        var c2 = _camera.VirtualScreenToWorld(new Vector2(_camera.VirtualWidth, _camera.VirtualHeight));
        var c3 = _camera.VirtualScreenToWorld(new Vector2(0f, _camera.VirtualHeight));
        return (
            MathF.Min(MathF.Min(c0.X, c1.X), MathF.Min(c2.X, c3.X)),
            MathF.Min(MathF.Min(c0.Y, c1.Y), MathF.Min(c2.Y, c3.Y)),
            MathF.Max(MathF.Max(c0.X, c1.X), MathF.Max(c2.X, c3.X)),
            MathF.Max(MathF.Max(c0.Y, c1.Y), MathF.Max(c2.Y, c3.Y)));
    }

    private static void Park(ref DrawComponent draw)
    {
        draw.Vertices = Array.Empty<VertexPositionColor>();
        draw.Indices = Array.Empty<int>();
    }

    public void Dispose()
    {
        _grids.Dispose();
        _cursorSet.Dispose();
    }
}
