using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.UI;

/// <summary>
/// Prepares DrawComponents with mesh data for button outlines.
/// This system is game-agnostic and generates generic vertex buffer data.
/// </summary>
[With(typeof(SimpleButtonComponent), typeof(TransformComponent))]
public class ButtonMeshPrepSystem(World world) : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref readonly var outline = ref entity.Get<SimpleButtonComponent>();
        ref readonly var transform = ref entity.Get<TransformComponent>();

        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();
        var indexOffset = 0;

        // Calculate corners of the button using world position as top-left origin
        // (Layout system positions elements at their top-left corner)
        var position = transform.WorldPosition;

        // Top-left, top-right, bottom-right, bottom-left corners
        var topLeft = position;
        var topRight = new Vector2(position.X + outline.Size.X, position.Y);
        var bottomRight = position + outline.Size;
        var bottomLeft = new Vector2(position.X, position.Y + outline.Size.Y);

        // Optional solid fill behind the outline.
        if (outline.FillColor.A > 0)
        {
            var fill = outline.FillColor;
            vertices.Add(new VertexPositionColor(new Vector3(topLeft, 0), fill));
            vertices.Add(new VertexPositionColor(new Vector3(topRight, 0), fill));
            vertices.Add(new VertexPositionColor(new Vector3(bottomRight, 0), fill));
            vertices.Add(new VertexPositionColor(new Vector3(bottomLeft, 0), fill));
            indices.Add(indexOffset);
            indices.Add(indexOffset + 1);
            indices.Add(indexOffset + 2);
            indices.Add(indexOffset);
            indices.Add(indexOffset + 2);
            indices.Add(indexOffset + 3);
            indexOffset += 4;
        }

        // Create thick lines for the four sides of the rectangle (skip if thickness is zero).
        if (outline.LineThickness > 0f)
        {
            AddThickLine(vertices, indices, topLeft, topRight, outline.LineThickness, outline.Color, ref indexOffset);
            AddThickLine(vertices, indices, topRight, bottomRight, outline.LineThickness, outline.Color, ref indexOffset);
            AddThickLine(vertices, indices, bottomRight, bottomLeft, outline.LineThickness, outline.Color, ref indexOffset);
            AddThickLine(vertices, indices, bottomLeft, topLeft, outline.LineThickness, outline.Color, ref indexOffset);
        }

        // Set or update the DrawComponent.
        // Vertices above are baked in world coordinates, so WorldMatrix must be
        // identity (or null). When the screen also runs MeshPrepSystem (which would
        // otherwise write transform.WorldMatrix here) place this system AFTER
        // MeshPrepSystem in the draw pipeline so this assignment wins.
        if (!entity.Has<DrawComponent>())
        {
            entity.Set(new DrawComponent
            {
                Type = DrawElementType.Mesh,
                Vertices = vertices.ToArray(),
                Indices = indices.ToArray(),
                PrimitiveType = PrimitiveType.TriangleList,
                Target = outline.Target,
                LayerDepth = 0.95f,
                WorldMatrix = Matrix.Identity,
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
            drawComponent.WorldMatrix = Matrix.Identity;
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
