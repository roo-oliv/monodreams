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
    protected override void Update(GameState state, in Entity entity)
    {
        ref readonly var spline = ref entity.Get<HermiteSpline>();
        ref readonly var velocityProfile = ref entity.Get<VelocityProfileComponent>();
        
        // Generate triangle mesh for the track
        GenerateTrackMesh(entity, spline, velocityProfile.VelocityProfile);
    }

    private void GenerateTrackMesh(Entity entity, HermiteSpline spline, float[] velocityProfile)
    {
        const int segments = 1000; // Number of segments along the spline
        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();
        
        var maxVelocity = 2000f;
        var minVelocity = 200f;
        var velocityRange = maxVelocity - minVelocity;
        
        // Generate vertices along the spline
        for (int i = 0; i <= segments; i++)
        {
            var t = (float)i / segments;
            var progress = t * spline.MaxProgress();
            
            // Get position and direction at this point
            var position = spline.GetPoint(progress);
            var direction = spline.GetDirection(progress);
            
            // Calculate perpendicular vector for track width
            var perpendicular = new Vector2(-direction.Y, direction.X);
            perpendicular.Normalize();
            
            // Calculate color based on velocity
            var velocityIndex = Math.Min(i, velocityProfile.Length - 1);
            var normalizedVelocity = velocityProfile[velocityIndex] / velocityRange;
            
            var color = Color.Lerp(Color.Blue, Color.Red, normalizedVelocity);
            
            // Create left and right edge vertices
            var leftPosition = position + perpendicular * (trackWidth * 0.5f);
            var rightPosition = position - perpendicular * (trackWidth * 0.5f);
            
            vertices.Add(new VertexPositionColor(new Vector3(leftPosition, 0), color));
            vertices.Add(new VertexPositionColor(new Vector3(rightPosition, 0), color));
        }
        
        // Generate triangle indices
        for (int i = 0; i < segments; i++)
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
        
        // Set or update the triangle mesh component
        if (entity.Has<TriangleMeshInfo>())
        {
            ref var meshInfo = ref entity.Get<TriangleMeshInfo>();
            meshInfo.Vertices = vertices.ToArray();
            meshInfo.Indices = indices.ToArray();
        }
        else
        {
            entity.Set(new TriangleMeshInfo
            {
                Vertices = vertices.ToArray(),
                Indices = indices.ToArray(),
                PrimitiveType = PrimitiveType.TriangleList,
                Target = RenderTargetID.Main,
                LayerDepth = 0.5f
            });
        }
    }
}
