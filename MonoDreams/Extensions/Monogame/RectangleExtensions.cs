using Microsoft.Xna.Framework;

namespace MonoDreams.Extensions.Monogame;

public static class RectangleExtensions
{
    public static Vector2 Dimension(this Rectangle rectangle)
    {
        return rectangle.Size.ToVector2();
    }
    
    public static Vector2 Origin(this Rectangle rectangle)
    {
        return rectangle.Location.ToVector2();
    }

    public static Rectangle AtPosition(this Rectangle rectangle, Vector2 position)
    {
        return new Rectangle(rectangle.Location + position.ToPoint(), rectangle.Size);
    }
}