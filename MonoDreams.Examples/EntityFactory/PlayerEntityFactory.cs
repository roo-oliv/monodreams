using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Input;
using MonoDreams.Component.Physics;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Camera;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.Message.Level;

namespace MonoDreams.Examples.EntityFactory;

/// <summary>
/// Factory for creating player entities with all necessary components
/// </summary>
public class PlayerEntityFactory(ContentManager content) : IEntityFactory
{
    private readonly Texture2D _charactersTileset = content.Load<Texture2D>("Atlas/TX Player");

    public Entity CreateEntity(World world, in EntitySpawnRequest request)
    {
        var entity = world.CreateEntity();

        // Add core components
        entity.Set(new EntityInfo(EntityType.Player));
        entity.Set(new PlayerState());
        entity.Set(new Position(request.Position));
        entity.Set(new InputControlled());
        entity.Set(new BoxCollider(new Rectangle(Constants.PlayerOffset.ToPoint(), Constants.PlayerSize)));
        entity.Set(new RigidBody());
        entity.Set(new Velocity());

        // Add camera follow target component
        entity.Set(new CameraFollowTarget
        {
            DampingX = 5.0f,  // Adjust for desired smoothness
            DampingY = 5.0f,
            MaxDistanceX = 150.0f,  // Maximum distance camera can lag behind
            MaxDistanceY = 100.0f,
            IsActive = true
        });

        // Add sprite information for rendering
        entity.Set(new SpriteInfo
        {
            SpriteSheet = _charactersTileset,
            Source = new Rectangle((int)request.TilesetPosition.X, (int)request.TilesetPosition.Y, 
                                 (int)request.Size.X, (int)request.Size.Y),
            Size = request.Size,
            Color = Color.White * request.Layer._Opacity,
            Target = RenderTargetID.Main,
            LayerDepth = 0.1f,
            Offset = Constants.PlayerOffset,
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
        
        // Handle camera follow configuration from LDtk
        if (customFields.TryGetValue("cameraDamping", out var damping) && damping is float dampingValue)
        {
            var followTarget = entity.Get<CameraFollowTarget>();
            followTarget.DampingX = dampingValue;
            followTarget.DampingY = dampingValue;
            entity.Set(followTarget);
        }
    }
}