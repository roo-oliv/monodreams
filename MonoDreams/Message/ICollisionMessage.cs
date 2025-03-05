using DefaultEcs;
using Microsoft.Xna.Framework;

namespace MonoDreams.Message;

public interface ICollisionMessage
{
        public Entity BaseEntity { get; }
        public Entity CollidingEntity { get; }
        public Vector2 ContactPoint { get; }
        public Vector2 ContactNormal { get; }
        public float ContactTime { get; }
        public int Layer { get; }
}