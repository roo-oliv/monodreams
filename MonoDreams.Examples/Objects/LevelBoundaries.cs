using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Renderer;

namespace MonoDreams.Examples.Objects;

public static class LevelBoundaries
{
    public static void Create(World world, Texture2D square, ViewportManager renderer, RenderTarget2D renderTarget)
    {
        Tile.Create(
            world, square,
            new Vector2(-renderer.VirtualWidth / 2, -renderer.VirtualHeight / 2 - 1000), new Point(renderer.VirtualWidth, 1000),
            renderTarget,
            TileType.Deadly);
        Tile.Create(
            world, square,
            new Vector2(-renderer.VirtualWidth / 2, renderer.VirtualHeight / 2), new Point(renderer.VirtualWidth, 1000),
            renderTarget,
            TileType.Deadly);
        Tile.Create(
            world, square,
            new Vector2(-renderer.VirtualWidth / 2 - 1000, -renderer.VirtualHeight / 2), new Point(1000, renderer.VirtualHeight),
            renderTarget,
            TileType.Deadly);
        Tile.Create(
            world, square,
            new Vector2(renderer.VirtualWidth / 2, -renderer.VirtualHeight / 2), new Point(1000, renderer.VirtualHeight),
            renderTarget,
            TileType.Deadly);
    }
}