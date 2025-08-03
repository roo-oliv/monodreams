using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;
using MonoGame.SplineFlower.Spline.Types;

namespace MonoDreams.Examples.System.Draw;

[With(typeof(HermiteSpline), typeof(VelocityProfileComponent))]
public class TrackMeshGenerationSystem(World world, float trackWidth = 20f) : AEntitySetSystem<GameState>(world)
{
    private static readonly Color BorderColor = new(35, 57, 114);
    
    protected override void Update(GameState state, in Entity entity)
    {
        ref readonly var spline = ref entity.Get<HermiteSpline>();
        ref readonly var velocityProfile = ref entity.Get<VelocityProfileComponent>();
        
        // First generate the black border track mesh
        GenerateTrackMesh(entity, spline, null, trackWidth + 2f, BorderColor, 0.41f);

        // Then generate the colored track mesh on top
        GenerateTrackMesh(entity, spline, velocityProfile.VelocityProfile, trackWidth, null, 0.5f);
    }

    private void GenerateTrackMesh(Entity entity, HermiteSpline spline, float[] velocityProfile, float width, Color? fixedColor = null, float layerDepth = 0.5f)
    {
        const int segments = 1000; // Number of segments along the spline
        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();

        var maxVelocity = 1000f;
        var minVelocity = 50f;
        var velocityRange = maxVelocity - minVelocity;

        // Generate vertices along the spline
        for (var i = 0; i <= segments; i++)
        {
            var t = (float)i / segments;
            var progress = t * spline.MaxProgress();

            // Get position and direction at this point
            var position = spline.GetPoint(progress);
            var direction = spline.GetDirection(progress);

            // Calculate perpendicular vector for track width
            var perpendicular = new Vector2(-direction.Y, direction.X);
            perpendicular.Normalize();

            // Determine the color to use
            Color color;

            if (fixedColor.HasValue)
            {
                // Use the provided fixed color (for the border)
                color = fixedColor.Value;
            }
            else if (velocityProfile != null)
            {
                // Calculate color based on velocity using three-color lerp
                var velocityIndex = Math.Min(i, velocityProfile.Length - 1);
                var normalizedVelocity = velocityProfile[velocityIndex] / velocityRange;

                if (normalizedVelocity <= 0.5f)
                {
                    // Lerp between Yellow and LightSeaGreen for the first half
                    var s = normalizedVelocity * 2f; // Scale 0-0.5 to 0-1
                    color = Color.Lerp(Color.Yellow, Color.LightSeaGreen, s);
                }
                else
                {
                    // Lerp between LightSeaGreen and Navy for the second half
                    var s = (normalizedVelocity - 0.5f) * 2f; // Scale 0.5-1 to 0-1
                    color = Color.Lerp(Color.LightSeaGreen, Color.Navy, s);
                }
            }
            else
            {
                // Default color if no velocity profile or fixed color is provided
                color = Color.White;
            }

            // Create left and right edge vertices using the provided width
            var leftPosition = position + perpendicular * (width * 0.5f);
            var rightPosition = position - perpendicular * (width * 0.5f);
            
            vertices.Add(new VertexPositionColor(new Vector3(leftPosition, 0), color));
            vertices.Add(new VertexPositionColor(new Vector3(rightPosition, 0), color));
        }
        
        // Generate triangle indices
        for (var i = 0; i < segments; i++)
        {
            var baseIndex = i * 2;
            
            // First triangle (left-right-left)
            indices.Add(baseIndex);     // Current left
            indices.Add(baseIndex + 1); // Current right
            indices.Add(baseIndex + 2); // Next left
            
            // Second triangle (current right-next right-next left)
            indices.Add(baseIndex + 1); // Current right
            indices.Add(baseIndex + 3); // Next right
            indices.Add(baseIndex + 2); // Next left
        }

        // Create a unique component for each mesh
        var triangleMeshInfo = new TriangleMeshInfo
        {
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            PrimitiveType = PrimitiveType.TriangleList,
            Target = RenderTargetID.Main,
            LayerDepth = layerDepth
        };

        // This way we can have both a border and colored track on the same entity
        if (fixedColor.HasValue)
        {
            entity.Get<Entity>().Set(triangleMeshInfo);
        }
        else
        {
            entity.Set(triangleMeshInfo);
        }
    }
}