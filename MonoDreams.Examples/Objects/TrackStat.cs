using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Draw;
using MonoGame.Extended.BitmapFonts;
using DynamicText = MonoDreams.Examples.Component.Draw.DynamicText;

namespace MonoDreams.Examples.Objects;

public static class TrackStat
{
    public static Entity Create(World world, StatType statType, BitmapFont font, Vector2 position, RenderTargetID renderTarget)
    {
        var entity = world.CreateEntity();

        // Add basic components
        entity.Set(new Transform(position));
        entity.Set(new Visible());
        entity.Set(new StatComponent(statType));

        // Add dynamic text component that will be revealed instantly
        entity.Set(new DynamicText
        {
            Target = renderTarget,
            LayerDepth = 0.9f,
            TextContent = GetStatLabel(statType) + ": 0",
            Font = font,
            Color = Color.White,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
            Scale = 0.2f // Smaller scale for stats
        });

        return entity;
    }

    private static string GetStatLabel(StatType statType)
    {
        return statType switch
        {
            StatType.MaxSpeed => "Top Speed",
            StatType.MinSpeed => "Lowest Speed",
            StatType.AverageSpeed => "Average Speed",
            _ => "Unknown Stat"
        };
    }
}
