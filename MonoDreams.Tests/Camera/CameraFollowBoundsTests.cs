using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.State;
using MonoDreams.System.Camera;
using Xunit;

namespace MonoDreams.Tests.Camera;

/// Protects the camera premises "Follow bounds clamp the target before smoothing" and
/// "CameraFollowSystem eases the camera ENTITY; CameraSyncSystem copies it into the adapter" (CM).
/// The follow system now writes the camera ENTITY's Transform; the sync system copies that pose into the
/// shared <see cref="MonoDreams.Component.Camera"/> adapter, so each test runs follow → sync and asserts
/// the resolved adapter position.
public class CameraFollowBoundsTests
{
    // A one-second tick with high damping makes the exponential smoothing resolve
    // essentially onto the target this frame, isolating the bounds clamp.
    private static GameState OneSecondTick() =>
        new(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

    /// <summary>Creates the scene camera ENTITY at <paramref name="position"/> (CM) — the thing
    /// <see cref="CameraFollowSystem"/> eases and <see cref="CameraSyncSystem"/> copies into the adapter.</summary>
    private static void CameraEntity(World world, Vector2 position)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(position));
        e.Set(new CameraComponent());
    }

    private static Entity Target(World world, Vector2 position, Rectangle? bounds)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(position));
        e.Set(new CameraFollowTargetComponent
        {
            DampingX = 1000f,
            DampingY = 1000f,
            MaxDistanceX = 100000f,
            MaxDistanceY = 100000f,
            Bounds = bounds,
        });
        return e;
    }

    /// <summary>Runs one follow tick then one sync tick, so the adapter reflects the eased camera entity.</summary>
    private static void FollowThenSync(World world, MonoDreams.Component.Camera camera)
    {
        using var follow = new CameraFollowSystem(world);
        using var sync = new CameraSyncSystem(world, camera);
        follow.Update(OneSecondTick());
        sync.Update(OneSecondTick());
    }

    [Fact]
    public void TargetOutsideBounds_ClampsCameraToBoundsEdge()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600);
        CameraEntity(world, Vector2.Zero);
        var bounds = new Rectangle(-200, -100, 400, 200); // edges at ±200, ±100
        Target(world, new Vector2(5000, 5000), bounds);

        FollowThenSync(world, camera);

        Assert.Equal(200f, camera.Position.X, 3);
        Assert.Equal(100f, camera.Position.Y, 3);
    }

    [Fact]
    public void TargetInsideBounds_FollowsTargetUnclamped()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600);
        CameraEntity(world, Vector2.Zero);
        var bounds = new Rectangle(-200, -100, 400, 200);
        Target(world, new Vector2(50, 25), bounds);

        FollowThenSync(world, camera);

        Assert.Equal(50f, camera.Position.X, 3);
        Assert.Equal(25f, camera.Position.Y, 3);
    }

    [Fact]
    public void CameraOutsideBounds_EasesBackInsteadOfSnappingToEdge()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600);
        CameraEntity(world, new Vector2(1000, 0)); // the camera entity starts outside the bounds
        var bounds = new Rectangle(-200, -100, 400, 200); // edge at +200
        var target = world.CreateEntity();
        target.Set(new TransformComponent(Vector2.Zero)); // target sits inside bounds
        target.Set(new CameraFollowTargetComponent
        {
            // ln(2) per second ⇒ a one-second tick resolves ~50% toward the target.
            DampingX = 0.6931472f,
            DampingY = 0.6931472f,
            MaxDistanceX = 100000f,
            MaxDistanceY = 100000f,
            Bounds = bounds,
        });

        FollowThenSync(world, camera);

        // Clamping the target (not the resolved position) means the camera eases from
        // 1000 toward 0 and lands partway (~500) — still outside the +200 edge this
        // frame. A post-smoothing clamp would have hard-snapped it to exactly 200.
        Assert.True(camera.Position.X > 200f,
            $"expected smooth easing past the edge, but camera snapped to {camera.Position.X}");
        Assert.True(camera.Position.X < 1000f,
            $"expected the camera to move toward the target, got {camera.Position.X}");
    }

    [Fact]
    public void NoBounds_FollowsTargetFreely()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600);
        CameraEntity(world, Vector2.Zero);
        Target(world, new Vector2(5000, 5000), bounds: null);

        FollowThenSync(world, camera);

        Assert.Equal(5000f, camera.Position.X, 1);
        Assert.Equal(5000f, camera.Position.Y, 1);
    }
}
