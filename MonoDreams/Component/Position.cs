using Microsoft.Xna.Framework;

namespace MonoDreams.Component
{
    public struct Position
    {
        public Point DiscreteValue;
        public Vector2 TrueValue;
        public Vector2 LastValue;

        public Position(float x, float y)
        {
            DiscreteValue = new Point((int)x, (int)y);
            TrueValue = new Vector2(x, y);
            LastValue = new Vector2(x, y);
        }
    }
}