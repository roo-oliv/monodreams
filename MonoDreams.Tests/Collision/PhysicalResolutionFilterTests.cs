using System;
using DefaultEcs;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Physics;
using MonoDreams.Message;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Collision;
using MonoDreams.System.Physics;
using Xunit;

namespace MonoDreams.Tests.Collision;

/// <summary>
/// Pins what <see cref="TransformPhysicalCollisionResolutionSystem"/> actually gates on, because the
/// module docs used to claim otherwise (issue #82 review): it is a subclass whose ONLY override admits
/// a message when <c>CollisionMessage.Type == CollisionType.Physics</c>. The gate is the message TYPE —
/// which the game stamps in its <c>CreateCollisionMessageDelegate</c> and which defaults to
/// <see cref="CollisionType.Generic"/> — not the components the colliding bodies carry, and the
/// resolution math is the base class's positional correction: no impulse, and
/// <c>RigidBodyComponent.Mass</c> is never read (nothing in the collision module reads
/// <c>RigidBodyComponent</c> outside <c>ColliderBody.Resolve</c>'s body walk).
///
/// Geometry is the one already proven by <see cref="PenetrationResolutionTests"/>: a 20×20 body at
/// (40,0) moving +X at 20/frame is inside a 200×200 passive wall centred at the origin, so a RESOLVED
/// run depenetrates out the near face and parks at X=110, while an IGNORED run just keeps integrating
/// velocity and sails through the wall (40 + 5×20 = 140). dt is 1s, and the pipeline is the reference
/// order Velocity → Detection → Resolution → Commit.
/// </summary>
public class PhysicalResolutionFilterTests
{
    private const float ResolvedX = 110f;   // depenetrated: body's left edge lands on the wall's right face
    private const float SailedThroughX = 140f; // never corrected: 5 frames of free flight at 20/frame

    /// <summary>
    /// A fully equipped physics body — <see cref="RigidBodyComponent"/> AND
    /// <see cref="VelocityComponent"/>, deep inside a wall — is still ignored unless the contact is
    /// typed <see cref="CollisionType.Physics"/>. This is the half of the old doc claim that read
    /// "acts only on bodies carrying RigidBody + Velocity": carrying them is neither here nor there.
    /// </summary>
    [Theory]
    [InlineData(CollisionType.Physics, ResolvedX)]
    [InlineData(CollisionType.Generic, SailedThroughX)]      // the DEFAULT type — untagged contacts are dropped
    [InlineData(CollisionType.Collectible, SailedThroughX)]
    [InlineData(CollisionType.Dialogue, SailedThroughX)]
    public void Resolution_IsGatedOnMessageType_NotOnTheBodysPhysicsComponents(CollisionType type, float expectedX)
        => Assert.Equal(expectedX, RunOverlap(type, rigidBody: new RigidBodyComponent()), 3);

    /// <summary>
    /// The other half: a <see cref="CollisionType.Physics"/> contact resolves identically whether or
    /// not the body carries a <see cref="RigidBodyComponent"/>, and whatever its
    /// <see cref="RigidBodyComponent.Mass"/> — a 1000× mass difference moves the body by exactly the
    /// same MTV, because resolution never reads mass.
    /// </summary>
    [Theory]
    [InlineData(false, 0f)]      // no RigidBodyComponent at all (the shape every PenetrationResolutionTests body has)
    [InlineData(true, 1f)]
    [InlineData(true, 1000f)]
    public void PhysicsContact_ResolvesTheSame_WithoutRigidBody_AndAtAnyMass(bool withRigidBody, float mass)
        => Assert.Equal(ResolvedX, RunOverlap(CollisionType.Physics, withRigidBody ? new RigidBodyComponent(mass) : null), 3);

    /// <summary>
    /// Runs the reference pipeline for 5 frames with every contact classified as <paramref name="type"/>,
    /// and returns the body's final X.
    /// </summary>
    private static float RunOverlap(CollisionType type, RigidBodyComponent? rigidBody)
    {
        using var world = new World();
        using var runner = new DefaultParallelRunner(1);

        // Detection FIRST so its component-added subscription auto-tags the colliders created below.
        var detect = new TransformCollisionDetectionSystem<CollisionMessage>(world,
            (colliderA, colliderB, bodyA, bodyB, contactPoint, contactNormal, contactTime, penetrationDepth, layer)
                => new CollisionMessage(colliderA, colliderB, bodyA, bodyB, contactPoint, contactNormal,
                    contactTime, penetrationDepth, layer, type));

        // The body is its own collider (standalone): collider == body.
        var body = world.CreateEntity();
        body.Set(new TransformComponent(new Vector2(40, 0)));
        body.Set(new VelocityComponent(new Vector2(20, 0)));
        body.Set(new BoxColliderComponent(new Vector2(20, 20)));
        if (rigidBody is not null) body.Set(rigidBody);

        var wall = world.CreateEntity();
        wall.Set(new TransformComponent(Vector2.Zero));
        wall.Set(new BoxColliderComponent(new Vector2(200, 200), passive: true));

        var velocity = new TransformVelocitySystem(world, runner);
        var resolve = new TransformPhysicalCollisionResolutionSystem(world);
        var commit = new TransformCommitSystem(world, runner);
        var play = new GameState(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1))) { RunMode = RunMode.Play };
        for (var i = 0; i < 5; i++) { velocity.Update(play); detect.Update(play); resolve.Update(play); commit.Update(play); }

        return body.Get<TransformComponent>().Position.X;
    }
}
