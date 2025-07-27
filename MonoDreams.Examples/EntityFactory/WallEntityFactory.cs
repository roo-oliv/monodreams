using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Physics;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.Message.Level;
using DefaultEcsWorld = DefaultEcs.World;
using DefaultEcsEntity = DefaultEcs.Entity;

namespace MonoDreams.Examples.EntityFactory;

/// <summary>
/// Factory for creating wall entities that have collision.
/// Creates entities with EntityInfo, Position, DrawComponent, BoxCollider, and RigidBody.
/// </summary>
public class WallEntityFactory(ContentManager content) : IEntityFactory
{
    public DefaultEcsEntity CreateEntity(World world, DefaultEcsWorld defaultEcsWorld, in EntitySpawnRequest request)
    {
        var entityInfoComponent = new EntityInfo(EntityType.Tile);
        var positionComponent = new Position(request.Position);
        // Extract custom fields
        var layerDepth = request.CustomFields.TryGetValue("layerDepth", out var depth) ? (float)depth : 0.09f;
        var tilesetTexture = request.CustomFields.TryGetValue("tilesetTexture", out var texture) ? (Texture2D)texture : null;
        SpriteInfo? spriteInfoComponent = null; 
        if (tilesetTexture != null)
        {
            spriteInfoComponent = new SpriteInfo
            {
                SpriteSheet = tilesetTexture,
                Source = new Rectangle((int)request.TilesetPosition.X, (int)request.TilesetPosition.Y,
                    (int)request.Size.X, (int)request.Size.Y),
                Size = request.Size,
                Color = Color.White * request.Layer._Opacity,
                Target = RenderTargetID.Main,
                LayerDepth = layerDepth,
            };
        }
        var drawComponent = new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.Main,
        };
        var boxColliderComponent = new BoxCollider(
            new Rectangle(Point.Zero, new Point((int)request.Size.X, (int)request.Size.Y)),
            passive: true);
        var rigidBodyComponent = new RigidBody();

        var entity = world.Entity()
            .Set(entityInfoComponent)
            .Set(positionComponent)
            .Set(drawComponent)
            .Set(boxColliderComponent)
            .Set(rigidBodyComponent);
        if (spriteInfoComponent.HasValue) entity.Set(spriteInfoComponent.Value);
        
        var defaultEcsEntity = defaultEcsWorld.CreateEntity();
        defaultEcsEntity.Set(entityInfoComponent);
        defaultEcsEntity.Set(positionComponent);
        if (spriteInfoComponent.HasValue) defaultEcsEntity.Set(spriteInfoComponent.Value);
        defaultEcsEntity.Set(drawComponent);
        defaultEcsEntity.Set(boxColliderComponent);
        defaultEcsEntity.Set(rigidBodyComponent);
        ProcessCustomFields(defaultEcsEntity, request.CustomFields);

        return defaultEcsEntity;
    }

    private void ProcessCustomFields(DefaultEcsEntity entity, Dictionary<string, object> customFields)
    {
        // Handle wall-specific custom fields from LDtk
        if (customFields.TryGetValue("destructible", out var destructible) && destructible is bool isDestructible && isDestructible)
        {
            // Could add destructible component here
            // entity.Set(new DestructibleComponent());
        }

        if (customFields.TryGetValue("damage", out var damage) && damage is int damageValue)
        {
            // Could add damage component for harmful walls
            // entity.Set(new DamageComponent(damageValue));
        }
    }
}
