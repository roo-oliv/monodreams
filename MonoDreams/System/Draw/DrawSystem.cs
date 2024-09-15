using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.State;
using MonoDreams.Draw;
using Position = MonoDreams.Component.Physics.Position;

namespace MonoDreams.System.Draw;

public sealed class DrawSystem(World world, SpriteBatch spriteBatch, IParallelRunner parallelRunner)
    : AEntitySetSystem<GameState>(world.GetEntities().With<DrawInfo>().AsSet(), parallelRunner)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var drawInfo = ref entity.Get<DrawInfo>();
        ref var position = ref entity.Get<Position>();
        var layerDepth = DrawLayerDepth.GetLayerDepth(drawInfo.Layer);
        spriteBatch.Draw(drawInfo.SpriteSheet,  new Rectangle(position.Current.ToPoint(), drawInfo.Size), drawInfo.Source, drawInfo.Color, 0f, Vector2.Zero, SpriteEffects.None, layerDepth);
    }
}
