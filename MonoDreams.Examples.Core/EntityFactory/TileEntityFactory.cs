using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Draw;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Draw;
using MonoDreams.Component.Draw;
using MonoDreams.EntityFactory;
using MonoDreams.Message;
using MonoDreams.System.Level;

namespace MonoDreams.Examples.EntityFactory;

public class TileEntityFactory(DrawLayerMap layers) : IEntityFactory
{
    /// <summary>The LDtk layer opacity off the request's <c>ldtk:</c> channel; 1 for a code-driven
    /// spawn that carries no LDtk layer context.</summary>
    private static float LayerOpacity(in EntitySpawnRequest request) =>
        request.CustomFields.TryGetValue(LDtkSpawnFields.LayerOpacity, out var value) && value is float opacity
            ? opacity
            : 1f;

    public Entity CreateEntity(World world, in EntitySpawnRequest request)
    {
        var entity = world.CreateEntity();

        entity.Set(new EntityInfoComponent(nameof(EntityType.Tile)));
        entity.Set(new TransformComponent(request.Position));

        // Extract from custom fields (same pattern as WallEntityFactory)
        var layerDepth = request.CustomFields.TryGetValue("layerDepth", out var depth) ? (float)depth : layers.GetDepth(GameDrawLayer.Tiles);
        var tilesetTexture = request.CustomFields.TryGetValue("tilesetTexture", out var texture) ? (Texture2D)texture : null;
        var tilesetKey = request.CustomFields.TryGetValue("tilesetKey", out var key) ? key as string : null;

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

        // Adiciona sprites extras se necessário (ex: overlay, efeitos)
        // AddExtraSprites(drawComponent, request);
        
        // entity.Set(drawComponent);

        // Componentes específicos do tipo de tile
        ProcessCustomFields(entity, request.CustomFields);

        return entity;
    }
    
    private void ProcessCustomFields(Entity entity, Dictionary<string, object> customFields)
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