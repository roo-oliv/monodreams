using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Input;
using MonoDreams.Component.Physics;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.Message.Level;

namespace MonoDreams.Examples.EntityFactory;

/// <summary>
/// Factory for creating player entities with all necessary components
/// </summary>
public class PlayerEntityFactory : IEntityFactory
{
    private readonly ContentManager _content;
    private readonly Texture2D _charactersTileset;

    public PlayerEntityFactory(ContentManager content)
    {
        _content = content;
        _charactersTileset = content.Load<Texture2D>("Characters");
    }

    public Entity CreateEntity(World world, in EntitySpawnRequest request)
    {
        var entity = world.CreateEntity();

        // Add core components
        entity.Set(new EntityInfo(EntityType.Player));
        entity.Set(new PlayerState());
        entity.Set(new Position(request.Position));
        entity.Set(new InputControlled());
        entity.Set(new BoxCollider(new Rectangle(Point.Zero, Constants.PlayerSize)));
        entity.Set(new RigidBody());
        entity.Set(new Velocity());

        // Add sprite information for rendering
        entity.Set(new SpriteInfo
        {
            SpriteSheet = _charactersTileset,
            Source = new Rectangle((int)request.TilesetPosition.X, (int)request.TilesetPosition.Y, 
                                 request.Layer._GridSize, request.Layer._GridSize),
            Size = new Vector2(request.Layer._GridSize, request.Layer._GridSize),
            Color = Color.White * request.Layer._Opacity,
            Target = RenderTargetID.Main,
            LayerDepth = 0.1f // Or calculate based on some logic
        });
        entity.Set(new DrawComponent());

        // Process custom fields from LDtk
        ProcessCustomFields(entity, request.CustomFields);

        return entity;
    }

    private void ProcessCustomFields(Entity entity, Dictionary<string, object> customFields)
    {
        // Handle player-specific custom fields from LDtk
        if (customFields.TryGetValue("health", out var health) && health is int healthValue)
        {
            // Add health component if specified
            // entity.Set(new Health(healthValue));
        }

        if (customFields.TryGetValue("speed", out var speed) && speed is float speedValue)
        {
            // Modify velocity or add speed component
            // entity.Set(new MovementSpeed(speedValue));
        }
    }
}