using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Physics;
using MonoDreams.Draw;
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
        entity.Set(new Transform(request.Position));
        // entity.Set(new BoxCollider(new Rectangle(Constants.PlayerOffset.ToPoint(), Constants.PlayerSize)));
        entity.Set(new BoxCollider(new Rectangle(Point.Zero, Constants.PlayerSize)));
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
            Offset = Constants.PlayerSpriteOffset,
        });
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.Main,
        });

        // Process custom fields from LDtk
        ProcessCustomFields(entity, request.CustomFields);

        // Create orbiting orbs
        var playerTransform = entity.Get<Transform>();
        CreateBlueOrb(world, playerTransform);

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

    private void CreateBlueOrb(World world, Transform parentTransform)
    {
        var orbEntity = world.CreateEntity();

        // Create transform parented to player
        var orbTransform = new Transform(
            position: new Vector2(50, 0),
            rotation: 0f,
            scale: Vector2.One
        );
        orbTransform.Parent = parentTransform;
        orbEntity.Set(orbTransform);

        // Add orbital motion component
        orbEntity.Set(new OrbitalMotion
        {
            Angle = 0f,
            Radius = 300f,
            Speed = 1f,  // clockwise
            CenterOffset = new Vector2(8, 16)
        });

        // Create blue circle mesh
        var circleMesh = new CircleMeshGenerator(
            center: Vector2.Zero,
            radius: 9f,
            color: Color.DodgerBlue,
            segments: 20
        ).Generate();

        orbEntity.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Main,
            Vertices = circleMesh.Vertices,
            Indices = circleMesh.Indices,
            PrimitiveType = circleMesh.PrimitiveType,
            LayerDepth = 0.991f
        });
        orbEntity.Set(new Visible());

        // Create 3 red child orbs evenly spaced (0°, 120°, 240°)
        CreateRedOrb(world, orbTransform, 0f);
        CreateRedOrb(world, orbTransform, MathF.PI * 2f / 3f);      // 120°
        CreateRedOrb(world, orbTransform, MathF.PI * 4f / 3f);      // 240°
    }

    private void CreateRedOrb(World world, Transform parentTransform, float startAngle)
    {
        var orbEntity = world.CreateEntity();
        var rnd = new Random();

        // Create transform parented to blue orb
        var orbTransform = new Transform(
            position: new Vector2(20, 0),
            rotation: 0f,
            scale: Vector2.One
        );
        orbTransform.Parent = parentTransform;
        orbEntity.Set(orbTransform);

        // Add orbital motion component with starting angle offset
        orbEntity.Set(new OrbitalMotion
        {
            Angle = startAngle,
            Radius = 20f,
            Speed = 5.0f + (rnd.NextSingle() * (7.0f - 5.0f)),  // clockwise, same direction as parent
            CenterOffset = Vector2.Zero
        });

        // Create red circle mesh (smaller)
        var circleMesh = new CircleMeshGenerator(
            center: Vector2.Zero,
            radius: 4f,
            color: Color.Red,
            segments: 16
        ).Generate();

        orbEntity.Set(new DrawComponent
        {
            Type = DrawElementType.Mesh,
            Target = RenderTargetID.Main,
            Vertices = circleMesh.Vertices,
            Indices = circleMesh.Indices,
            PrimitiveType = circleMesh.PrimitiveType,
            LayerDepth = 0.992f
        });
        orbEntity.Set(new Visible());
    }
}