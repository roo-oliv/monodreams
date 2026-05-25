using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoDreams.Util;

public static class Texture2DExtensions
{
    private static readonly Dictionary<Texture2D, Color[]> TextureDataCache = new();
    public static Texture2D Crop(this Texture2D sourceTexture, Rectangle sourceBounds, GraphicsDevice graphicsDevice)
    {
        // Check if the source texture data is already cached
        if (!TextureDataCache.TryGetValue(sourceTexture, out var sourceData))
        {
            // Get color data from the source texture and cache it
            sourceData = new Color[sourceTexture.Width * sourceTexture.Height];
            sourceTexture.GetData(sourceData);
            TextureDataCache[sourceTexture] = sourceData;
        }

        // Create a new texture with the dimensions of the source rectangle
        var croppedTexture = new Texture2D(graphicsDevice, sourceBounds.Width, sourceBounds.Height);

        // Create array for the cropped data
        var croppedData = new Color[sourceBounds.Width * sourceBounds.Height];

        // Copy the relevant portion of the texture
        for (var y = 0; y < sourceBounds.Height; y++)
        {
            for (var x = 0; x < sourceBounds.Width; x++)
            {
                var sourceIndex = (y + sourceBounds.Y) * sourceTexture.Width + (x + sourceBounds.X);
                var targetIndex = y * sourceBounds.Width + x;
                croppedData[targetIndex] = sourceData[sourceIndex];
            }
        }

        // Set the data in the new texture
        croppedTexture.SetData(croppedData);

        return croppedTexture;
    }
}