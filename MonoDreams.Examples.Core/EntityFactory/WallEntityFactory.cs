using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Physics;
using MonoDreams.Draw;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Draw;
using MonoDreams.Component.Draw;
using MonoDreams.EntityFactory;
using MonoDreams.Extension;
using MonoDreams.Message;
using MonoDreams.System.Level;

namespace MonoDreams.Examples.EntityFactory;

/// <summary>
/// Factory for creating wall entities that have collision.
/// Creates entities with EntityInfoComponent, Position, DrawComponent, BoxColliderComponent, and RigidBodyComponent.
/// </summary>
public class WallEntityFactory : IEntityFactory
{
    private readonly ContentManager _content;
    private readonly DrawLayerMap _layers;

    /// <summary>The LDtk layer opacity off the request's <c>ldtk:</c> channel; 1 for a code-driven
    /// spawn that carries no LDtk layer context.</summary>
    private static float LayerOpacity(in EntitySpawnRequest request) =>
        request.CustomFields.TryGetValue(LDtkSpawnFields.LayerOpacity, out var value) && value is float opacity
            ? opacity
            : 1f;

    public WallEntityFactory(ContentManager content, DrawLayerMap layers)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _layers = layers ?? throw new ArgumentNullException(nameof(layers));
    }

    public Entity CreateEntity(World world, in EntitySpawnRequest request)
    {
        var entity = world.CreateEntity();

        // Core components for wall entities
        entity.Set(new EntityInfoComponent(nameof(EntityType.Wall)));
        entity.Set(new TransformComponent(request.Position));

        // // Create DrawComponent with a single DrawElement
        // var drawComponent = new DrawComponent();
        
        // Extract custom fields
        var layerDepth = request.CustomFields.TryGetValue("layerDepth", out var depth) ? (float)depth : _layers.GetDepth(GameDrawLayer.Environment);
        var tilesetTexture = request.CustomFields.TryGetValue("tilesetTexture", out var texture) ? (Texture2D)texture : null;
        var tilesetKey = request.CustomFields.TryGetValue("tilesetKey", out var key) ? key as string : null;

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
        //         Color = Color.White * LayerOpacity(request),
        //         Size = request.Size,
        //         LayerDepth = layerDepth
        //     };
        //     drawComponent.Drawables.Add(drawElement);
        // }
        //
        // entity.Set(drawComponent);
        if (tilesetTexture != null)
        {
            entity.Set(new SpriteInfoComponent
            {
                SpriteSheet = tilesetTexture,
                AssetKey = tilesetKey, // content key so an imported native scene re-loads this tileset
                Source = new Rectangle((int)request.TilesetPosition.X, (int)request.TilesetPosition.Y,
                    (int)request.Size.X, (int)request.Size.Y),
                Size = request.Size,
                Color = Color.White * LayerOpacity(request),
                Target = RenderTargetID.Main,
                LayerDepth = layerDepth,
            });
        }
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.Main,
        });

        // Add collision components for walls. Colliders-as-entities: the collider is a child entity;
        // the wall is the body (RigidBody). The former top-left footprint's centre (Size/2) keeps the
        // world rect unchanged (box now centered on the child's transform).
        entity.Set(new RigidBodyComponent());

        var wallCollider = world.CreateEntity();
        wallCollider.Set(new TransformComponent(new Vector2(request.Size.X / 2f, request.Size.Y / 2f)));
        wallCollider.Set(new BoxColliderComponent(new Vector2(request.Size.X, request.Size.Y), passive: true));
        wallCollider.SetParent(entity);

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
