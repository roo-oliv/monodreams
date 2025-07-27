using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Draw;

// This system ONLY updates the state of the text, doesn't prepare drawing
[With(typeof(DynamicText))] // Ensures entities have DynamicText
public sealed class TextUpdateSystem : AEntitySetSystem<GameState>
{
    public TextUpdateSystem(World world) : base(world) { }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var text = ref entity.Get<DynamicText>();

        if (text.IsRevealed || text.RevealingSpeed <= 0)
        {
            text.VisibleCharacterCount = text.TextContent?.Length ?? 0;
            text.IsRevealed = true; // Ensure flag is set
            return; // No update needed if already revealed or instant reveal
        }

        // Initialize start time if not set
        if (float.IsNaN(text.RevealStartTime))
        {
            text.RevealStartTime = state.TotalTime; // Assuming GameState has TotalTime in seconds
        }

        float elapsedTime = state.TotalTime - text.RevealStartTime;
        int targetVisibleChars = (int)Math.Floor(elapsedTime * text.RevealingSpeed);
        int maxChars = text.TextContent?.Length ?? 0;

        text.VisibleCharacterCount = Math.Max(0, Math.Min(targetVisibleChars, maxChars));

        if (text.VisibleCharacterCount >= maxChars)
        {
            text.IsRevealed = true;
        }
    }
}