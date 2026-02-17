#nullable enable
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component.Draw;
using MonoDreams.Examples.Component.Layout;
using MonoDreams.State;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Examples.System.Layout;

/// <summary>
/// Debug visualization system for auto layout.
/// Renders rectangle boundaries and node names for layout debugging.
/// </summary>
public class LayoutDebugSystem : ISystem<GameState>
{
    /// <summary>
    /// Global toggle for layout debug visualization.
    /// </summary>
    public static bool Enabled = false;

    private static readonly Color[] DepthColors =
    [
        new Color(100, 149, 237),  // Cornflower Blue - depth 0
        new Color(50, 205, 50),    // Lime Green - depth 1
        new Color(255, 165, 0),    // Orange - depth 2
        new Color(186, 85, 211),   // Medium Orchid - depth 3+
    ];

    private const float LineThickness = 1f;
    private const float LabelScale = 0.08f;
    private const float DebugLayerDepth = 0.99f;

    private readonly World _world;
    private readonly EntitySet _layoutEntities;
    private readonly BitmapFont _font;
    private readonly RenderTargetID _renderTarget;
    private readonly MonoDreams.Component.Camera _camera;

    // Track debug entities for cleanup
    private readonly List<Entity> _debugEntities = [];

    public LayoutDebugSystem(World world, BitmapFont font, MonoDreams.Component.Camera camera, RenderTargetID renderTarget = RenderTargetID.Main)
    {
        _world = world;
        _font = font;
        _camera = camera;
        _renderTarget = renderTarget;

        _layoutEntities = world.GetEntities()
            .With<LayoutSlot>()
            .With<MonoDreams.Component.Transform>()
            .AsSet();
    }

    public bool IsEnabled { get; set; } = true;

    public void Update(GameState state)
    {
        // Clean up old debug entities
        foreach (var entity in _debugEntities)
        {
            if (entity.IsAlive)
            {
                entity.Dispose();
            }
        }
        _debugEntities.Clear();

        if (!IsEnabled || !Enabled) return;

        // Create debug visuals for each layout node
        foreach (var entity in _layoutEntities.GetEntities())
        {
            ref readonly var slot = ref entity.Get<LayoutSlot>();
            ref readonly var transform = ref entity.Get<MonoDreams.Component.Transform>();

            // Calculate hierarchy depth for coloring
            var depth = CalculateDepth(slot.Node);
            var color = DepthColors[global::System.Math.Min(depth, DepthColors.Length - 1)];

            // Get computed layout bounds
            // Use Position (not WorldPosition) because layout containers don't have Origin offset
            // Position is the top-left corner set by AutoLayoutSystem
            var pos = transform.Position;
            var width = slot.ComputedWidth;
            var height = slot.ComputedHeight;

            // Create rectangle from top-left to bottom-right
            var x = pos.X;
            var y = pos.Y;

            // Create bounding rectangle entity
            CreateBoundsEntity(x, y, width, height, color);

            // Create label entity if node has a name
            if (!string.IsNullOrEmpty(slot.Node.Name))
            {
                CreateLabelEntity(slot.Node.Name, x, y, color);
            }
        }
    }

    private int CalculateDepth(MonoDreams.Examples.Layout.FlexLayoutNode node)
    {
        var depth = 0;
        var current = node.Parent;
        while (current != null)
        {
            depth++;
            current = current.Parent;
        }
        return depth;
    }

    private void CreateBoundsEntity(float x, float y, float width, float height, Color color)
    {
        var entity = _world.CreateEntity();
        _debugEntities.Add(entity);

        // Calculate corners
        var topLeft = new Vector2(x, y);
        var topRight = new Vector2(x + width, y);
        var bottomRight = new Vector2(x + width, y + height);
        var bottomLeft = new Vector2(x, y + height);

        // Build mesh for rectangle outline
        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();
        var indexOffset = 0;

        AddThickLine(vertices, indices, topLeft, topRight, LineThickness, color, ref indexOffset);
        AddThickLine(vertices, indices, topRight, bottomRight, LineThickness, color, ref indexOffset);
        AddThickLine(vertices, indices, bottomRight, bottomLeft, LineThickness, color, ref indexOffset);
        AddThickLine(vertices, indices, bottomLeft, topLeft, LineThickness, color, ref indexOffset);

        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            PrimitiveType = PrimitiveType.TriangleList,
            Target = _renderTarget,
            LayerDepth = DebugLayerDepth
        });
    }

    private void CreateLabelEntity(string name, float x, float y, Color color)
    {
        var entity = _world.CreateEntity();
        _debugEntities.Add(entity);

        // Position label at top-left corner, slightly offset
        var labelPos = new Vector2(x + 4, y + 2);

        entity.Set(new MonoDreams.Component.Transform(labelPos));
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Text,
            Font = _font,
            Text = name,
            Position = labelPos,
            Color = color,
            Scale = new Vector2(LabelScale),
            Target = _renderTarget,
            LayerDepth = DebugLayerDepth
        });
    }

    private void AddThickLine(List<VertexPositionColor> vertices, List<int> indices,
        Vector2 start, Vector2 end, float thickness, Color color, ref int indexOffset)
    {
        var direction = end - start;
        if (direction.LengthSquared() < 0.0001f) return;

        var perpendicular = new Vector2(-direction.Y, direction.X);
        perpendicular.Normalize();
        perpendicular *= thickness / 2;

        vertices.Add(new VertexPositionColor(new Vector3(start + perpendicular, 0), color));
        vertices.Add(new VertexPositionColor(new Vector3(start - perpendicular, 0), color));
        vertices.Add(new VertexPositionColor(new Vector3(end - perpendicular, 0), color));
        vertices.Add(new VertexPositionColor(new Vector3(end + perpendicular, 0), color));

        indices.Add(indexOffset);
        indices.Add(indexOffset + 1);
        indices.Add(indexOffset + 2);

        indices.Add(indexOffset);
        indices.Add(indexOffset + 2);
        indices.Add(indexOffset + 3);

        indexOffset += 4;
    }

    public void Dispose()
    {
        foreach (var entity in _debugEntities)
        {
            if (entity.IsAlive)
            {
                entity.Dispose();
            }
        }
        _debugEntities.Clear();
        _layoutEntities.Dispose();
    }
}
