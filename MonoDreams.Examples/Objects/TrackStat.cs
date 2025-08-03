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
    public static Entity Create(World world, StatType statType, BitmapFont font, Vector2 position, RenderTargetID renderTarget, Color? labelColor = null, Color? statColor = null)
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
            TextContent = "0",
            Font = font,
            Color = statColor ?? Color.White,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
            Scale = 0.3f
        });
        
        var label = world.CreateEntity();
        label.Set(new Transform(position + new Vector2(0, -20))); // Position above the stat
        label.Set(new Visible());
        label.Set(new DynamicText
        {
            Target = renderTarget,
            LayerDepth = 0.9f,
            TextContent = GetStatLabel(statType),
            Font = font,
            Color = labelColor ?? Color.White,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue,
            Scale = 0.15f // Smaller scale for label
        });

        return entity;
    }

    private static string GetStatLabel(StatType statType)
    {
        return statType switch
        {
            StatType.TopSpeed => "Top Speed",
            StatType.LapTime => "Lap Time",
            StatType.OvertakingSpots => "Overtaking Spots",
            StatType.Score => "Score",
            _ => "Unknown Stat"
        };
    }
}
