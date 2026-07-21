using System;
using System.Collections.Generic;
using System.Diagnostics;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Physics;
using MonoDreams.Extension;
using MonoDreams.Extensions.Monogame;
using MonoDreams.Message;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Collision;
using MonoDreams.System.Physics;
using DefaultEcs.Threading;
using Xunit;
using Xunit.Abstractions;

namespace MonoDreams.Tests.Collision;

/// <summary>
/// Protects the colliders-as-entities (CE-A) model: a collider is its own entity; its body is
/// resolved via <see cref="ColliderBody"/>; world shapes derive from the collider entity's own
/// transform (even under a moved/scaled parent); resolution corrects the BODY (never the collider
/// child — pre-mortem #1); the message carries both the collider and body granularities; and a body
/// with multiple collider children resolves without exploding. Plus a coarse, non-gating perf smoke.
/// </summary>
public class ColliderEntityTests
{
    private readonly ITestOutputHelper _out;
    public ColliderEntityTests(ITestOutputHelper output) => _out = output;

    private static GameState Play() => new(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1))) { RunMode = RunMode.Play };

    private static CollisionMessage Create(Entity colliderA, Entity colliderB, Entity bodyA, Entity bodyB,
        Vector2 contactPoint, Vector2 contactNormal, float contactTime, float penetrationDepth, int layer)
        => new(colliderA, colliderB, bodyA, bodyB, contactPoint, contactNormal, contactTime, penetrationDepth, layer, CollisionType.Physics);

    // ─── Body resolution matrix ────────────────────────────────────────────────

    [Fact]
    public void Resolve_StandaloneCollider_IsItsOwnBody()
    {
        using var world = new World();
        var e = world.CreateEntity();
        e.Set(new TransformComponent());
        e.Set(new BoxColliderComponent(new Vector2(4, 4)));
        Assert.Equal(e, ColliderBody.Resolve(e));
    }

    [Fact]
    public void Resolve_ColliderChildOfRigidBody_ResolvesToTheRigidBody()
    {
        using var world = new World();
        var body = world.CreateEntity();
        body.Set(new TransformComponent());
        body.Set(new RigidBodyComponent());
        var collider = world.CreateEntity();
        collider.Set(new TransformComponent());
        collider.Set(new BoxColliderComponent(new Vector2(4, 4)));
        collider.SetParent(body);
        Assert.Equal(body, ColliderBody.Resolve(collider));
    }

    [Fact]
    public void Resolve_ColliderChildOfVelocityOnlyBody_ResolvesToThatBody()
    {
        using var world = new World();
        var body = world.CreateEntity();
        body.Set(new TransformComponent());
        body.Set(new VelocityComponent());
        var collider = world.CreateEntity();
        collider.Set(new TransformComponent());
        collider.Set(new ConvexColliderComponent(new[] { new Vector2(0, 0), new Vector2(2, 0), new Vector2(1, 2) }));
        collider.SetParent(body);
        Assert.Equal(body, ColliderBody.Resolve(collider));
    }

    [Fact]
    public void Resolve_RigidBodyAncestorWinsOverNearerVelocityAncestor()
    {
        using var world = new World();
        var rigid = world.CreateEntity();          // grandparent: RigidBody
        rigid.Set(new TransformComponent());
        rigid.Set(new RigidBodyComponent());
        var velocity = world.CreateEntity();        // parent: Velocity only (nearer)
        velocity.Set(new TransformComponent());
        velocity.Set(new VelocityComponent());
        velocity.SetParent(rigid);
        var collider = world.CreateEntity();
        collider.Set(new TransformComponent());
        collider.Set(new BoxColliderComponent(new Vector2(4, 4)));
        collider.SetParent(velocity);
        // RigidBody wins outright even though the Velocity body is nearer.
        Assert.Equal(rigid, ColliderBody.Resolve(collider));
    }

    [Fact]
    public void Resolve_ColliderChildOfPlainParent_FallsBackToItself()
    {
        using var world = new World();
        var plain = world.CreateEntity(); // no physics
        plain.Set(new TransformComponent());
        var collider = world.CreateEntity();
        collider.Set(new TransformComponent());
        collider.Set(new BoxColliderComponent(new Vector2(4, 4)));
        collider.SetParent(plain);
        // No physics anywhere up the chain → the collider is its own body (a trigger zone).
        Assert.Equal(collider, ColliderBody.Resolve(collider));
    }

    // ─── Child-collider world shape under a moved/scaled parent (extends PF-G) ───

    [Fact]
    public void BoxChildCollider_WorldRect_ScalesAndFollowsAMovedParent()
    {
        using var world = new World();
        var root = world.CreateEntity();
        root.Set(new TransformComponent(new Vector2(100, 100), 0f, new Vector2(2, 2))); // scale 2
        var child = world.CreateEntity();
        child.Set(new TransformComponent(new Vector2(5, 0)));
        child.Set(new BoxColliderComponent(new Vector2(10, 20)));
        child.SetParent(root);

        var box = child.Get<BoxColliderComponent>();
        var rect = SATCollision.BoxWorldRect(box, child.Get<TransformComponent>());
        // WorldPosition = (5*2+100, 0*2+100) = (110,100); WorldScale = 2 → size (20,40).
        Assert.Equal(new Vector2(100, 80), rect.Position);
        Assert.Equal(new Vector2(20, 40), rect.Size);

        // Move the parent: the child's world rect follows (the lazy WorldMatrix re-walks on the
        // parent's dirty flag — no HierarchySystem needed for a one-level child).
        root.Get<TransformComponent>().Position = new Vector2(200, 100);
        var moved = SATCollision.BoxWorldRect(box, child.Get<TransformComponent>());
        Assert.Equal(new Vector2(200, 80), moved.Position); // centre (210,100) - (10,20)
    }

    [Fact]
    public void ConvexChildCollider_WorldVertices_FollowAMovedParent()
    {
        using var world = new World();
        var root = world.CreateEntity();
        root.Set(new TransformComponent(new Vector2(100, 0)));
        var child = world.CreateEntity();
        child.Set(new TransformComponent(Vector2.Zero));
        child.Set(new ConvexColliderComponent(new[]
        {
            new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1),
        }));
        child.SetParent(root);

        var convex = child.Get<ConvexColliderComponent>();
        convex.UpdateWorldVertices(child.Get<TransformComponent>());
        Assert.Equal(new Vector2(99, -1), convex.WorldVertices[0]); // model + parent world pos (100,0)

        root.Get<TransformComponent>().Position = new Vector2(150, 0);
        convex.UpdateWorldVertices(child.Get<TransformComponent>());
        Assert.Equal(new Vector2(149, -1), convex.WorldVertices[0]);
    }

    // ─── Resolution corrects the BODY, never the collider child (pre-mortem #1) ──

    [Fact]
    public void Resolution_CorrectsTheBody_NotTheColliderChild()
    {
        using var world = new World();
        using var runner = new DefaultParallelRunner(1);
        // Detection FIRST so its component-added subscription auto-tags the colliders created below.
        var detect = new TransformCollisionDetectionSystem<CollisionMessage>(world, Create);

        // A body moving right, with a collider CHILD centered on it.
        var body = world.CreateEntity();
        body.Set(new TransformComponent(new Vector2(0, 0)));
        body.Set(new VelocityComponent(new Vector2(20, 0)));
        var collider = world.CreateEntity();
        collider.Set(new TransformComponent(Vector2.Zero));
        collider.Set(new BoxColliderComponent(new Vector2(16, 16)));
        collider.SetParent(body);

        // A static wall to the right (its own body, passive).
        var wall = world.CreateEntity();
        wall.Set(new TransformComponent(new Vector2(60, 0)));
        wall.Set(new BoxColliderComponent(new Vector2(16, 400), passive: true));

        var velocity = new TransformVelocitySystem(world, runner);
        var resolve = new TransformPhysicalCollisionResolutionSystem(world);
        var commit = new TransformCommitSystem(world, runner);
        var play = Play();
        for (var i = 0; i < 8; i++) { velocity.Update(play); detect.Update(play); resolve.Update(play); commit.Update(play); }

        // The BODY was corrected (blocked before the wall's left edge at x=52) …
        var bodyX = body.Get<TransformComponent>().Position.X;
        Assert.True(bodyX > 0 && bodyX < 52, $"body should advance then be blocked before the wall; X={bodyX}");
        // … and the collider child NEVER drifted inside its parent (its LOCAL position is untouched).
        Assert.Equal(Vector2.Zero, collider.Get<TransformComponent>().Position);
    }

    // ─── The message carries both the collider and body granularities ───────────

    [Fact]
    public void CollisionMessage_CarriesColliderAndBody_ForBothSides()
    {
        using var world = new World();
        using var runner = new DefaultParallelRunner(1);
        var detect = new TransformCollisionDetectionSystem<CollisionMessage>(world, Create); // before entities → auto-tag

        var body = world.CreateEntity();
        body.Set(new TransformComponent(new Vector2(0, 0)));
        body.Set(new VelocityComponent(new Vector2(20, 0)));
        var collider = world.CreateEntity();
        collider.Set(new TransformComponent(Vector2.Zero));
        collider.Set(new BoxColliderComponent(new Vector2(16, 16)));
        collider.SetParent(body);

        var wall = world.CreateEntity(); // standalone collider: its own body
        wall.Set(new TransformComponent(new Vector2(40, 0)));
        wall.Set(new BoxColliderComponent(new Vector2(16, 400), passive: true));

        var hits = new List<CollisionMessage>();
        world.Subscribe((in CollisionMessage m) => hits.Add(m));

        var velocity = new TransformVelocitySystem(world, runner);
        var commit = new TransformCommitSystem(world, runner);
        var play = Play();
        for (var i = 0; i < 4 && hits.Count == 0; i++) { velocity.Update(play); detect.Update(play); commit.Update(play); }

        Assert.NotEmpty(hits);
        var hit = hits.Find(m => m.ColliderA == collider);
        Assert.Equal(collider, hit.ColliderA); // initiator collider (the moving child)
        Assert.Equal(body, hit.BodyA);          // resolved to the moving body
        Assert.Equal(wall, hit.ColliderB);      // the wall collider
        Assert.Equal(wall, hit.BodyB);          // standalone → its own body
    }

    // ─── Multi-collider body: two collider children, one frame, no explosion ────

    [Fact]
    public void TwoColliderBody_BothChildrenContact_ResolvesWithoutExploding()
    {
        using var world = new World();
        using var runner = new DefaultParallelRunner(1);
        var detect = new TransformCollisionDetectionSystem<CollisionMessage>(world, Create); // before entities → auto-tag

        var body = world.CreateEntity();
        body.Set(new TransformComponent(new Vector2(0, 0)));
        body.Set(new VelocityComponent(new Vector2(20, 0)));
        // Two collider children stacked vertically — both sweep into the same wall.
        foreach (var oy in new[] { -20f, 20f })
        {
            var c = world.CreateEntity();
            c.Set(new TransformComponent(new Vector2(0, oy)));
            c.Set(new BoxColliderComponent(new Vector2(10, 10)));
            c.SetParent(body);
        }

        var wall = world.CreateEntity();
        wall.Set(new TransformComponent(new Vector2(40, 0)));
        wall.Set(new BoxColliderComponent(new Vector2(10, 200), passive: true));

        var velocity = new TransformVelocitySystem(world, runner);
        var resolve = new TransformPhysicalCollisionResolutionSystem(world);
        var commit = new TransformCommitSystem(world, runner);
        var play = Play();
        for (var i = 0; i < 10; i++) { velocity.Update(play); detect.Update(play); resolve.Update(play); commit.Update(play); }

        // Sequential per-message correction re-validates, so two contacts on one body do not
        // double-correct into oblivion: the body stops near the wall, finite and un-flung.
        var pos = body.Get<TransformComponent>().Position;
        Assert.True(float.IsFinite(pos.X) && float.IsFinite(pos.Y), $"body position must be finite, was {pos}");
        Assert.InRange(pos.X, 0f, 40f);
    }

    // ─── Coarse, non-gating perf smoke (RFC criterion) ──────────────────────────

    [Fact]
    public void PerfSmoke_ManyConvexColliders_OneDetectionPass_Completes()
    {
        const int count = 500;
        using var world = new World();
        var detect = new TransformCollisionDetectionSystem<CollisionMessage>(world, Create); // before entities → auto-tag
        var rng = new Random(1234);
        for (var i = 0; i < count; i++)
        {
            var e = world.CreateEntity();
            e.Set(new TransformComponent(new Vector2(rng.Next(0, 2000), rng.Next(0, 2000))));
            e.Set(new ConvexColliderComponent(new[]
            {
                new Vector2(-5, -5), new Vector2(5, -5), new Vector2(5, 5), new Vector2(-5, 5),
            }));
        }

        var play = Play();
        detect.Update(play); // warm the grid + sets

        var sw = Stopwatch.StartNew();
        detect.Update(play);
        sw.Stop();

        _out.WriteLine($"[CE-A perf smoke] one detection pass over {count} convex colliders: {sw.Elapsed.TotalMilliseconds:F2} ms");
        // Non-gating: a generous ceiling that only catches a catastrophic regression.
        Assert.True(sw.Elapsed.TotalMilliseconds < 2000, $"detection pass took {sw.Elapsed.TotalMilliseconds:F2} ms");
    }
}
