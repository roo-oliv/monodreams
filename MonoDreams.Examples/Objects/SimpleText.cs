using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using MonoGame.Extended.BitmapFonts;
using DynamicText = MonoDreams.Examples.Component.Draw.DynamicText;

namespace MonoDreams.Examples.Objects;

public static class SimpleText
{
    public static Entity Create(World world, string text, BitmapFont font, Vector2 position, RenderTargetID renderTarget, float scale = 0.5f, Color? color = null)
    {
        var entity = world.CreateEntity();

        // Add basic components
        entity.Set(new Transform(position));
        entity.Set(new Visible());

        // Add dynamic text component that will be revealed instantly
        entity.Set(new DynamicText
        {
            Target = renderTarget,
            LayerDepth = 0.9f,
            TextContent = text,
            Font = font,
            Color = color ?? Color.White,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
            Scale = scale
        });

        return entity;
    }
}