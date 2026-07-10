using DefaultEcs;
using MonoDreams.Component;
using MonoDreams.Component.Physics;

namespace MonoDreams.System.Collision;

/// <summary>
/// Resolves the <b>body</b> a collider entity belongs to — the single source of truth shared by
/// detection, resolution, and message construction under the colliders-as-entities model.
///
/// <para>A collider is its own entity (a <see cref="MonoDreams.Component.Collision.ColliderTagComponent"/>-
/// tagged shape with its own <see cref="TransformComponent"/>). Its body is the nearest
/// <see cref="ChildOfComponent"/> ancestor (walking up, including the collider entity itself)
/// carrying a <see cref="RigidBodyComponent"/>; failing that, the nearest carrying a
/// <see cref="VelocityComponent"/>; failing that, the collider entity itself. A standalone collider
/// (no physics ancestor) is therefore its own body — static geometry and trigger zones reduce to
/// today's behaviour where collider == body.</para>
///
/// <para><b>Why RigidBody wins over Velocity regardless of distance:</b> the <c>else</c> is a
/// fallback, not a nearest-wins race — a body is a physics concept, and <c>RigidBodyComponent</c>
/// is the strongest signal of "this is the physical body". Resolution write-back targets the body's
/// Transform/Velocity, so picking the physics owner keeps corrections on the mover, never on a
/// collider child that would drift inside its parent (pre-mortem #1).</para>
/// </summary>
public static class ColliderBody
{
    // Defensive cap against a malformed cyclic ChildOf graph (hierarchies are shallow trees).
    private const int MaxWalk = 128;

    /// <summary>Returns the body entity for <paramref name="collider"/> per the class contract.</summary>
    public static Entity Resolve(Entity collider)
    {
        var node = collider;
        var velocityFallback = default(Entity);
        var haveVelocity = false;

        for (var depth = 0; depth < MaxWalk; depth++)
        {
            if (node.Has<RigidBodyComponent>()) return node; // nearest RigidBody wins outright
            if (!haveVelocity && node.Has<VelocityComponent>())
            {
                velocityFallback = node;
                haveVelocity = true;
            }

            if (!node.Has<ChildOfComponent>()) break;
            var parent = node.Get<ChildOfComponent>().Parent;
            if (!parent.IsAlive) break;
            node = parent;
        }

        return haveVelocity ? velocityFallback : collider;
    }
}
