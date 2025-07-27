using Flecs.NET.Core;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using DynamicText = MonoDreams.Examples.Component.Draw.DynamicText;
using static MonoDreams.Examples.System.SystemPhase;

namespace MonoDreams.Examples.System.Draw;

public static class TextPrepSystem
{
    public static void Register(World world)
    {
        world.System<DynamicText, Position, Visible, DrawComponent>()
            .Kind(Ecs.OnUpdate)
            .Kind(DrawPhase)
            .Each((ref DynamicText text, ref Position position, ref Visible visible, ref DrawComponent drawComponent) =>
            {
                if (text.VisibleCharacterCount <= 0 || string.IsNullOrEmpty(text.TextContent))
                {
                    return; // Nothing to draw
                }

                var layerDepth = text.LayerDepth;
                var visibleText = text.TextContent[..text.VisibleCharacterCount];

                // Update the DrawComponent
                drawComponent.Type = DrawElementType.Text;
                drawComponent.Target = text.Target;
                drawComponent.Text = visibleText;
                drawComponent.Font = text.Font;
                drawComponent.Position = position.Current;
                drawComponent.Color = text.Color;
                drawComponent.LayerDepth = layerDepth;
                drawComponent.Size = text.Font.MeasureString(visibleText);
            });
    }
}