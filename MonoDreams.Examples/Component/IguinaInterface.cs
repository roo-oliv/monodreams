using Iguina;
using Iguina.Entities;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.UI;

namespace MonoDreams.Examples.Component;

public class IguinaInterface
{
    public readonly UISystem UserInterface;
    public List<(string text, Paragraph paragraph)> DynamicParagraphs;

    public IguinaInterface(string assetsPath, ContentManager content, SpriteBatch spriteBatch, GraphicsDevice graphicsDevice)
    {
        DynamicParagraphs = [];
        var renderer = new IguinaRenderer(content, graphicsDevice, spriteBatch, assetsPath);
        var input = new IguinaInput();
        UserInterface = new UISystem(Path.Combine(assetsPath, "system_style.json"), renderer, input);
    }
}