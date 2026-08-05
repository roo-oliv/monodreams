using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.System.Cursor;
using Xunit;
using CursorFactory = MonoDreams.Cursor.Cursor;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.Cursor;

/// <summary>
/// Protects the cursor premise "<c>SkipDerivation</c> lets an injection channel own the cursor's
/// derived positions": the mouse half of input replay. <c>CursorInputSystem.SkipHardwareRead</c>
/// already lets a replay / editor-op channel INJECT cursor state, but
/// <see cref="CursorPositionSystem"/> then re-derives screen→virtual→world every frame and
/// overwrites that injection (with <c>OutsideViewport = true</c> whenever the injected
/// <c>ScreenPosition</c> is not a real in-viewport mouse coordinate). With
/// <c>SkipDerivation = true</c> the derivation stands down and the injected frame survives.
/// Pure logic — no GraphicsDevice: the real <see cref="ViewportManager"/> never dereferences its
/// <c>Game</c> and <see cref="GameCamera"/> is a plain matrix adapter.
/// </summary>
public class CursorPositionSystemTests
{
    private static GameState Frame() => new(new GameTime());

    /// <summary>An 800×600 virtual surface on an 800×600 window: no letterbox, scale 1, so a
    /// screen position inside the window maps to the identical virtual position (and anything
    /// outside maps to <c>null</c> — the un-mapped case the injection channel authors).</summary>
    private static ViewportManager Viewport() =>
        new(null, 800, 600) { ScreenWidth = 800, ScreenHeight = 600 };

    /// <summary>The real cursor factory (the module's contract entry point), on the mesh path so
    /// the entity needs no texture asset. Composes controller + input + transform + DrawComponent,
    /// which is exactly the set <see cref="CursorPositionSystem"/> queries.</summary>
    private static Entity MakeCursor(World world, Vector2 hotSpot) =>
        CursorFactory.CreateMesh(world, Triangle(), RenderTargetID.HUD, CursorType.Default, hotSpot);

    private static MeshData Triangle() => new(
        [
            new VertexPositionColor(new Vector3(0, 0, 0), Color.White),
            new VertexPositionColor(new Vector3(8, 0, 0), Color.White),
            new VertexPositionColor(new Vector3(0, 8, 0), Color.White),
        ],
        [0, 1, 2]);

    // ── The issue contract: an injected cursor position survives the frame ──

    /// <summary>The load-bearing case. A replay / editor-op channel injects Virtual + World
    /// positions, <c>OutsideViewport = false</c>, and the cursor transform, while
    /// <c>ScreenPosition</c> is NOT a mappable window coordinate (the channel authors world-space
    /// intent, not a mouse pixel). With <c>SkipDerivation = true</c> the system early-returns
    /// before touching camera or viewport, so every injected value is still there after the
    /// frame.</summary>
    [Fact]
    public void SkipDerivation_InjectedCursorState_SurvivesTheFrame()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var cursor = MakeCursor(world, hotSpot: new Vector2(3, 5));

        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(-5000, -5000); // un-mappable: derivation would flag Outside
        input.VirtualPosition = new Vector2(123, 45);
        input.WorldPosition = new Vector2(777, -321);
        input.OutsideViewport = false;
        cursor.Get<TransformComponent>().Position = new Vector2(11, 22);

        using var position = new CursorPositionSystem(world, camera, Viewport()) { SkipDerivation = true };
        position.Update(Frame());

        var after = cursor.Get<CursorInputComponent>();
        Assert.Equal(new Vector2(123, 45), after.VirtualPosition);
        Assert.Equal(new Vector2(777, -321), after.WorldPosition);
        Assert.False(after.OutsideViewport); // NOT clobbered to true by the un-mapped screen position
        Assert.Equal(new Vector2(11, 22), cursor.Get<TransformComponent>().Position);
    }

    // ── The contrast: without the flag, the derivation clobbers exactly those fields ──

    /// <summary>Why the flag has to exist: with derivation live, the un-mapped injected
    /// <c>ScreenPosition</c> maps to <c>null</c>, so the system marks the pointer outside the
    /// viewport — the state every world-space consumer reads as "ignore this click".</summary>
    [Fact]
    public void WithoutSkipDerivation_UnmappedScreenPosition_ClobbersInjectionWithOutsideViewport()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var cursor = MakeCursor(world, hotSpot: Vector2.Zero);

        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(-5000, -5000);
        input.VirtualPosition = new Vector2(123, 45);
        input.WorldPosition = new Vector2(777, -321);
        input.OutsideViewport = false;

        using var position = new CursorPositionSystem(world, camera, Viewport());
        position.Update(Frame());

        Assert.True(cursor.Get<CursorInputComponent>().OutsideViewport);
    }

    /// <summary>And with a mappable <c>ScreenPosition</c> the derivation overwrites the injected
    /// Virtual/World positions and the transform outright — the same one-line early return is what
    /// keeps the injected frame intact above. (HUD target ⇒ transform = virtual + hot spot.)</summary>
    [Fact]
    public void WithoutSkipDerivation_MappedScreenPosition_RecomputesVirtualWorldAndTransform()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var cursor = MakeCursor(world, hotSpot: new Vector2(3, 5));

        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = new Vector2(100, 100); // inside the un-letterboxed 800×600 viewport
        input.VirtualPosition = new Vector2(123, 45);
        input.WorldPosition = new Vector2(777, -321);
        cursor.Get<TransformComponent>().Position = new Vector2(11, 22);

        using var position = new CursorPositionSystem(world, camera, Viewport());
        position.Update(Frame());

        var after = cursor.Get<CursorInputComponent>();
        Assert.False(after.OutsideViewport);
        Assert.Equal(new Vector2(100, 100), after.VirtualPosition);
        Assert.Equal(camera.VirtualScreenToWorld(new Vector2(100, 100)), after.WorldPosition);
        Assert.NotEqual(new Vector2(777, -321), after.WorldPosition);
        Assert.Equal(new Vector2(103, 105), cursor.Get<TransformComponent>().Position);
    }
}
