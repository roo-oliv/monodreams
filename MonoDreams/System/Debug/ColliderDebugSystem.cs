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
/// Debug system that renders collision shapes for entities with BoxCollider or ConvexCollider components.
/// Shows green lines for passive colliders and red lines for active colliders.
/// Only adds debug visuals when a collider is added to an entity.
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
        _world.SubscribeEntityComponentAdded<ConvexCollider>(AddDebugLinesToConvexEntity);
    }

    private void AddDebugLinesToEntity(in Entity entity, in BoxCollider boxCollider)
    {
        var size = boxCollider.Bounds.Dimension();
        if (size.X <= 0 || size.Y <= 0)
            return;

        ref readonly var transform = ref entity.Get<Transform>();
        var lineColor = GetDebugColor(boxCollider);
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
    }

    private void AddDebugLinesToConvexEntity(in Entity entity, in ConvexCollider convexCollider)
    {
        // Use the broad-phase AABB as a debug outline for convex colliders
        var aabb = SATCollision.ComputeAABB(convexCollider.ModelVertices);
        if (aabb.Size.X <= 0 || aabb.Size.Y <= 0)
            return;

        ref readonly var transform = ref entity.Get<Transform>();
        var lineColor = GetDebugColor(convexCollider);

        var r = new Rectangle(0, 0, 1, 1);
        var c = new Rectangle(1, 0, 1, 1);
        var spriteInfo = new SpriteInfo{
            SpriteSheet = _pixelTexture,
            Source = new Rectangle(0, 0, 1, 1),
            Size = aabb.Size,
            Color = lineColor,
            Target = RenderTargetID.Main,
            LayerDepth = DEBUG_LINE_DEPTH,
            NinePatchData = new NinePatchInfo(0, r, r, r, r, c, r, r, r, r),
            Offset = aabb.Position,
        };

        var debugEntity = _world.CreateEntity();
        debugEntity.Set(transform);
        debugEntity.Set(spriteInfo);
        debugEntity.Set(new DrawComponent());
    }

    private static Color GetDebugColor(ICollider collider)
    {
        if (!collider.Enabled) return Color.Gray;
        return collider.Passive ? Color.Green : Color.Red;
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
