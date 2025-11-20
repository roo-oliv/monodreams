using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Extensions.Monogame;

namespace MonoDreams.Examples.System.Debug;

/// <summary>
/// Debug system that renders collision box outlines for entities with BoxCollider components.
/// Shows bright green outlines for passive colliders and bright red outlines for active colliders.
/// Only adds debug outlines when a BoxCollider is set on an entity.
/// </summary>
public static class ColliderDebugSystem
{
    private const float DEBUG_LINE_DEPTH = 1f; // Render on top of all elements
    private static Texture2D? _pixelTexture;

    public static void Register(World world, GraphicsDevice graphicsDevice)
    {
        // Create a 2x1 white and transparent pixels texture for drawing lines
        _pixelTexture = new Texture2D(graphicsDevice, 2, 1);
        _pixelTexture.SetData([Color.White, Color.Transparent]);

        // Create an observer that triggers when BoxCollider is added to an entity
        world.Observer<BoxCollider, Position>()
            .Event(Ecs.OnSet)
            .Each((Entity entity, ref BoxCollider boxCollider, ref Position position) =>
            {
                AddDebugLinesToEntity(world, entity, ref boxCollider, ref position);
            });
    }

    private static void AddDebugLinesToEntity(World world, Entity entity, ref BoxCollider boxCollider, ref Position position)
    {
        if (_pixelTexture == null) return;

        // Use bright, high-value colors with transparency
        Color lineColor;
        if (boxCollider.Enabled)
        {
            lineColor = boxCollider.Passive ? new Color(0, 255, 0, 180) : new Color(255, 0, 0, 180); // Bright green for passive, bright red for active
        }
        else
        {
            lineColor = new Color(128, 128, 128, 100); // Gray for disabled
        }

        var bounds = boxCollider.Bounds;
        var size = bounds.Dimension();
        const int lineThickness = 2;

        // Top edge
        world.Entity()
            .Set(position)
            .Set(new SpriteInfo
            {
                SpriteSheet = _pixelTexture,
                Source = new Rectangle(0, 0, 1, 1),
                Size = new Vector2(size.X, lineThickness),
                Color = lineColor,
                Target = RenderTargetID.Main,
                LayerDepth = DEBUG_LINE_DEPTH,
                Offset = new Vector2(bounds.X, bounds.Y)
            })
            .Set(new DrawComponent());

        // Bottom edge
        world.Entity()
            .Set(position)
            .Set(new SpriteInfo
            {
                SpriteSheet = _pixelTexture,
                Source = new Rectangle(0, 0, 1, 1),
                Size = new Vector2(size.X, lineThickness),
                Color = lineColor,
                Target = RenderTargetID.Main,
                LayerDepth = DEBUG_LINE_DEPTH,
                Offset = new Vector2(bounds.X, bounds.Y + bounds.Height - lineThickness)
            })
            .Set(new DrawComponent());

        // Left edge
        world.Entity()
            .Set(position)
            .Set(new SpriteInfo
            {
                SpriteSheet = _pixelTexture,
                Source = new Rectangle(0, 0, 1, 1),
                Size = new Vector2(lineThickness, size.Y),
                Color = lineColor,
                Target = RenderTargetID.Main,
                LayerDepth = DEBUG_LINE_DEPTH,
                Offset = new Vector2(bounds.X, bounds.Y)
            })
            .Set(new DrawComponent());

        // Right edge
        world.Entity()
            .Set(position)
            .Set(new SpriteInfo
            {
                SpriteSheet = _pixelTexture,
                Source = new Rectangle(0, 0, 1, 1),
                Size = new Vector2(lineThickness, size.Y),
                Color = lineColor,
                Target = RenderTargetID.Main,
                LayerDepth = DEBUG_LINE_DEPTH,
                Offset = new Vector2(bounds.X + bounds.Width - lineThickness, bounds.Y)
            })
            .Set(new DrawComponent());
    }

    public static void Cleanup()
    {
        _pixelTexture?.Dispose();
        _pixelTexture = null;
    }
}
