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
using MonoDreams.Message;

namespace MonoDreams.Examples.EntityFactory;

/// <summary>
/// Factory for creating NPC entities
/// </summary>
public class NPCEntityFactory(ContentManager content, DrawLayerMap layers) : IEntityFactory
{
    private readonly Texture2D _charactersTileset = content.Load<Texture2D>("Characters");

    public Entity CreateEntity(World world, in EntitySpawnRequest request)
    {
        var entity = world.CreateEntity();

        // Add core components
        var name = request.CustomFields.TryGetValue("name", out var n) ? n as string : null;
        entity.Set(new EntityInfo(nameof(EntityType.NPC), name));
        entity.Set(new Transform(request.Position));
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
            LayerDepth = layers.GetDepth(GameDrawLayer.Characters)
        });
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.Main,
        });

        // Process custom fields from LDtk
        ProcessCustomFields(entity, request.CustomFields);

        return entity;
    }

    private void ProcessCustomFields(Entity entity, Dictionary<string, object> customFields)
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