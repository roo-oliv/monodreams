using Flecs.NET.Core;
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
using DefaultEcsWorld = DefaultEcs.World;
using DefaultEcsEntity = DefaultEcs.Entity;

namespace MonoDreams.Examples.EntityFactory;

/// <summary>
/// Factory for creating player entities with all necessary components
/// </summary>
public class PlayerEntityFactory(ContentManager content) : IEntityFactory
{
    private readonly Texture2D _charactersTileset = content.Load<Texture2D>("Atlas/TX Player");

    public DefaultEcsEntity CreateEntity(World world, DefaultEcsWorld defaultEcsWorld, in EntitySpawnRequest request)
    {
        var entityInfoComponent = new EntityInfo(EntityType.Player);
        var playerStateComponent = new PlayerState();
        var positionComponent = new Position(request.Position);
        var inputControlledComponent = new InputControlled();
        var boxColliderComponent = new BoxCollider(new Rectangle(Point.Zero, Constants.PlayerSize));
        var rigidBodyComponent = new RigidBody();
        var velocityComponent = new Velocity();
        var cameraFollowTargetComponent = new CameraFollowTarget
        {
            DampingX = 5.0f, // Adjust for desired smoothness
            DampingY = 5.0f,
            MaxDistanceX = 150.0f, // Maximum distance camera can lag behind
            MaxDistanceY = 100.0f,
            IsActive = true
        };
        var spriteInfoComponent = new SpriteInfo
        {
            SpriteSheet = _charactersTileset,
            Source = new Rectangle((int)request.TilesetPosition.X, (int)request.TilesetPosition.Y,
                (int)request.Size.X, (int)request.Size.Y),
            Size = request.Size,
            Color = Color.White * request.Layer._Opacity,
            Target = RenderTargetID.Main,
            LayerDepth = 0.1f,
            Offset = Constants.PlayerSpriteOffset,
        };
        var drawComponent = new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.Main,
        };

        world.Entity()
            .Set(entityInfoComponent)
            .Set(playerStateComponent)
            .Set(positionComponent)
            .Set(inputControlledComponent)
            .Set(boxColliderComponent)
            .Set(rigidBodyComponent)
            .Set(velocityComponent)
            .Set(cameraFollowTargetComponent)
            .Set(spriteInfoComponent)
            .Set(drawComponent);
        
        var entity = defaultEcsWorld.CreateEntity();

        // Add core components
        entity.Set(entityInfoComponent);
        entity.Set(playerStateComponent);
        entity.Set(positionComponent);
        entity.Set(inputControlledComponent);
        // entity.Set(new BoxCollider(new Rectangle(Constants.PlayerOffset.ToPoint(), Constants.PlayerSize)));
        entity.Set(boxColliderComponent);
        entity.Set(rigidBodyComponent);
        entity.Set(velocityComponent);

        // Add camera follow target component
        entity.Set(cameraFollowTargetComponent);

        // Add sprite information for rendering
        entity.Set(spriteInfoComponent);
        entity.Set(drawComponent);

        // Process custom fields from LDtk
        ProcessCustomFields(entity, request.CustomFields);

        return entity;
    }

    private void ProcessCustomFields(DefaultEcsEntity entity, Dictionary<string, object> customFields)
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