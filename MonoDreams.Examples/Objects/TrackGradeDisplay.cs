using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Draw;
using MonoGame.Extended.BitmapFonts;
using DynamicText = MonoDreams.Examples.Component.Draw.DynamicText;

namespace MonoDreams.Examples.Objects;

public static class TrackGradeDisplay
{
    public static Entity Create(World world, BitmapFont font, Vector2 position, RenderTargetID renderTarget, Color? labelColor = null)
    {
        var displays = new Dictionary<TrackScoreComponent.Grade, (Entity label, Entity threshold)>();
        
        var padding = 150f;
        var startingPosition = position;
        var labelPadding = new Vector2(0, -20);
        foreach (var grade in Enum.GetValues(typeof(TrackScoreComponent.Grade)))
        {
            startingPosition -= new Vector2(padding, 0);
            
            var label = world.CreateEntity();
            label.Set(new Transform(startingPosition + labelPadding));
            label.Set(new Visible());
            label.Set(new DynamicText
            {
                Target = renderTarget,
                LayerDepth = 0.9f,
                TextContent = grade?.ToString() ?? "",
                Font = font,
                Color = labelColor ?? Color.DarkSlateGray,
                IsRevealed = true,
                VisibleCharacterCount = int.MaxValue,
                Scale = 0.15f // Smaller scale for label
            });
            label.Set(new LevelEntity());
            
            var threshold = world.CreateEntity();
            threshold.Set(new Transform(startingPosition));
            threshold.Set(new Visible());
            threshold.Set(new DynamicText
            {
                Target = renderTarget,
                LayerDepth = 0.9f,
                TextContent = "N/A",
                Font = font,
                Color = labelColor ?? Color.DarkSlateGray,
                IsRevealed = true,
                VisibleCharacterCount = int.MaxValue,
                Scale = 0.2f
            });
            threshold.Set(new LevelEntity());
            
            displays[(TrackScoreComponent.Grade)grade] = (label, threshold);
        }
        
        
        var entity = world.CreateEntity();
        entity.Set(new TrackGradeDisplayComponent(displays));
        entity.Set(new LevelEntity());
        return entity;
    }
}
