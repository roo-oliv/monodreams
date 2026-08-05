using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.State;
using MonoDreams.System.Camera;
using Xunit;

namespace MonoDreams.Tests.Camera;

/// Protects the camera premise "`CameraLookAt` overrides the DESIRED POSITION only — and bypasses the
/// leash": while a look-at is live, <see cref="CameraFollowSystem"/> aims at the look-at subject instead
/// of the active follow target, but everything else about the follow is unchanged — the ACTIVE FOLLOW
/// TARGET still owns the damping and the <c>Bounds</c> clamp (applied to the desired position before
/// smoothing), the system still needs an active target and a camera entity to do anything, and it still
/// writes only the camera ENTITY's Transform (<see cref="CameraSyncSystem"/> stays the sole writer of the
/// <see cref="MonoDreams.Component.Camera"/> adapter). The one thing a look-at DOES suspend is the
/// <c>MaxDistanceX/Y</c> leash, so a cutaway to a distant subject is not metered a few pixels per frame.
/// A dead subject falls back to the follow target on its own; <c>Release: true</c> restores normal follow,
/// leash included.
///
/// Each test runs follow → sync and asserts the resolved adapter position. Where a test needs more than
/// one tick it reuses ONE <see cref="CameraFollowSystem"/> / <see cref="CameraSyncSystem"/> pair, because
/// the look-at state lives on the follow system instance (it is fed by the <c>CameraLookAt</c> message,
/// so the system must exist before the message is published).
public class CameraLookAtTests
{
    // A one-second tick: with the high damping below, exponential smoothing resolves essentially onto
    // the aim point this frame, isolating whichever clamp (leash / bounds) is under test.
    private static GameState OneSecondTick() =>
        new(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

    /// <summary>Creates the scene camera ENTITY at <paramref name="position"/> (CM) — the thing
    /// <see cref="CameraFollowSystem"/> eases and <see cref="CameraSyncSystem"/> copies into the adapter.</summary>
    private static Entity CameraEntity(World world, Vector2 position)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(position));
        e.Set(new CameraComponent());
        return e;
    }

    /// <summary>The ACTIVE FOLLOW TARGET: it keeps owning the damping, the leash and the bounds even
    /// while a look-at is aiming the camera somewhere else.</summary>
    private static Entity Target(
        World world,
        Vector2 position,
        float damping = 1000f,
        float maxDistance = 100000f,
        Rectangle? bounds = null)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(position));
        e.Set(new CameraFollowTargetComponent
        {
            DampingX = damping,
            DampingY = damping,
            MaxDistanceX = maxDistance,
            MaxDistanceY = maxDistance,
            Bounds = bounds,
        });
        return e;
    }

    /// <summary>A look-at subject: a plain entity carrying nothing but a Transform.</summary>
    private static Entity Subject(World world, Vector2 position)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(position));
        return e;
    }

    /// <summary>One follow tick then one sync tick on fresh systems (no look-at can be live).</summary>
    private static void FollowThenSync(World world, MonoDreams.Component.Camera camera)
    {
        using var follow = new CameraFollowSystem(world);
        using var sync = new CameraSyncSystem(world, camera);
        follow.Update(OneSecondTick());
        sync.Update(OneSecondTick());
    }

    /// <summary>One follow tick then one sync tick on the SAME systems, so look-at state survives ticks.</summary>
    private static void Tick(CameraFollowSystem follow, CameraSyncSystem sync)
    {
        follow.Update(OneSecondTick());
        sync.Update(OneSecondTick());
    }

    [Fact]
    public void LookAt_EasesTowardSubject_BypassingTheLeash()
    {
        // Control: with no look-at live, a tight leash meters the move to 10px per frame — so the leash
        // really is wired up, and the look-at case below cannot land on 5000 by accident.
        {
            using var world = new World();
            var camera = new MonoDreams.Component.Camera(800, 600);
            CameraEntity(world, Vector2.Zero);
            Target(world, new Vector2(5000, 5000), maxDistance: 10f);

            FollowThenSync(world, camera);

            Assert.Equal(10f, camera.Position.X, 3);
            Assert.Equal(10f, camera.Position.Y, 3);
        }

        // With a look-at live, the camera aims at the SUBJECT and the leash is bypassed entirely: it
        // arrives this frame instead of crawling 10px toward it.
        {
            using var world = new World();
            var camera = new MonoDreams.Component.Camera(800, 600);
            CameraEntity(world, Vector2.Zero);
            Target(world, Vector2.Zero, maxDistance: 10f); // active target sits on the camera
            var subject = Subject(world, new Vector2(5000, 5000));

            using var follow = new CameraFollowSystem(world);
            using var sync = new CameraSyncSystem(world, camera);
            world.Publish(new CameraLookAt(subject));
            Tick(follow, sync);

            Assert.Equal(5000f, camera.Position.X, 1);
            Assert.Equal(5000f, camera.Position.Y, 1);
        }
    }

    [Fact]
    public void LookAt_KeepsDampingAndBounds_FromTheActiveFollowTarget()
    {
        // Bounds: the look-at only replaces the aim POINT — the active follow target's Bounds still
        // clamp the desired position before smoothing, so a subject far outside the rectangle is
        // clamped to its edges. Note the tight leash here lands nowhere near the result (it would have
        // produced 10/10), which is the bypass showing through a second time.
        {
            using var world = new World();
            var camera = new MonoDreams.Component.Camera(800, 600);
            CameraEntity(world, Vector2.Zero);
            var bounds = new Rectangle(-200, -100, 400, 200); // edges at ±200, ±100
            Target(world, Vector2.Zero, maxDistance: 10f, bounds: bounds);
            var subject = Subject(world, new Vector2(5000, 5000));

            using var follow = new CameraFollowSystem(world);
            using var sync = new CameraSyncSystem(world, camera);
            world.Publish(new CameraLookAt(subject));
            Tick(follow, sync);

            Assert.Equal(200f, camera.Position.X, 3);
            Assert.Equal(100f, camera.Position.Y, 3);
        }

        // Damping: the subject's arrival is still EASED, and the easing rate comes from the active
        // follow target, not from the subject (which carries no follow component at all).
        {
            using var world = new World();
            var camera = new MonoDreams.Component.Camera(800, 600);
            CameraEntity(world, Vector2.Zero);
            // ln(2) per second ⇒ a one-second tick resolves ~50% toward the aim point.
            Target(world, Vector2.Zero, damping: 0.6931472f);
            var subject = Subject(world, new Vector2(1000, 0));

            using var follow = new CameraFollowSystem(world);
            using var sync = new CameraSyncSystem(world, camera);
            world.Publish(new CameraLookAt(subject));
            Tick(follow, sync);

            Assert.Equal(500f, camera.Position.X, 3);
            Assert.Equal(0f, camera.Position.Y, 3);
        }
    }

    [Fact]
    public void LookAt_DeadSubject_FallsBackToFollowTarget()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600);
        CameraEntity(world, Vector2.Zero);
        Target(world, new Vector2(100, 0));
        var subject = Subject(world, new Vector2(5000, 0));

        using var follow = new CameraFollowSystem(world);
        using var sync = new CameraSyncSystem(world, camera);
        world.Publish(new CameraLookAt(subject));
        Tick(follow, sync);

        Assert.Equal(5000f, camera.Position.X, 1); // aiming at the subject

        subject.Dispose();
        Tick(follow, sync); // same system instances: the look-at is still "set", but its subject is gone

        // No Release was published — a dead subject falls back to the normal follow target on its own,
        // so a subject that despawns mid-cutaway can never strand the camera.
        Assert.Equal(100f, camera.Position.X, 3);
        Assert.Equal(0f, camera.Position.Y, 3);
    }

    [Fact]
    public void LookAt_Release_RestoresNormalFollow_WithLeash()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600);
        CameraEntity(world, Vector2.Zero);
        Target(world, new Vector2(100, 0), maxDistance: 10f);
        var subject = Subject(world, new Vector2(5000, 0));

        using var follow = new CameraFollowSystem(world);
        using var sync = new CameraSyncSystem(world, camera);
        world.Publish(new CameraLookAt(subject));
        Tick(follow, sync);

        Assert.Equal(5000f, camera.Position.X, 1); // leash bypassed on the way out

        world.Publish(new CameraLookAt(subject, Release: true));
        Tick(follow, sync);

        // Normal follow semantics are fully restored — including the leash, which meters the trip back
        // to the follow target at 10px this frame (4990) instead of snapping to 100.
        Assert.Equal(4990f, camera.Position.X, 1);
        Assert.Equal(0f, camera.Position.Y, 3);
    }

    [Fact]
    public void LookAt_WritesTheCameraEntityOnly_AdapterUnchangedUntilSync()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600);
        var sentinel = new Vector2(-777, -777);
        camera.Position = sentinel; // any adapter write by the follow system will show up as a change
        var start = new Vector2(30, 40);
        var cameraEntity = CameraEntity(world, start);
        Target(world, Vector2.Zero);
        var subject = Subject(world, new Vector2(5000, 5000));

        using var follow = new CameraFollowSystem(world);
        using var sync = new CameraSyncSystem(world, camera);
        world.Publish(new CameraLookAt(subject));

        follow.Update(OneSecondTick()); // follow ONLY — no sync this time

        // The follow system moved the camera ENTITY (inspectable, saveable) and did not touch the
        // adapter: CameraSyncSystem is the only writer of the adapter in Play, look-at included.
        var entityPosition = cameraEntity.Get<TransformComponent>().Position;
        Assert.Equal(sentinel, camera.Position);
        Assert.NotEqual(start, entityPosition);
        Assert.Equal(5000f, entityPosition.X, 1);
        Assert.Equal(5000f, entityPosition.Y, 1);

        sync.Update(OneSecondTick());

        Assert.Equal(entityPosition, camera.Position);
    }
}
