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

[With(typeof(CatMulRomSpline), typeof(VelocityProfileComponent))]
public class PinPointRenderSystem(World world) : AEntitySetSystem<GameState>(world)
{
    private const float MainCircleRadius = 1.5f;      // Size of the main circle indicator
    private const float EndCircleRadius = 6f;       // Size of the circle at the end of perpendicular line
    private const float LineLength = 16f;            // Length of the perpendicular line
    private const float LineThickness = 1f;         // Thickness of the perpendicular line
    private static readonly Color OvertakingColor = new(203, 30, 75);
    private static readonly Color MaxSpeedColor = new(255, 201, 7);
    private static readonly Color SpeedSymbolColor = Color.White;  // Color for the speed symbols

    protected override void Update(GameState state, in Entity entity)
    {
        ref readonly var spline = ref entity.Get<CatMulRomSpline>();
        ref readonly var velocityProfile = ref entity.Get<VelocityProfileComponent>();

        // Check if we have anything to visualize
        bool hasOvertakingOpportunities = velocityProfile.OvertakingOpportunities.Count > 0;
        bool hasMaxSpeed = velocityProfile.MaxSpeed > 0;

        if (!hasOvertakingOpportunities && !hasMaxSpeed)
        {
            ClearVisualizations(entity);
            return;
        }

        // Generate visualizations
        GeneratePinPointVisualizations(entity, spline, velocityProfile);
    }

    private void GeneratePinPointVisualizations(Entity entity, CatMulRomSpline spline, VelocityProfileComponent velocityProfile)
    {
        // Create meshes for all pin points
        var circleVertices = new List<VertexPositionColor>();
        var circleIndices = new List<int>();

        var vertexIndex = 0;

        // Process each overtaking opportunity
        foreach (var opportunity in velocityProfile.OvertakingOpportunities)
        {
            // Get the position on the spline at the end percentage (braking point)
            var endProgress = opportunity.EndPercentage * spline.MaxProgress();
            var endPoint = spline.GetPoint(endProgress);

            // Get the direction of the spline at the end point
            var direction = spline.GetDirection(endProgress);

            // Calculate perpendicular vector (180 degrees to the spline direction)
            // Make sure it's always consistent in direction (-90 degrees, not 90)
            var perpendicular = new Vector2(direction.Y, -direction.X);
            perpendicular.Normalize();

            // Calculate the end point of the perpendicular line
            var perpendicularEndPoint = endPoint + perpendicular * LineLength;

            // Add a circle at the end point (braking point) of the overtaking opportunity
            AddCircle(circleVertices, circleIndices, endPoint, MainCircleRadius, OvertakingColor, ref vertexIndex);

            // Add a perpendicular line
            AddLine(circleVertices, circleIndices, endPoint, perpendicularEndPoint, LineThickness, OvertakingColor, ref vertexIndex);

            // Add a larger circle at the end of the perpendicular line
            AddCircle(circleVertices, circleIndices, perpendicularEndPoint, EndCircleRadius, OvertakingColor, ref vertexIndex);
        }

        // Find the point of maximum speed on the track if available
        if (velocityProfile.StatsCalculated && velocityProfile.MaxSpeed > 0 && velocityProfile.VelocityProfile.Length > 0)
        {
            // Find the index of the maximum speed in the velocity profile
            int maxSpeedIndex = Array.IndexOf(velocityProfile.VelocityProfile, velocityProfile.MaxSpeed);

            if (maxSpeedIndex >= 0 && maxSpeedIndex < velocityProfile.VelocityProfile.Length)
            {
                // Calculate the progress along the spline
                float maxSpeedProgress = (float)maxSpeedIndex / velocityProfile.VelocityProfile.Length * spline.MaxProgress();
                var maxSpeedPoint = spline.GetPoint(maxSpeedProgress);

                // Get the direction at the max speed point
                var maxSpeedDirection = spline.GetDirection(maxSpeedProgress);

                // Calculate perpendicular vector for the max speed indicator
                var maxSpeedPerpendicular = new Vector2(maxSpeedDirection.Y, -maxSpeedDirection.X);
                maxSpeedPerpendicular.Normalize();

                // Calculate the end point of the perpendicular line for max speed
                var maxSpeedEndPoint = maxSpeedPoint + maxSpeedPerpendicular * LineLength;

                // Add a circle at the max speed point
                AddCircle(circleVertices, circleIndices, maxSpeedPoint, MainCircleRadius, MaxSpeedColor, ref vertexIndex);

                // Add a perpendicular line for max speed
                AddLine(circleVertices, circleIndices, maxSpeedPoint, maxSpeedEndPoint, LineThickness, MaxSpeedColor, ref vertexIndex);

                // Add a larger circle at the end of the perpendicular line for max speed
                AddCircle(circleVertices, circleIndices, maxSpeedEndPoint, EndCircleRadius, MaxSpeedColor, ref vertexIndex);

                // Add the speed symbols (two '>' shapes) on top of the end circle
                // AddSpeedSymbols(circleVertices, circleIndices, maxSpeedEndPoint, EndCircleRadius * 0.7f, SpeedSymbolColor, ref vertexIndex);
            }
        }

        // Set or update the pin points mesh component
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

    private void AddLine(List<VertexPositionColor> vertices, List<int> indices, Vector2 start, Vector2 end, float thickness, Color color, ref int indexOffset)
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

    private void AddSpeedSymbols(List<VertexPositionColor> vertices, List<int> indices, Vector2 center, float size, Color color, ref int indexOffset)
    {
        // Parameters for the symbols
        float symbolWidth = size * 1.2f;
        float symbolHeight = size * 1.5f;
        float symbolThickness = size * 0.4f;
        float spacing = size * 0.9f; // Space between the two symbols

        // Calculate the position of the first symbol (left one)
        Vector2 leftSymbolCenter = new(center.X - spacing/2, center.Y);

        // Create first '>' symbol (left one)
        AddGreaterThanSymbol(vertices, indices, leftSymbolCenter, symbolWidth, symbolHeight, symbolThickness, color, ref indexOffset);

        // Calculate the position of the second symbol (right one)
        Vector2 rightSymbolCenter = new(center.X + spacing/2, center.Y);

        // Create second '>' symbol (right one)
        AddGreaterThanSymbol(vertices, indices, rightSymbolCenter, symbolWidth, symbolHeight, symbolThickness, color, ref indexOffset);
    }

    private void AddGreaterThanSymbol(List<VertexPositionColor> vertices, List<int> indices, 
        Vector2 center, float width, float height, float thickness, Color color, ref int indexOffset)
    {
        // Calculate points for the greater than symbol
        // The symbol consists of two line segments that form a '>' shape

        // Calculate points for the symbol
        Vector2 leftPoint = new(center.X - width/2, center.Y);
        Vector2 rightPoint = new(center.X + width/2, center.Y);
        Vector2 topPoint = new(center.X, center.Y - height/2);
        Vector2 bottomPoint = new(center.X, center.Y + height/2);

        // Add top line (left point to top point)
        AddLine(vertices, indices, rightPoint, topPoint, thickness, color, ref indexOffset);

        // Add bottom line (left point to bottom point)
        AddLine(vertices, indices, rightPoint, bottomPoint, thickness, color, ref indexOffset);
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
