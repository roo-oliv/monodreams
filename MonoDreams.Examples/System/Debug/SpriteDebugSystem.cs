using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Debug;

/// <summary>
/// Debug visualization system for sprite rendering.
/// Renders semi-transparent rectangles showing sprite boundaries and yellow circles at origin points.
/// </summary>
public class SpriteDebugSystem : ISystem<GameState>
{
    /// <summary>
    /// Global toggle for sprite debug visualization.
    /// </summary>
    public static bool Enabled = true;

    private const float DebugLayerDepth = 0.98f;
    private const float OriginCircleRadius = 0.2f;
    private const int CircleSegments = 12;
    private const float LineThickness = 0.1f;

    private readonly World _world;
    private readonly EntitySet _spriteEntities;
    private readonly RenderTargetID _renderTarget;

    // Track debug entities for cleanup
    private readonly List<Entity> _debugEntities = [];

    // Colors
    private static readonly Color BoundsColor = new(0, 0, 0, 50);
    private static readonly Color OriginColor = Color.Orange;

    public SpriteDebugSystem(World world, RenderTargetID renderTarget = RenderTargetID.Main)
    {
        _world = world;
        _renderTarget = renderTarget;

        _spriteEntities = world.GetEntities()
            .With<DrawComponent>()
            .With<SpriteInfo>()
            .With<Transform>()
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

        // Create debug visuals for each sprite entity
        foreach (var entity in _spriteEntities.GetEntities())
        {
            ref readonly var drawComponent = ref entity.Get<DrawComponent>();

            // Only visualize sprite types
            if (drawComponent.Type != DrawElementType.Sprite) continue;
            if (drawComponent.Size == Vector2.Zero) continue;

            // Get the position and origin from DrawComponent
            var position = drawComponent.Position;
            var origin = drawComponent.Origin;
            var size = drawComponent.Size;

            // Calculate the top-left corner of the sprite bounds
            // Origin is in SOURCE texture coordinates, so we need to scale it to destination size
            // scale = destinationSize / sourceSize
            Vector2 scaledOrigin;
            if (drawComponent.SourceRectangle.HasValue &&
                drawComponent.SourceRectangle.Value.Width > 0 &&
                drawComponent.SourceRectangle.Value.Height > 0)
            {
                scaledOrigin = new Vector2(
                    origin.X * size.X / drawComponent.SourceRectangle.Value.Width,
                    origin.Y * size.Y / drawComponent.SourceRectangle.Value.Height);
            }
            else
            {
                scaledOrigin = origin;
            }

            // When rendering, SpriteBatch places the scaled origin at position
            // So bounds top-left = position - scaledOrigin
            var boundsTopLeft = position - scaledOrigin;

            // Create bounds rectangle (semi-transparent black)
            CreateFilledRectEntity(boundsTopLeft.X, boundsTopLeft.Y, size.X, size.Y, BoundsColor);

            // Create origin circle (yellow) at the position
            CreateCircleEntity(position.X, position.Y, OriginCircleRadius, OriginColor);

            // Create line from origin to center of bounds
            var boundsCenter = boundsTopLeft + size / 2;
            CreateLineEntity(position, boundsCenter, OriginColor);
        }
    }

    private void CreateFilledRectEntity(float x, float y, float width, float height, Color color)
    {
        var entity = _world.CreateEntity();
        _debugEntities.Add(entity);

        // Create a filled rectangle with 4 vertices and 2 triangles
        var vertices = new VertexPositionColor[]
        {
            new(new Vector3(x, y, 0), color),                         // Top-left
            new(new Vector3(x + width, y, 0), color),                 // Top-right
            new(new Vector3(x + width, y + height, 0), color),        // Bottom-right
            new(new Vector3(x, y + height, 0), color),                // Bottom-left
        };

        var indices = new int[]
        {
            0, 1, 2,  // First triangle
            0, 2, 3   // Second triangle
        };

        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Vertices = vertices,
            Indices = indices,
            PrimitiveType = PrimitiveType.TriangleList,
            Target = _renderTarget,
            LayerDepth = DebugLayerDepth
        });
    }

    private void CreateCircleEntity(float centerX, float centerY, float radius, Color color)
    {
        var entity = _world.CreateEntity();
        _debugEntities.Add(entity);

        // Create a filled circle using triangle fan
        var vertices = new List<VertexPositionColor>();
        var indices = new List<int>();

        // Center vertex
        vertices.Add(new VertexPositionColor(new Vector3(centerX, centerY, 0), color));

        // Perimeter vertices
        for (int i = 0; i <= CircleSegments; i++)
        {
            var angle = (float)(i * 2 * Math.PI / CircleSegments);
            var px = centerX + radius * MathF.Cos(angle);
            var py = centerY + radius * MathF.Sin(angle);
            vertices.Add(new VertexPositionColor(new Vector3(px, py, 0), color));
        }

        // Create triangles (triangle fan from center)
        for (int i = 1; i <= CircleSegments; i++)
        {
            indices.Add(0);       // Center
            indices.Add(i);       // Current perimeter vertex
            indices.Add(i + 1);   // Next perimeter vertex
        }

        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            PrimitiveType = PrimitiveType.TriangleList,
            Target = _renderTarget,
            LayerDepth = DebugLayerDepth + 0.001f  // Slightly in front of bounds
        });
    }

    private void CreateLineEntity(Vector2 start, Vector2 end, Color color)
    {
        var entity = _world.CreateEntity();
        _debugEntities.Add(entity);

        // Calculate perpendicular direction for line thickness
        var direction = end - start;
        if (direction.LengthSquared() < 0.0001f) return;

        var perpendicular = new Vector2(-direction.Y, direction.X);
        perpendicular.Normalize();
        perpendicular *= LineThickness / 2;

        // Create a quad (thick line) with 4 vertices
        var vertices = new VertexPositionColor[]
        {
            new(new Vector3(start + perpendicular, 0), color),
            new(new Vector3(start - perpendicular, 0), color),
            new(new Vector3(end - perpendicular, 0), color),
            new(new Vector3(end + perpendicular, 0), color),
        };

        var indices = new int[]
        {
            0, 1, 2,
            0, 2, 3
        };

        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Vertices = vertices,
            Indices = indices,
            PrimitiveType = PrimitiveType.TriangleList,
            Target = _renderTarget,
            LayerDepth = DebugLayerDepth + 0.0005f  // Between bounds and circle
        });
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
        _spriteEntities.Dispose();
    }
}
