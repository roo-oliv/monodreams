using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.State;
using MonoDreams.System.Camera;
using Xunit;

namespace MonoDreams.Tests.Camera;

/// Protects the camera premise "`pixelSnap` is one of two first-class pixel-art styles — the failure mode
/// is the mix": <see cref="CameraSyncSystem"/> with <c>pixelSnap: true</c> rounds the camera position to
/// whole world pixels on its way to the <see cref="MonoDreams.Component.Camera"/> adapter (the third of
/// the three snaps the hard-snap retro style needs), while the camera ENTITY's eased position stays
/// smooth and fractional. Without the flag the adapter copy is bit-exact — the byte-identical guarantee
/// for existing games and for the smooth-scroll style, which deliberately keeps a fractional camera.
public class CameraPixelSnapTests
{
    // A one-second tick: with the ln(2) damping below, exponential smoothing resolves ~50% per frame,
    // which is what produces fractional intermediate camera-entity positions.
    private static GameState OneSecondTick() =>
        new(new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

    /// <summary>Creates the scene camera ENTITY (CM) — the thing <see cref="CameraFollowSystem"/> eases
    /// and <see cref="CameraSyncSystem"/> copies into the adapter.</summary>
    private static Entity CameraEntity(World world, Vector2 position, float rotation = 0f, float zoom = 1f)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(position, rotation));
        e.Set(new CameraComponent { Zoom = zoom });
        return e;
    }

    private static Entity Target(World world, Vector2 position, float damping)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(position));
        e.Set(new CameraFollowTargetComponent
        {
            DampingX = damping,
            DampingY = damping,
            MaxDistanceX = 100000f,
            MaxDistanceY = 100000f,
        });
        return e;
    }

    /// <summary>Runs one sync tick with the given snap setting.</summary>
    private static void Sync(World world, MonoDreams.Component.Camera camera, bool pixelSnap)
    {
        using var sync = new CameraSyncSystem(world, camera, pixelSnap);
        sync.Update(OneSecondTick());
    }

    private static void AssertIntegral(float value, string what)
    {
        Assert.True(value == MathF.Round(value), $"expected {what} to be a whole world pixel, got {value}");
    }

    [Fact]
    public void PixelSnap_AdapterPositionIsIntegral_WhileEntityStaysFractional()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600);
        // A fractional pose with a non-integer zoom and a rotation: only the POSITION may be rounded.
        var cameraEntity = CameraEntity(world, new Vector2(100.4f, 57.6f), rotation: 0.35f, zoom: 2.5f);

        Sync(world, camera, pixelSnap: true);

        // The adapter copy is snapped to whole world pixels.
        Assert.Equal(100f, camera.Position.X);
        Assert.Equal(58f, camera.Position.Y);
        AssertIntegral(camera.Position.X, "camera.Position.X");
        AssertIntegral(camera.Position.Y, "camera.Position.Y");

        // The snap lives on the adapter copy ONLY — the entity's (eased, authored) position is untouched,
        // so easing stays smooth and the saved/inspected value never drifts to a rounded one.
        var transform = cameraEntity.Get<TransformComponent>();
        Assert.Equal(100.4f, transform.Position.X);
        Assert.Equal(57.6f, transform.Position.Y);

        // Rotation and zoom flow through unrounded (MathF.Round(2.5f) would be 2).
        Assert.Equal(0.35f, camera.Rotation);
        Assert.Equal(2.5f, camera.Zoom);
    }

    [Fact]
    public void PixelSnap_OverMultipleEasedFrames_AdapterAlwaysIntegral_EntitySmooth()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600);
        var cameraEntity = CameraEntity(world, Vector2.Zero);
        // ln(2) per second ⇒ a one-second tick resolves ~50% toward the target each frame. A fractional
        // target keeps every intermediate eased position fractional.
        const float targetX = 333.3f;
        const float targetY = -111.1f;
        Target(world, new Vector2(targetX, targetY), damping: 0.6931472f);

        using var follow = new CameraFollowSystem(world);
        using var sync = new CameraSyncSystem(world, camera, pixelSnap: true);

        var sawFractionalEntityPosition = false;
        for (var frame = 0; frame < 5; frame++)
        {
            follow.Update(OneSecondTick());
            sync.Update(OneSecondTick());

            // The adapter is snapped every single frame — never a fractional camera reaching the view
            // transform, which is what would make the whole world shimmer and crawl.
            AssertIntegral(camera.Position.X, $"camera.Position.X on frame {frame}");
            AssertIntegral(camera.Position.Y, $"camera.Position.Y on frame {frame}");

            // The entity is still easing, unrounded: strictly between the start and the target.
            var position = cameraEntity.Get<TransformComponent>().Position;
            Assert.True(position.X > 0f && position.X < targetX,
                $"expected the camera entity to still be easing on frame {frame}, got X={position.X}");
            Assert.True(position.Y < 0f && position.Y > targetY,
                $"expected the camera entity to still be easing on frame {frame}, got Y={position.Y}");

            if (position.X != MathF.Round(position.X) || position.Y != MathF.Round(position.Y))
                sawFractionalEntityPosition = true;
        }

        Assert.True(sawFractionalEntityPosition,
            "expected the eased camera entity position to stay fractional — the snap must not quantize the easing");
    }

    [Fact]
    public void Default_NoSnap_AdapterEqualsEntityPositionExactly()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600);
        CameraEntity(world, new Vector2(100.4f, 57.6f), rotation: 0.35f, zoom: 2.5f);

        // The DEFAULT ctor — no pixelSnap argument at all.
        using var sync = new CameraSyncSystem(world, camera);
        sync.Update(OneSecondTick());

        // Bit-exact: an existing game (and the smooth-scroll style, which wants a fractional camera)
        // renders byte-identically to before the flag existed.
        Assert.Equal(100.4f, camera.Position.X);
        Assert.Equal(57.6f, camera.Position.Y);
        Assert.Equal(new Vector2(100.4f, 57.6f), camera.Position);
        Assert.Equal(0.35f, camera.Rotation);
        Assert.Equal(2.5f, camera.Zoom);
    }

    [Fact]
    public void PixelSnap_RoundingMatchesMathFRound()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600);
        CameraEntity(world, new Vector2(2.5f, -2.5f));

        Sync(world, camera, pixelSnap: true);

        // The contract is MathF.Round (banker's rounding on exact halves) — asserted against MathF.Round
        // itself so the test documents the rounding without over-specifying it.
        Assert.Equal(MathF.Round(2.5f), camera.Position.X);
        Assert.Equal(MathF.Round(-2.5f), camera.Position.Y);
    }
}
