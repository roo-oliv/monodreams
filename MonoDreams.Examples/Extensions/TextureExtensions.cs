using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoDreams.Examples.Extensions;

public static class TextureExtensions
{
    public static Texture2D Crop(this Texture2D texture, Rectangle sourceRectangle, GraphicsDevice graphicsDevice)
    {
        // Create a new texture with the dimensions of the source rectangle
        Texture2D croppedTexture = new Texture2D(graphicsDevice, sourceRectangle.Width, sourceRectangle.Height);
        
        // Extract the pixel data from the source texture
        Color[] data = new Color[texture.Width * texture.Height];
        texture.GetData(data);
        
        // Create an array for the cropped data
        Color[] croppedData = new Color[sourceRectangle.Width * sourceRectangle.Height];
        
        // Copy the pixel data from the source rectangle to the cropped data array
        for (int y = 0; y < sourceRectangle.Height; y++)
        {
            for (int x = 0; x < sourceRectangle.Width; x++)
            {
                int sourceIndex = (y + sourceRectangle.Y) * texture.Width + (x + sourceRectangle.X);
                int destIndex = y * sourceRectangle.Width + x;
                
                // Make sure we don't go out of bounds
                if (sourceIndex >= 0 && sourceIndex < data.Length && 
                    destIndex >= 0 && destIndex < croppedData.Length)
                {
                    croppedData[destIndex] = data[sourceIndex];
                }
            }
        }
        
        // Set the cropped data to the new texture
        croppedTexture.SetData(croppedData);
        
        return croppedTexture;
    }
} 