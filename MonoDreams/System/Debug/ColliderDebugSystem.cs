using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Extensions.Monogame;
using MonoDreams.State;

namespace MonoDreams.System.Debug;

/// <summary>
/// Debug system that renders collision boxes for entities with BoxCollider components.
/// Shows green lines for passive colliders and red lines for active colliders.
/// Only adds debug lines when a BoxCollider is added to an entity.
/// </summary>
public sealed class ColliderDebugSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly Texture2D _pixelTexture;
    private const float DEBUG_LINE_DEPTH = 1f; // Render on top of all elements
    
    public bool IsEnabled { get; set; } = true;

    public ColliderDebugSystem(World world, GraphicsDevice graphicsDevice)
    {
        _world = world;
        // Create a 2x1 white and transparent pixels texture for drawing lines
        _pixelTexture = new Texture2D(graphicsDevice, 2, 1);
        _pixelTexture.SetData([Color.White, Color.Transparent]);


        _world.SubscribeEntityComponentAdded<BoxCollider>(AddDebugLinesToEntity);
    }

    private void AddDebugLinesToEntity(in Entity entity, in BoxCollider boxCollider)
    {
        ref readonly var transform = ref entity.Get<Transform>();
        Color lineColor;
        if (boxCollider.Enabled)
        {
            lineColor = boxCollider.Passive ? Color.Green : Color.Red;
        }
        else
        {
            lineColor = Color.Gray;
        }
        var r = new Rectangle(0, 0, 1, 1);
        var c = new Rectangle(1, 0, 1, 1);
        var spriteInfo = new SpriteInfo{
            SpriteSheet = _pixelTexture,
            Source = new Rectangle(0, 0, 1, 1),
            Size = boxCollider.Bounds.Dimension(),
            Color = lineColor,
            Target = RenderTargetID.Main,
            LayerDepth = DEBUG_LINE_DEPTH,
            NinePatchData = new NinePatchInfo(0, r, r, r, r, c, r, r, r, r),
            Offset = boxCollider.Bounds.Origin(),
        };

        var debugEntity = _world.CreateEntity();
        debugEntity.Set(transform);
        debugEntity.Set(spriteInfo);
        debugEntity.Set(new DrawComponent());

        // var worldBounds = boxCollider.Bounds.AtPosition(position.Current);

        // Add debug draw elements for the four sides of the rectangle
        // AddDebugLine(drawComponent, worldBounds.Left, worldBounds.Top, worldBounds.Right, worldBounds.Top, lineColor); // Top
        // AddDebugLine(drawComponent, worldBounds.Right - 1, worldBounds.Top, worldBounds.Right - 1, worldBounds.Bottom, lineColor); // Right
        // AddDebugLine(drawComponent, worldBounds.Left, worldBounds.Bottom - 1, worldBounds.Right, worldBounds.Bottom - 1, lineColor); // Bottom
        // AddDebugLine(drawComponent, worldBounds.Left, worldBounds.Top, worldBounds.Left, worldBounds.Bottom, lineColor); // Left
    }

    private void AddDebugLine(DrawComponent drawComponent, int x1, int y1, int x2, int y2, Color color)
    {
        // Calculate line properties
        var start = new Vector2(x1, y1);
        var end = new Vector2(x2, y2);
        var direction = end - start;
        var length = direction.Length();
        var size = x1 == x2 ? new Vector2(1, length) : new Vector2(length, 1);

        // Create a DrawElement for the line
        var lineElement = new DrawElement
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.Main,
            Texture = _pixelTexture,
            Position = start,
            Size = size,
            Color = color,
            LayerDepth = DEBUG_LINE_DEPTH,
        };

        // drawComponent.Drawables.Add(lineElement);
    }

    public void Update(GameState state)
    {
        // This system doesn't need to update every frame since it only responds to entity changes
        // The work is done in the EntityAdded event handler
    }

    public void Dispose()
    {
        _pixelTexture?.Dispose();
    }
}
