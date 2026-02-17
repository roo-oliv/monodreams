using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Physics;
using MonoDreams.Examples.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Examples.Message.Level;

namespace MonoDreams.Examples.EntityFactory;

/// <summary>
/// Factory for creating wall entities that have collision.
/// Creates entities with EntityInfo, Position, DrawComponent, BoxCollider, and RigidBody.
/// </summary>
public class WallEntityFactory : IEntityFactory
{
    private readonly ContentManager _content;

    public WallEntityFactory(ContentManager content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public Entity CreateEntity(World world, in EntitySpawnRequest request)
    {
        var entity = world.CreateEntity();

        // Core components for wall entities
        entity.Set(new EntityInfo(EntityType.Tile));
        entity.Set(new Transform(request.Position));

        // // Create DrawComponent with a single DrawElement
        // var drawComponent = new DrawComponent();
        
        // Extract custom fields
        var layerDepth = request.CustomFields.TryGetValue("layerDepth", out var depth) ? (float)depth : 0.09f;
        var tilesetTexture = request.CustomFields.TryGetValue("tilesetTexture", out var texture) ? (Texture2D)texture : null;

        // if (tilesetTexture != null)
        // {
        //     var drawElement = new DrawElement
        //     {
        //         Type = DrawElementType.Sprite,
        //         Target = RenderTargetID.Main,
        //         Texture = tilesetTexture,
        //         Position = request.Position,
        //         SourceRectangle = new Rectangle(request.TilesetPosition.ToPoint(), 
        //             new Point((int)request.Size.X, (int)request.Size.Y)),
        //         Color = Color.White * request.Layer._Opacity,
        //         Size = request.Size,
        //         LayerDepth = layerDepth
        //     };
        //     drawComponent.Drawables.Add(drawElement);
        // }
        //
        // entity.Set(drawComponent);
        if (tilesetTexture != null)
        {
            entity.Set(new SpriteInfo
            {
                SpriteSheet = tilesetTexture,
                Source = new Rectangle((int)request.TilesetPosition.X, (int)request.TilesetPosition.Y,
                    (int)request.Size.X, (int)request.Size.Y),
                Size = request.Size,
                Color = Color.White * request.Layer._Opacity,
                Target = RenderTargetID.Main,
                LayerDepth = layerDepth,
            });
        }
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.Main,
        });

        // Add collision components for walls
        var colliderBounds = new Rectangle(
            Point.Zero, // Relative to entity position
            new Point((int)request.Size.X, (int)request.Size.Y)
        );
        
        entity.Set(new BoxCollider(colliderBounds, passive: true));
        entity.Set(new RigidBody());

        // Process any additional custom fields
        ProcessCustomFields(entity, request.CustomFields);

        return entity;
    }

    private void ProcessCustomFields(Entity entity, Dictionary<string, object> customFields)
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
