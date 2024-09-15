using DefaultEcs;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component.Debug;
using MonoDreams.Renderer;

namespace MonoDreams.Examples.Objects;

public class Debug
{
    public static Entity Create(World world, SpriteBatch batch, ResolutionIndependentRenderer renderer)
    {
        var entity = world.CreateEntity();
        entity.Set(new DebugUI(new RenderTarget2D(batch.GraphicsDevice, renderer.ScreenWidth, renderer.ScreenHeight)));
        return entity;
    }
}