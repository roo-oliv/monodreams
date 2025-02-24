using DefaultEcs;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Debug;
using MonoDreams.Examples.Level.Levels;
using MonoDreams.Level;
using MonoDreams.Objects;
using MonoDreams.Renderer;

namespace MonoDreams.Examples.Level;

public class LevelLoader(
    World world,
    GraphicsDevice graphicsDevice,
    ContentManager content,
    SpriteBatch spriteBatch,
    (RenderTarget2D main, RenderTarget2D ui) renderTargets) : ALevelLoader(world)
{
    public override void LoadLevel(int index)
    {
        CurrentLevel = index;
        ILevel level = index switch
        {
            0 => new Level0(content, graphicsDevice, spriteBatch, renderTargets),
            // 1 => new Level1(content, Renderer),
            _ => new Level0(content, graphicsDevice, spriteBatch, renderTargets),
        };
        level.Load(World);
    }
}