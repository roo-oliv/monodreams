using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.Message.Level;
using DefaultEcsWorld = DefaultEcs.World;
using DefaultEcsEntity = DefaultEcs.Entity;

namespace MonoDreams.Examples.EntityFactory;

/// <summary>
/// Factory for creating miscellaneous entities like tiles and decorative elements.
/// Creates entities with EntityInfo, Position, and DrawComponent.
/// </summary>
public class MiscEntityFactory(ContentManager content) : IEntityFactory
{
    public DefaultEcsEntity CreateEntity(World world, DefaultEcsWorld defaultEcsWorld, in EntitySpawnRequest request)
    {
        // Extract custom fields
        var layerDepth = request.CustomFields.TryGetValue("layerDepth", out var depth) ? (float)depth : 0.09f;
        var tilesetTexture = request.CustomFields.TryGetValue("tilesetTexture", out var texture) ? (Texture2D)texture : null;

        // Create Flecs entity
        var entity = world.Entity()
            .Set(new EntityInfo(EntityType.Tile))
            .Set(new Position(request.Position))
            .Set(new DrawComponent
            {
                Type = DrawElementType.Sprite,
                Target = RenderTargetID.Main,
            });

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

        // Process any additional custom fields
        ProcessCustomFields(entity, request.CustomFields);

        return defaultEcsWorld.CreateEntity();
    }

    private void ProcessCustomFields(Entity entity, Dictionary<string, object> customFields)
    {
        // Handle misc-specific custom fields from LDtk
        if (customFields.TryGetValue("animated", out var animated) && animated is bool isAnimated && isAnimated)
        {
            // Could add animation component here if needed
            // entity.Set(new AnimationComponent());
        }

        if (customFields.TryGetValue("collectible", out var collectible) && collectible is bool isCollectible && isCollectible)
        {
            // Could add collectible component here
            // entity.Set(new CollectibleComponent());
        }
    }
}