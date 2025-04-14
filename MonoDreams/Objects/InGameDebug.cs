using DefaultEcs;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component.Debug;
using MonoDreams.Renderer;

namespace MonoDreams.Objects;

public class InGameDebug
{
    public static Entity Create(World world, SpriteBatch batch, ViewportManager renderer)
    {
        var entity = world.CreateEntity();
        entity.Set(new DebugUI(new RenderTarget2D(batch.GraphicsDevice, renderer.ScreenWidth, renderer.ScreenHeight)));
        return entity;
    }
}