using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Draw;
using MonoDreams.State;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.System.Draw;

public sealed class TextSystem(SpriteBatch batch, World world)
    : AEntitySetSystem<GameState>(world.GetEntities().With<SimpleText>().With<Transform>().AsSet(), true)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var text = ref entity.Get<SimpleText>();
        ref var position = ref entity.Get<Transform>();
        var layerDepth = DrawLayerDepth.GetLayerDepth(text.DrawLayer);
        batch.DrawString(
            text.Font,
            text.Value,
            position.CurrentPosition,
            text.Color,
            0.0f,
            text.Origin, 
            Vector2.One, 
            SpriteEffects.None,
            layerDepth);
    }
}