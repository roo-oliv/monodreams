using DefaultEcs;
using Microsoft.Xna.Framework;

namespace MonoDreams.Message;

public interface ICollisionMessage
{
        public Entity BaseEntity { get; }
        public Entity CollidingEntity { get; }
        /// <summary>
        /// Point of contact between the two entities. Exact for swept-AABB (BoxCollider) collisions.
        /// For SAT (ConvexCollider) collisions, this is a centroid-midpoint approximation.
        /// </summary>
        public Vector2 ContactPoint { get; }
        public Vector2 ContactNormal { get; }
        public float ContactTime { get; }
        public float PenetrationDepth { get; }
        public int Layer { get; }
}