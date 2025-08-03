using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;
using MonoGame.SplineFlower.Spline.Types;
using System;

namespace MonoDreams.Examples.System.Draw;

[With(typeof(HermiteSpline), typeof(VelocityProfileComponent))]
public class OvertakingOpportunityRenderSystem(World world) : AEntitySetSystem<GameState>(world)
{
    private const float CircleRadius = 2f;         // Size of the overtaking opportunity indicators (smaller dot)
    private static readonly Color CircleColor = new(203, 30, 75);

    protected override void Update(GameState state, in Entity entity)
    {
        ref readonly var spline = ref entity.Get<HermiteSpline>();
        ref readonly var velocityProfile = ref entity.Get<VelocityProfileComponent>();

        if (velocityProfile.OvertakingOpportunities.Count == 0)
        {
            ClearVisualizations(entity);
            return;
        }

        // Generate visualization for overtaking opportunities
        GenerateOvertakingOpportunityVisualization(entity, spline, velocityProfile);
    }

    private void GenerateOvertakingOpportunityVisualization(Entity entity, HermiteSpline spline, VelocityProfileComponent velocityProfile)
    {
        // Create meshes for opportunity circles
        var circleVertices = new List<VertexPositionColor>();
        var circleIndices = new List<int>();

        var vertexIndex = 0;

        // Process each overtaking opportunity
        foreach (var opportunity in velocityProfile.OvertakingOpportunities)
        {
            // Get the position on the spline at the end percentage (braking point)
            var endPoint = spline.GetPoint(opportunity.EndPercentage * spline.MaxProgress());

            // Add a circle at the end point (braking point) of the overtaking opportunity
            AddCircle(circleVertices, circleIndices, endPoint, CircleRadius, CircleColor, ref vertexIndex);
        }

        // Set or update the overtaking opportunities mesh component
        SetMeshComponent(entity, circleVertices.ToArray(), circleIndices.ToArray(), PrimitiveType.TriangleList);
    }

    private void AddCircle(List<VertexPositionColor> vertices, List<int> indices, Vector2 center, float radius, Color color, ref int indexOffset)
    {
        // Number of segments to create a smooth circle
        const int segments = 16;

        // Add center vertex for filled circle
        vertices.Add(new VertexPositionColor(new Vector3(center, 0), color));
        int centerIndex = indexOffset++;

        // Create filled circle by constructing triangles from center to perimeter
        for (int i = 0; i < segments; i++)
        {
            // Calculate the two points on the perimeter
            float angle1 = 2 * MathF.PI * i / segments;
            float angle2 = 2 * MathF.PI * ((i + 1) % segments) / segments;

            Vector2 point1 = new(
                center.X + radius * MathF.Cos(angle1),
                center.Y + radius * MathF.Sin(angle1));

            Vector2 point2 = new(
                center.X + radius * MathF.Cos(angle2),
                center.Y + radius * MathF.Sin(angle2));

            // Add the perimeter vertices
            vertices.Add(new VertexPositionColor(new Vector3(point1, 0), color));
            vertices.Add(new VertexPositionColor(new Vector3(point2, 0), color));

            // Create a triangle: center -> point1 -> point2
            indices.Add(centerIndex);
            indices.Add(indexOffset);
            indices.Add(indexOffset + 1);

            indexOffset += 2;
        }
    }


    private void SetMeshComponent(Entity entity, VertexPositionColor[] vertices, int[] indices, PrimitiveType primitiveType)
    {
        if (!entity.Has<OvertakingOpportunityDrawComponent>())
        {
            entity.Set(new OvertakingOpportunityDrawComponent
            {
                Type = DrawElementType.Triangles,
                Vertices = vertices,
                Indices = indices,
                PrimitiveType = primitiveType,
                Target = RenderTargetID.Main,
                LayerDepth = 0.95f // Slightly below control points but above the track
            });
        }
        else
        {
            ref var drawComponent = ref entity.Get<OvertakingOpportunityDrawComponent>();
            drawComponent.Vertices = vertices;
            drawComponent.Indices = indices;
        }
    }

    private void ClearVisualizations(in Entity entity)
    {
        if (!entity.Has<OvertakingOpportunityDrawComponent>())
        {
            entity.Remove<OvertakingOpportunityDrawComponent>();
        }
    }
}
