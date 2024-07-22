using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.State;
using MonoDreams.Draw;

namespace MonoDreams.System.Draw;

public sealed class DrawSystem(World world, SpriteBatch spriteBatch)
    : AEntitySetSystem<GameState>(world.GetEntities().With<DrawInfo>().AsSet())
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var drawInfo = ref entity.Get<DrawInfo>();
        ref var position = ref entity.Get<Position>();
        var layerDepth = DrawLayerDepth.GetLayerDepth(drawInfo.Layer);
        spriteBatch.Draw(drawInfo.SpriteSheet, new Rectangle(position.CurrentLocation.ToPoint(), drawInfo.Size), drawInfo.Source, drawInfo.Color, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
    }
}
