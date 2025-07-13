
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.Message.Level;

namespace MonoDreams.Examples.EntityFactory;

/// <summary>
/// Factory for creating miscellaneous entities like tiles and decorative elements.
/// Creates entities with EntityInfo, Position, and DrawComponent.
/// </summary>
public class MiscEntityFactory : IEntityFactory
{
    private readonly ContentManager _content;

    public MiscEntityFactory(ContentManager content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public Entity CreateEntity(World world, in EntitySpawnRequest request)
    {
        var entity = world.CreateEntity();

        // Core components for misc entities
        entity.Set(new EntityInfo(EntityType.Tile));
        entity.Set(new Position(request.Position));

        // Create DrawComponent with a single DrawElement
        var drawComponent = new DrawComponent();
        
        // Extract custom fields
        var layerDepth = request.CustomFields.TryGetValue("layerDepth", out var depth) ? (float)depth : 0.09f;
        var tilesetTexture = request.CustomFields.TryGetValue("tilesetTexture", out var texture) ? (Texture2D)texture : null;

        if (tilesetTexture != null)
        {
            var drawElement = new DrawElement
            {
                Type = DrawElementType.Sprite,
                Target = RenderTargetID.Main,
                Texture = tilesetTexture,
                Position = request.Position,
                SourceRectangle = new Rectangle(request.TilesetPosition.ToPoint(), 
                    new Point((int)request.Size.X, (int)request.Size.Y)),
                Color = Color.White * request.Layer._Opacity,
                Size = request.Size,
                LayerDepth = layerDepth
            };
            drawComponent.Drawables.Add(drawElement);
        }

        entity.Set(drawComponent);

        // Process any additional custom fields
        ProcessCustomFields(entity, request.CustomFields);

        return entity;
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
