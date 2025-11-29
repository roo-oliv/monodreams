using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.Component.UI;
using MonoDreams.State;

namespace MonoDreams.Examples.System.UI;

/// <summary>
/// Prepares DrawComponents with mesh data for button outlines.
/// This system is game-agnostic and generates generic vertex buffer data.
/// </summary>
[With(typeof(SimpleButton), typeof(Transform))]
public class ButtonMeshPrepSystem(World world) : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref readonly var outline = ref entity.Get<SimpleButton>();
        ref readonly var transform = ref entity.Get<Transform>();

        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();
        var indexOffset = 0;

        // Calculate corners of the button using world position
        var halfWidth = outline.Size.X / 2;
        var halfHeight = outline.Size.Y / 2;
        var position = transform.WorldPosition;

        // Top-left, top-right, bottom-right, bottom-left corners
        var topLeft = new Vector2(position.X - halfWidth, position.Y - halfHeight);
        var topRight = new Vector2(position.X + halfWidth, position.Y - halfHeight);
        var bottomRight = new Vector2(position.X + halfWidth, position.Y + halfHeight);
        var bottomLeft = new Vector2(position.X - halfWidth, position.Y + halfHeight);

        // Create thick lines for the four sides of the rectangle
        AddThickLine(vertices, indices, topLeft, topRight, outline.LineThickness, outline.Color, ref indexOffset);
        AddThickLine(vertices, indices, topRight, bottomRight, outline.LineThickness, outline.Color, ref indexOffset);
        AddThickLine(vertices, indices, bottomRight, bottomLeft, outline.LineThickness, outline.Color, ref indexOffset);
        AddThickLine(vertices, indices, bottomLeft, topLeft, outline.LineThickness, outline.Color, ref indexOffset);

        // Set or update the DrawComponent
        if (!entity.Has<DrawComponent>())
        {
            entity.Set(new DrawComponent
            {
                Type = DrawElementType.Mesh,
                Vertices = vertices.ToArray(),
                Indices = indices.ToArray(),
                PrimitiveType = PrimitiveType.TriangleList,
                Target = outline.Target,
                LayerDepth = 0.95f
            });
        }
        else
        {
            ref var drawComponent = ref entity.Get<DrawComponent>();
            drawComponent.Type = DrawElementType.Mesh;
            drawComponent.Vertices = vertices.ToArray();
            drawComponent.Indices = indices.ToArray();
            drawComponent.PrimitiveType = PrimitiveType.TriangleList;
            drawComponent.Target = outline.Target;
        }
    }

    private void AddThickLine(List<VertexPositionColor> vertices, List<int> indices,
        Vector2 start, Vector2 end, float thickness, Color color, ref int indexOffset)
    {
        // Calculate direction and perpendicular vector for thickness
        Vector2 direction = end - start;
        Vector2 perpendicular = new Vector2(-direction.Y, direction.X);
        perpendicular.Normalize();
        perpendicular *= thickness / 2;

        // Create the four corners of the line segment
        vertices.Add(new VertexPositionColor(new Vector3(start + perpendicular, 0), color));
        vertices.Add(new VertexPositionColor(new Vector3(start - perpendicular, 0), color));
        vertices.Add(new VertexPositionColor(new Vector3(end - perpendicular, 0), color));
        vertices.Add(new VertexPositionColor(new Vector3(end + perpendicular, 0), color));

        // Create two triangles to form the line
        indices.Add(indexOffset);
        indices.Add(indexOffset + 1);
        indices.Add(indexOffset + 2);

        indices.Add(indexOffset);
        indices.Add(indexOffset + 2);
        indices.Add(indexOffset + 3);

        indexOffset += 4;
    }
}
