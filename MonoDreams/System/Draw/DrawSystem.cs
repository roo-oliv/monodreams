using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Draw;
using MonoDreams.State;

namespace MonoDreams.System.Draw;

public sealed class DrawSystem(SpriteBatch batch, World world)
    : AEntitySetSystem<GameState>(world.GetEntities().With<DrawInfo>().AsSet())
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var drawInfo = ref entity.Get<DrawInfo>();
        ref var position = ref entity.Get<Position>();

        if (entity.Has<Animation>())
        {
            ref var animation = ref entity.Get<Animation>();
        
            var totalDuration = animation.FrameDuration * animation.TotalFrames;
        
            animation.CurrentFrame = (int)((state.TotalTime % totalDuration) / animation.FrameDuration);
        
            var frameWidth = drawInfo.SpriteSheet.Width / animation.TotalFrames;
            drawInfo.Source = new Rectangle(frameWidth * animation.CurrentFrame, 0, frameWidth, drawInfo.SpriteSheet.Height);
        }
        
        var layerDepth = DrawLayerDepth.GetLayerDepth(drawInfo.Layer);
        var destination = new Rectangle(position.CurrentLocation.ToPoint(), drawInfo.Size);
        batch.Draw(drawInfo.SpriteSheet, destination, drawInfo.Source, drawInfo.Color, 0, Vector2.Zero, SpriteEffects.None, layerDepth);
    }
}