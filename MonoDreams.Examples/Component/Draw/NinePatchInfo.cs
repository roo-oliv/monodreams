using Microsoft.Xna.Framework;

namespace MonoDreams.Examples.Component.Draw;

public readonly struct NinePatchInfo(
    int borderSize,
    Rectangle topLeft,
    Rectangle top,
    Rectangle topRight,
    Rectangle left,
    Rectangle center,
    Rectangle right,
    Rectangle bottomLeft,
    Rectangle bottom,
    Rectangle bottomRight)
{
    public int BorderSize { get; } = borderSize;
    public Rectangle TopLeft { get; } = topLeft;
    public Rectangle Top { get; } = top;
    public Rectangle TopRight { get; } = topRight;
    public Rectangle Left { get; } = left;
    public Rectangle Center { get; } = center;
    public Rectangle Right { get; } = right;
    public Rectangle BottomLeft { get; } = bottomLeft;
    public Rectangle Bottom { get; } = bottom;
    public Rectangle BottomRight { get; } = bottomRight;
}
