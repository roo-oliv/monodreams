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
/// Factory for creating NPC entities
/// </summary>
public class NPCEntityFactory(ContentManager content) : IEntityFactory
{
    private readonly Texture2D _charactersTileset = content.Load<Texture2D>("Characters");

    public DefaultEcsEntity CreateEntity(World world, DefaultEcsWorld defaultEcsWorld, in EntitySpawnRequest request)
    {
        var entityInfoComponent = new EntityInfo(EntityType.Enemy);
        var positionComponent = new Position(request.Position);
        var boxColliderComponent = new BoxCollider(new Rectangle(Point.Zero, Constants.PlayerSize));
        var rigidBodyComponent = new RigidBody();
        var velocityComponent = new Velocity();
        var spriteInfoComponent = new SpriteInfo
        {
            SpriteSheet = _charactersTileset,
            Source = new Rectangle((int)request.TilesetPosition.X, (int)request.TilesetPosition.Y, 
                                 request.Layer._GridSize, request.Layer._GridSize),
            Size = new Vector2(request.Layer._GridSize, request.Layer._GridSize),
            Color = Color.White * request.Layer._Opacity,
            Target = RenderTargetID.Main,
            LayerDepth = 0.1f
        };
        var drawComponent = new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.Main,
        };

        // Create Flecs entity
        world.Entity()
            .Set(entityInfoComponent)
            .Set(positionComponent)
            .Set(boxColliderComponent)
            .Set(rigidBodyComponent)
            .Set(velocityComponent)
            .Set(spriteInfoComponent)
            .Set(drawComponent);

        // Create DefaultECS entity
        var entity = defaultEcsWorld.CreateEntity();

        // Add core components to DefaultECS entity
        entity.Set(entityInfoComponent);
        entity.Set(positionComponent);
        entity.Set(boxColliderComponent);
        entity.Set(rigidBodyComponent);
        entity.Set(velocityComponent);
        entity.Set(spriteInfoComponent);
        entity.Set(drawComponent);

        // Process custom fields from LDtk
        ProcessCustomFields(entity, request.CustomFields);

        return entity;
    }

    private void ProcessCustomFields(DefaultEcsEntity entity, Dictionary<string, object> customFields)
    {
        // Handle NPC-specific custom fields from LDtk
        if (customFields.TryGetValue("dialogue", out var dialogue) && dialogue is string dialogueId)
        {
            // Add dialogue component
            // entity.Set(new DialogueTrigger(dialogueId));
        }

        if (customFields.TryGetValue("patrolRadius", out var radius) && radius is float radiusValue)
        {
            // Add patrol behavior
            // entity.Set(new PatrolBehavior(radiusValue));
        }
    }
}