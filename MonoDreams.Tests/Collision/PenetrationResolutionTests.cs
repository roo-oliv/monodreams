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
/// Protects the overlap-vs-sweep dispatch in box-vs-box resolution: a body that STARTS the frame
/// already inside a collider is depenetrated along the minimum-translation vector (the shortest of
/// the four exits), never solved as a sweep. A swept solve whose start is inside the target returns a
/// NEGATIVE contact time, and its contact point then sits arbitrarily far back along the motion —
/// proportional to the TARGET's size — so overlap against one big merged collider hurls the body
/// across it and out the far side. Separated and strictly-touching contacts (<c>CollisionRect</c>
/// uses strict bounds) still take the sweep path, unchanged.
///
/// All the arithmetic below is exact: <c>BoxColliderComponent</c> boxes are CENTERED on the entity's
/// world position, the test <c>GameState</c> has dt = 1s (so a velocity of v moves the body v units
/// per frame), and the pipeline is the reference order
/// Velocity → Detection → Resolution → Commit.
/// </summary>
public class PenetrationResolutionTests
{
    private static GameState Play() => new(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1))) { RunMode = RunMode.Play };

    private static CollisionMessage Create(Entity colliderA, Entity colliderB, Entity bodyA, Entity bodyB,
        Vector2 contactPoint, Vector2 contactNormal, float contactTime, float penetrationDepth, int layer)
        => new(colliderA, colliderB, bodyA, bodyB, contactPoint, contactNormal, contactTime, penetrationDepth, layer, CollisionType.Physics);

    // ─── Overlap exits by the nearest face, in all four directions ──────────────

    /// <summary>
    /// A 20×20 body sits INSIDE a 200×200 passive wall centred at the origin (world rect
    /// (-100,-100)…(100,100)), past the wall's midpoint on one axis, still moving in the direction
    /// that drove it in. The nearest exit is therefore the face AHEAD of the motion, while a swept
    /// solve back-projects along the motion and would fling the body out the opposite face — 360
    /// units for a 400-unit round trip across a 200-wide wall. Depenetration takes the short exit.
    ///
    /// Per case (startX,startY) + (velX,velY) · dt(1s) = the position detection sees, then the MTV:
    ///  · right : (40,0)+(20,0)   → centre (60,0)   rect (50,-10)…(70,10)   → pushRight = 100-50 = 50  → (110,0)
    ///  · left  : (-40,0)+(-20,0) → centre (-60,0)  rect (-70,-10)…(-50,10) → pushLeft  = -50+100 = 50 → (-110,0)
    ///  · down  : (0,40)+(0,20)   → centre (0,60)   rect (-10,50)…(10,70)   → pushDown  = 100-50 = 50  → (0,110)
    ///  · up    : (0,-40)+(0,-20) → centre (0,-60)  rect (-10,-70)…(10,-50) → pushUp    = -50+100 = 50 → (0,-110)
    /// In every case the competing exits are 170 (the far face) and 110 (either perpendicular face),
    /// so the 50-unit exit wins, landing the body's box edge EXACTLY on the wall's face (the "right"
    /// case ends at centre 110, so its left edge is 110-10 = 100 == the wall's right edge), i.e.
    /// touching, not overlapping — and touching does not intersect, so nothing moves afterwards.
    /// </summary>
    [Theory]
    [InlineData(40f, 0f, 20f, 0f, 110f, 0f)]      // biased toward the wall's RIGHT face → exits +X
    [InlineData(-40f, 0f, -20f, 0f, -110f, 0f)]   // biased toward the wall's LEFT face  → exits -X
    [InlineData(0f, 40f, 0f, 20f, 0f, 110f)]      // biased toward the wall's BOTTOM face → exits +Y
    [InlineData(0f, -40f, 0f, -20f, 0f, -110f)]   // biased toward the wall's TOP face    → exits -Y
    public void OverlappingBody_ExitsByTheNearestFace(
        float startX, float startY, float velX, float velY, float expectedX, float expectedY)
    {
        using var world = new World();
        using var runner = new DefaultParallelRunner(1);
        // Detection FIRST so its component-added subscription auto-tags the colliders created below.
        var detect = new TransformCollisionDetectionSystem<CollisionMessage>(world, Create);

        // The body is its own collider (standalone): collider == body.
        var body = world.CreateEntity();
        body.Set(new TransformComponent(new Vector2(startX, startY)));
        body.Set(new VelocityComponent(new Vector2(velX, velY)));
        body.Set(new BoxColliderComponent(new Vector2(20, 20)));

        // One big passive wall — the "merged terrain rectangle" shape from the defect.
        var wall = world.CreateEntity();
        wall.Set(new TransformComponent(Vector2.Zero));
        wall.Set(new BoxColliderComponent(new Vector2(200, 200), passive: true));

        var velocity = new TransformVelocitySystem(world, runner);
        var resolve = new TransformPhysicalCollisionResolutionSystem(world);
        var commit = new TransformCommitSystem(world, runner);
        var play = Play();
        for (var i = 0; i < 5; i++) { velocity.Update(play); detect.Update(play); resolve.Update(play); commit.Update(play); }

        var pos = body.Get<TransformComponent>().Position;
        // Cleared of the overlap by exactly the MTV on the first resolve, then stable (the velocity on
        // that axis was zeroed, so later frames produce no movement and no contact).
        Assert.Equal(expectedX, pos.X, 3);
        Assert.Equal(expectedY, pos.Y, 3);
    }

    // ─── Regression shape from the defect: knocked deep into a big collider ─────

    /// <summary>
    /// The reported shape: a knockback carries the body past the midpoint of one big merged terrain
    /// rectangle and it keeps moving that way. Wall 400×400 at the origin → rect (-200,-200)…(200,200);
    /// body 20×20 starting at (120,0) with velocity (30,0) → detection sees centre (150,0), rect
    /// (140,-10)…(160,10). Exits: right 200-140 = 60, left 160+200 = 360, up/down 210 each → the body
    /// pops out the near (right) face at centre 150+60 = 210.
    ///
    /// A swept solve instead back-projects along +X to the expanded target's left face
    /// (-200 - 20/2 = -210): a 360-unit correction that lands the body 420 units from where it should
    /// be, on the far side of the wall. That is the teleport this test pins down.
    /// </summary>
    [Fact]
    public void OverlappingBody_WithVelocityIntoTheWall_ExitsNearestFace_NotThroughTheFarSide()
    {
        using var world = new World();
        using var runner = new DefaultParallelRunner(1);
        var detect = new TransformCollisionDetectionSystem<CollisionMessage>(world, Create); // before entities → auto-tag

        var body = world.CreateEntity();
        body.Set(new TransformComponent(new Vector2(120, 0)));
        body.Set(new VelocityComponent(new Vector2(30, 0)));
        body.Set(new BoxColliderComponent(new Vector2(20, 20)));

        var wall = world.CreateEntity();
        wall.Set(new TransformComponent(Vector2.Zero));
        wall.Set(new BoxColliderComponent(new Vector2(400, 400), passive: true));

        var velocity = new TransformVelocitySystem(world, runner);
        var resolve = new TransformPhysicalCollisionResolutionSystem(world);
        var commit = new TransformCommitSystem(world, runner);
        var play = Play();
        for (var i = 0; i < 6; i++) { velocity.Update(play); detect.Update(play); resolve.Update(play); commit.Update(play); }

        var pos = body.Get<TransformComponent>().Position;
        Assert.Equal(210f, pos.X, 3);   // cleared by the 60-unit MTV, body left edge == wall right edge (200)
        Assert.Equal(0f, pos.Y, 3);     // the perpendicular axis is untouched
        // Explicitly: outside the wall on the biased side, and nowhere near the far side (-210).
        Assert.True(pos.X > 200f, $"body must end outside the wall's right face; X={pos.X}");
        Assert.InRange(pos.X, 200f, 260f);
        // The depenetration axis' velocity is killed, so the body does not re-enter next frame.
        Assert.Equal(0f, body.Get<VelocityComponent>().Current.X, 3);
    }

    // ─── Touching is not overlapping: resting contacts keep the sweep path ──────

    /// <summary>
    /// A body exactly TOUCHING a wall face does not intersect it (<c>CollisionRect</c> compares with
    /// strict <c>&lt;</c>/<c>&gt;</c>), so it never reaches depenetration. Wall 200×200 at the origin
    /// → rect (-100,-100)…(100,100); body 20×20 at centre (-110,0) → rect (-120,-10)…(-100,10), whose
    /// right edge is exactly the wall's left edge (-100). At rest, the position must be bit-identical
    /// after any number of frames — no jitter, no push-off.
    /// </summary>
    [Fact]
    public void TouchingBodyAtRest_IsNotDepenetrated_AndDoesNotJitter()
    {
        using var world = new World();
        using var runner = new DefaultParallelRunner(1);
        var detect = new TransformCollisionDetectionSystem<CollisionMessage>(world, Create); // before entities → auto-tag

        var body = world.CreateEntity();
        body.Set(new TransformComponent(new Vector2(-110, 0)));
        body.Set(new VelocityComponent(Vector2.Zero));
        body.Set(new BoxColliderComponent(new Vector2(20, 20)));

        var wall = world.CreateEntity();
        wall.Set(new TransformComponent(Vector2.Zero));
        wall.Set(new BoxColliderComponent(new Vector2(200, 200), passive: true));

        var velocity = new TransformVelocitySystem(world, runner);
        var resolve = new TransformPhysicalCollisionResolutionSystem(world);
        var commit = new TransformCommitSystem(world, runner);
        var play = Play();
        for (var i = 0; i < 8; i++) { velocity.Update(play); detect.Update(play); resolve.Update(play); commit.Update(play); }

        Assert.Equal(new Vector2(-110, 0), body.Get<TransformComponent>().Position);
    }

    // ─── The normal approach sweep is unchanged ─────────────────────────────────

    /// <summary>
    /// The ordinary case, mirroring <c>ColliderEntityTests.Resolution_CorrectsTheBody_NotTheColliderChild</c>
    /// with a standalone body/collider: a 16×16 body approaching a 16×400 wall from a distance is
    /// blocked AT the wall face by the swept solve, not carried through it. Wall at the origin → rect
    /// (-8,-200)…(8,200); the resting centre is therefore -8 - 16/2 = -16, and the swept solve places
    /// the body's collider centre exactly on that contact point (the expanded target's left face,
    /// -8 - 16/2 = -16). Reached from (-200,0) at 20 units/frame.
    /// </summary>
    [Fact]
    public void ApproachingBody_IsBlockedAtTheWallFace_SweepPathUnchanged()
    {
        using var world = new World();
        using var runner = new DefaultParallelRunner(1);
        var detect = new TransformCollisionDetectionSystem<CollisionMessage>(world, Create); // before entities → auto-tag

        var body = world.CreateEntity();
        body.Set(new TransformComponent(new Vector2(-200, 0)));
        body.Set(new VelocityComponent(new Vector2(20, 0)));
        body.Set(new BoxColliderComponent(new Vector2(16, 16)));

        var wall = world.CreateEntity();
        wall.Set(new TransformComponent(Vector2.Zero));
        wall.Set(new BoxColliderComponent(new Vector2(16, 400), passive: true));

        var velocity = new TransformVelocitySystem(world, runner);
        var resolve = new TransformPhysicalCollisionResolutionSystem(world);
        var commit = new TransformCommitSystem(world, runner);
        var play = Play();
        for (var i = 0; i < 14; i++) { velocity.Update(play); detect.Update(play); resolve.Update(play); commit.Update(play); }

        var pos = body.Get<TransformComponent>().Position;
        Assert.Equal(-16f, pos.X, 3);  // blocked at the wall's left face, never past it
        Assert.Equal(0f, pos.Y, 3);
        Assert.True(pos.X < -8f, $"body must stop before the wall's left edge; X={pos.X}");
    }
}
