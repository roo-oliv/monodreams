using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.State;
using MonoDreams.Draw;
using Position = MonoDreams.Component.Position;

namespace MonoDreams.System.Draw;

public class DrawSystem(World world, SpriteBatch spriteBatch, RenderTarget2D renderTarget, IParallelRunner parallelRunner)
    : AEntitySetSystem<GameState>(world.GetEntities().With((in DrawInfo drawInfo) => drawInfo.RenderTarget == renderTarget).AsSet(), parallelRunner)
{

    protected override void Update(GameState state, in Entity entity)
    {
        ref var drawInfo = ref entity.Get<DrawInfo>();
        ref var position = ref entity.Get<Position>();
        var layerDepth = DrawLayerDepth.GetLayerDepth(drawInfo.Layer);
        if (drawInfo.NinePatchInfo == null)
        {
            spriteBatch.Draw(
                drawInfo.SpriteSheet, new Rectangle(position.Current.ToPoint(), drawInfo.Size), drawInfo.Source,
                drawInfo.Color, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
        }
        else
        {
            var pos = position.Current.ToPoint();

            spriteBatch.Draw(drawInfo.NinePatchTextures.topLeft, new Rectangle(pos + drawInfo.NinePatchDestinations.topLeft.Location, drawInfo.NinePatchDestinations.topLeft.Size), null, drawInfo.Color, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
            spriteBatch.Draw(drawInfo.NinePatchTextures.top, new Rectangle(pos + drawInfo.NinePatchDestinations.top.Location, drawInfo.NinePatchDestinations.top.Size), null, drawInfo.Color, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
            spriteBatch.Draw(drawInfo.NinePatchTextures.topRight, new Rectangle(pos + drawInfo.NinePatchDestinations.topRight.Location, drawInfo.NinePatchDestinations.topRight.Size), null, drawInfo.Color, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
            spriteBatch.Draw(drawInfo.NinePatchTextures.left, new Rectangle(pos + drawInfo.NinePatchDestinations.left.Location, drawInfo.NinePatchDestinations.left.Size), null, drawInfo.Color, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
            spriteBatch.Draw(drawInfo.NinePatchTextures.center, new Rectangle(pos + drawInfo.NinePatchDestinations.center.Location, drawInfo.NinePatchDestinations.center.Size), null, drawInfo.Color, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
            spriteBatch.Draw(drawInfo.NinePatchTextures.right, new Rectangle(pos + drawInfo.NinePatchDestinations.right.Location, drawInfo.NinePatchDestinations.right.Size), null, drawInfo.Color, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
            spriteBatch.Draw(drawInfo.NinePatchTextures.bottomLeft, new Rectangle(pos + drawInfo.NinePatchDestinations.bottomLeft.Location, drawInfo.NinePatchDestinations.bottomLeft.Size), null, drawInfo.Color, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
            spriteBatch.Draw(drawInfo.NinePatchTextures.bottom, new Rectangle(pos + drawInfo.NinePatchDestinations.bottom.Location, drawInfo.NinePatchDestinations.bottom.Size), null, drawInfo.Color, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
            spriteBatch.Draw(drawInfo.NinePatchTextures.bottomRight, new Rectangle(pos + drawInfo.NinePatchDestinations.bottomRight.Location, drawInfo.NinePatchDestinations.bottomRight.Size), null, drawInfo.Color, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
        }
    }
}
