using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.UI;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

public class IguinaSystem(World world)
    : AComponentSystem<GameState, IguinaInterface>(world)
{
    protected override void Update(GameState gameState, ref IguinaInterface iguinaInterface)
    {
        var ui = iguinaInterface.UserInterface;
        foreach (var (text, paragraph) in iguinaInterface.DynamicParagraphs)
        {
            var index = (int)Math.Min(gameState.TotalTime * 25, text.Length);
            paragraph.Text = text[..index];
        }
        // update input and ui system
        var input = (ui.Input as IguinaInput)!;
        input.StartFrame(gameState.GameTime.current);
        ui.Update((float)gameState.GameTime.current.ElapsedGameTime.TotalSeconds);
        input.EndFrame();
        
        var renderer = (ui.Renderer as IguinaRenderer)!;
        renderer.StartFrame();
        ui.Draw();
        renderer.EndFrame();
    }
}