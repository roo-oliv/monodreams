using DefaultEcs;
using Microsoft.Xna.Framework;

namespace MonoDreams.Message;

/// <summary>
/// Minimum contact contract any collision message must satisfy. Under the colliders-as-entities
/// model a contact names four entities: the two collider entities (<see cref="ColliderA"/>/
/// <see cref="ColliderB"/> — where the shape and identity live) and the two resolved bodies
/// (<see cref="BodyA"/>/<see cref="BodyB"/> — where physics/resolution act). A is the initiator.
/// </summary>
public interface ICollisionMessage
{
        /// <summary>The initiating collider entity (the shape that moved into the contact).</summary>
        public Entity ColliderA { get; }
        /// <summary>The other collider entity.</summary>
        public Entity ColliderB { get; }
        /// <summary>The body owning <see cref="ColliderA"/> (see <c>ColliderBody.Resolve</c>) — the
        /// resolution write-back target.</summary>
        public Entity BodyA { get; }
        /// <summary>The body owning <see cref="ColliderB"/>.</summary>
        public Entity BodyB { get; }
        /// <summary>
        /// Point of contact between the two colliders. Exact for swept-AABB (box-vs-box) collisions.
        /// For SAT (convex) collisions, this is a centroid-midpoint approximation.
        /// </summary>
        public Vector2 ContactPoint { get; }
        public Vector2 ContactNormal { get; }
        public float ContactTime { get; }
        public float PenetrationDepth { get; }
        public int Layer { get; }
}
