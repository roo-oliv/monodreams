using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Objects;
using MonoDreams.Renderer;

namespace MonoDreams.Examples.Entities;

public static class StaticBackground
{
    public static Entity Create(World world, Texture2D image, Camera camera, ResolutionIndependentRenderer renderer, float scale = 1, Enum drawLayer = null)
    {
        int stretchFactor = image.Width / image.Height < renderer.VirtualWidth / renderer.VirtualHeight
            ? (int)Math.Ceiling((double)(renderer.VirtualWidth / image.Width))
            : (int)Math.Ceiling((double)(renderer.VirtualHeight / image.Height));
        Vector2 position = camera.Position - new Vector2(renderer.VirtualWidth / 2, renderer.VirtualHeight / 2);
        var size = new Point((int) (image.Width * stretchFactor * scale), (int) (image.Height * stretchFactor * scale));
        var source = new Rectangle(0, 0, image.Width * stretchFactor, image.Height * stretchFactor);
        var entity = StaticImage.Create(world, position, image, size, source, new Color(Color.Gray, 0.5f), drawLayer);
        return entity;
    }
}