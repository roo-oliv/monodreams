using Microsoft.Xna.Framework;

namespace MonoDreams.Component
{
    public class DynamicBody
    {
        public bool IsRiding { get; set; }
        public bool IsJumping { get; set; }
        public Vector2 Acceleration;

        public DynamicBody()
        {
            IsRiding = false;
            IsJumping = false;
            Acceleration = Vector2.Zero;
        }
    }
}