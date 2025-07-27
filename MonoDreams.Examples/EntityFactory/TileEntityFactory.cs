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

public class TileEntityFactory(ContentManager content) : IEntityFactory
{
    private readonly Texture2D _tilemap = content.Load<Texture2D>("Atlas/Tilemap");

    public DefaultEcsEntity CreateEntity(World world, DefaultEcsWorld defaultEcsWorld, in EntitySpawnRequest request)
    {
        var entity = defaultEcsWorld.CreateEntity();
        
        entity.Set(new EntityInfo(EntityType.Tile));
        entity.Set(new Position(request.Position));

        // // DrawComponent com múltiples sprites (se necessário)
        // var drawComponent = new DrawComponent();
        
        // // Sprite principal do tile
        // var mainSprite = new DrawElement
        // {
        //     Type = DrawElementType.Sprite,
        //     Target = RenderTargetID.Main,
        //     Texture = _tilemap,
        //     Position = request.Position,
        //     SourceRectangle = new Rectangle(request.TilesetPosition.ToPoint(), new Point(request.Layer._GridSize, request.Layer._GridSize)),
        //     Color = Color.White * request.Layer._Opacity,
        //     Size = request.Size,
        //     LayerDepth = 1f,
        // };
        // drawComponent.Drawables.Add(mainSprite);
        
        entity.Set(new SpriteInfo
        {
            SpriteSheet = _tilemap,
            Source = new Rectangle((int)request.TilesetPosition.X, (int)request.TilesetPosition.Y, 
                (int)request.Size.X, (int)request.Size.Y),
            Size = request.Size,
            Color = Color.White * request.Layer._Opacity,
            Target = RenderTargetID.Main,
            LayerDepth = 1f,
        });
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.Main,
        });

        // Adiciona sprites extras se necessário (ex: overlay, efeitos)
        // AddExtraSprites(drawComponent, request);
        
        // entity.Set(drawComponent);

        // Componentes específicos do tipo de tile
        ProcessCustomFields(entity, request.CustomFields);

        return entity;
    }
    
    private void ProcessCustomFields(DefaultEcsEntity entity, Dictionary<string, object> customFields)
    {
        // Handle tile-specific custom fields from LDtk
        // if (customFields.TryGetValue("?", out var ?))
        // {
        //     // Add ...
        // }
    }

    // private void AddExtraSprites(DrawComponent drawComponent, EntitySpawnRequest request)
    // {
    //     // Exemplo: Goal tiles podem ter um efeito brilhante
    //     if (request.Identifier == "Goal")
    //     {
    //         var glowEffect = new DrawElement
    //         {
    //             Type = DrawElementType.Sprite,
    //             Target = RenderTargetID.Main,
    //             Texture = content.Load<Texture2D>("Effects/Glow"),
    //             Position = request.Position,
    //             Color = Color.Gold * 0.5f,
    //             Size = request.Size,
    //             LayerDepth = GetTileLayerDepth(request.Identifier) - 0.001f,
    //         };
    //         drawComponent.Drawables.Add(glowEffect);
    //     }
    //
    //     // Exemplo: Trap tiles podem ter múltiplas camadas visuais
    //     if (request.Identifier == "Trap")
    //     {
    //         var warningOverlay = new DrawElement
    //         {
    //             Type = DrawElementType.Sprite,
    //             Target = RenderTargetID.Main,
    //             Texture = content.Load<Texture2D>("Effects/Warning"),
    //             Position = request.Position,
    //             Color = Color.Red * 0.7f,
    //             Size = request.Size,
    //             LayerDepth = GetTileLayerDepth(request.Identifier) - 0.001f,
    //         };
    //         drawComponent.Drawables.Add(warningOverlay);
    //     }
    // }
}