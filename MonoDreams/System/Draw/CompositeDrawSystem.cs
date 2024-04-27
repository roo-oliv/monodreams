using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Draw;
using MonoDreams.State;
using MonoGame.Extended.TextureAtlases;

namespace MonoDreams.System.Draw;

public sealed class CompositeDrawSystem(SpriteBatch batch, World world)
    : AEntitySetSystem<GameState>(world.GetEntities().With<CompositeDrawInfo>().AsSet())
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var compositeDrawInfo = ref entity.Get<CompositeDrawInfo>();
        ref var position = ref entity.Get<Position>();
        
        var sourcePatches = compositeDrawInfo.NinePatchRegion.SourcePatches;
        var compositeDestination = new Rectangle(position.CurrentLocation.ToPoint(), compositeDrawInfo.Size);
        var destinationPatches = compositeDrawInfo.NinePatchRegion.CreatePatches(compositeDestination);
        var layerDepth = DrawLayerDepth.GetLayerDepth(compositeDrawInfo.Layer);
        for (var i = 0; i < destinationPatches.Length; i++)
        {
            batch.Draw(
                compositeDrawInfo.Texture,
                destinationPatches[i],
                sourcePatches[i],
                compositeDrawInfo.Color,
                0,
                Vector2.Zero,
                SpriteEffects.None,
                layerDepth);
        }
    }
}