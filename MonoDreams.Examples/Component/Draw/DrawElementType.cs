namespace MonoDreams.Examples.Component.Draw;

/// <summary>
/// Defines the type of drawable element.
/// </summary>
public enum DrawElementType
{
    /// <summary>Standard sprite/texture rendering via SpriteBatch.</summary>
    Sprite,
    /// <summary>Text rendering via SpriteFont.</summary>
    Text,
    /// <summary>Nine-patch scalable UI element rendering.</summary>
    NinePatch,
    /// <summary>Vertex buffer mesh rendering via BasicEffect.</summary>
    Mesh
}