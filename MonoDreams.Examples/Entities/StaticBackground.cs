using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Objects;
using MonoDreams.Renderer;

namespace MonoDreams.Examples.Entities;

public static class StaticBackground
{
    public static Entity Create(World world, Texture2D image, Camera camera, ResolutionIndependentRenderer renderer)
    {
        Point size = image.Width / image.Height < renderer.VirtualWidth / renderer.VirtualHeight
            ? new Point(renderer.VirtualWidth/2, (int) (renderer.VirtualWidth / (float) image.Width * image.Height)/2)
            : new Point((int) (renderer.VirtualHeight / (float) image.Height * image.Width)/2, renderer.VirtualHeight/2);
        Vector2 position = camera.Position - new Vector2(size.X / 2, size.Y / 2);
        var entity = StaticImage.Create(world, position, image, size, new Rectangle(0, 0, renderer.VirtualHeight, renderer.VirtualWidth));
        return entity;
    }
}