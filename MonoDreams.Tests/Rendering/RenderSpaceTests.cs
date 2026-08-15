using System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Renderer;
using Xunit;

namespace MonoDreams.Tests.Rendering;

/// <summary>
/// Protects the rendering premise "Authoring space and render space are distinct; the scale lives
/// only in the cameras" (issue #88). Two claims are under test:
///
/// <para><b>1. Opt-in.</b> A single-space game (no layout size, or layout == virtual) is
/// byte-identical to the pre-two-space engine: <c>RenderScale</c> is 1, the screen-space
/// <c>LayoutCamera</c>'s view matrix is exactly <see cref="Matrix.Identity"/>, and every mapping is
/// the historical one.</para>
///
/// <para><b>2. A render-resolution move costs nothing.</b> With the SAME authoring space, doubling
/// (or 1.5×-ing) the render resolution leaves every authored number where it was: the pointer still
/// maps to the same layout point, world picking still resolves to the same world point, and the
/// culling view extent is still the same world rectangle. That is the property that stops
/// coordinate-carrying tests from shattering on a resolution change.</para>
///
/// Pure CPU math — the <c>Game</c> ctor argument is never dereferenced, so tests pass null.
/// </summary>
public class RenderSpaceTests
{
    private static ViewportManager Manager(int screenWidth, int screenHeight,
        int virtualWidth, int virtualHeight, int layoutWidth = 0, int layoutHeight = 0) =>
        new(null, virtualWidth, virtualHeight, layoutWidth, layoutHeight)
        {
            ScreenWidth = screenWidth,
            ScreenHeight = screenHeight,
        };

    // ---- 1. Single-space default: nothing changed ----

    [Fact]
    public void ByDefault_AuthoringSpaceIsRenderSpace()
    {
        var vm = Manager(1600, 900, 1280, 720);

        Assert.Equal(1280, vm.LayoutWidth);
        Assert.Equal(720, vm.LayoutHeight);
        Assert.Equal(1f, vm.RenderScale);
    }

    [Fact]
    public void SingleSpace_LayoutCameraIsIdentity()
    {
        var vm = Manager(1600, 900, 1280, 720);

        // Exactly identity — which is why every screen-space pass can take the layout camera
        // instead of a null camera with zero behaviour change.
        Assert.Equal(Matrix.Identity, vm.LayoutCamera.GetViewTransformationMatrix());
        Assert.Equal(1f, vm.LayoutCamera.RenderScale);
        Assert.Equal(1280, vm.LayoutCamera.LayoutWidth);
    }

    [Fact]
    public void ExplicitEqualLayout_IsStillSingleSpace()
    {
        var vm = Manager(1600, 900, 1280, 720, 1280, 720);

        Assert.Equal(1f, vm.RenderScale);
        Assert.Equal(Matrix.Identity, vm.LayoutCamera.GetViewTransformationMatrix());
    }

    // ---- 2. Two spaces: construction + validation ----

    [Fact]
    public void TwoSpace_DerivesTheScaleFromBothResolutions()
    {
        var vm = Manager(1920, 1080, 1920, 1080, 1280, 720);

        Assert.Equal(1920, vm.VirtualWidth);
        Assert.Equal(1280, vm.LayoutWidth);
        Assert.Equal(720, vm.LayoutHeight);
        Assert.Equal(1.5f, vm.RenderScale, 4);
    }

    [Fact]
    public void MismatchedAspectRatios_Throw()
    {
        // 16:9 render space over a 4:3 authoring space would need two different scales — the one
        // thing the "the scale lives in exactly one place" invariant cannot express.
        Assert.Throws<ArgumentException>(() => Manager(1600, 900, 1920, 1080, 800, 600));

        // …but rounding a fractional scale to whole render pixels is not a mismatch: 1280×720 at
        // 1.3333 rounds to 1707×960, whose per-axis scales differ only in the fourth decimal.
        var rounded = Manager(1600, 900, 1707, 960, 1280, 720);
        Assert.Equal(1707f / 1280f, rounded.RenderScale, 4);
    }

    [Fact]
    public void NonPositiveResolutions_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Manager(1600, 900, 0, 1080));
        Assert.Throws<ArgumentOutOfRangeException>(() => Manager(1600, 900, 1920, 1080, -1, 720));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Manager(1600, 900, 1920, 1080).SetResolution(1920, 1080, 0, 720));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MonoDreams.Component.Camera(1920, 1080, renderScale: 0f));
    }

    [Fact]
    public void SetResolution_MovesRenderSpaceOnly_AndRebuildsTheLayoutCamera()
    {
        var vm = Manager(1920, 1080, 1280, 720);
        var before = vm.LayoutCamera;
        Assert.Equal(1f, before.RenderScale);

        vm.SetResolution(1920, 1080, 1280, 720);

        Assert.Equal(1280, vm.LayoutWidth);
        Assert.Equal(1.5f, vm.RenderScale, 4);
        // The camera's virtual resolution and render scale are immutable, so the manager hands out
        // a fresh one rather than a stale camera pointed at the old resolution.
        Assert.NotSame(before, vm.LayoutCamera);
        Assert.Equal(1.5f, vm.LayoutCamera.RenderScale, 4);

        // No layout argument = keep the two spaces equal (the single-space "change resolution").
        vm.SetResolution(2560, 1440);
        Assert.Equal(2560, vm.LayoutWidth);
        Assert.Equal(1f, vm.RenderScale);
    }

    // ---- 3. MapMouse: authoring coordinates, invariant to the render resolution ----

    [Fact]
    public void MapMouse_ReturnsAuthoringCoordinates_UnchangedByARenderResolutionMove()
    {
        // Same window, same authoring space, two different render resolutions.
        var single = Manager(1600, 900, 1280, 720);
        var doubled = Manager(1600, 900, 2560, 1440, 1280, 720);

        // The window is 16:9 like both spaces, so the destination rectangle fills it in both cases.
        Assert.Equal(new Rectangle(0, 0, 1600, 900), single.DestinationRectangle);
        Assert.Equal(new Rectangle(0, 0, 1600, 900), doubled.DestinationRectangle);

        foreach (var screen in new[] { new Vector2(800, 450), new Vector2(0, 0), new Vector2(1200, 225) })
        {
            var a = single.MapMouse(screen);
            var b = doubled.MapMouse(screen);
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.Equal(a!.Value.X, b!.Value.X, 3);
            Assert.Equal(a.Value.Y, b.Value.Y, 3);
        }

        // …and the values themselves are authoring-space (1280×720), not render-space.
        var centre = doubled.MapMouse(new Vector2(800, 450))!.Value;
        Assert.Equal(640f, centre.X, 3);
        Assert.Equal(360f, centre.Y, 3);
    }

    [Fact]
    public void MapMouse_InvertsThePresentDestinationRectangle_AcrossResizeAndLetterbox()
    {
        var vm = Manager(1920, 1080, 1920, 1080, 1280, 720);

        // 16:9 window: full-bleed. The last in-viewport screen pixel maps just inside the
        // authoring corner — 1919/1.5, 1079/1.5 — never to a render-space number.
        var corner = vm.MapMouse(new Vector2(1919, 1079));
        Assert.NotNull(corner);
        Assert.Equal(1919f / 1.5f, corner!.Value.X, 2);
        Assert.Equal(1079f / 1.5f, corner.Value.Y, 2);

        // Resize to a 4:3 window: the 16:9 content letterboxes to (0,105,1600,900) and the mapping
        // follows it — no bookkeeping anywhere else.
        vm.ScreenWidth = 1600;
        vm.ScreenHeight = 1200;
        var dest = vm.DestinationRectangle;
        Assert.Equal(new Rectangle(0, 150, 1600, 900), dest);

        var mapped = vm.MapMouse(new Vector2(dest.X, dest.Y));
        Assert.NotNull(mapped);
        Assert.Equal(0f, mapped!.Value.X, 2);
        Assert.Equal(0f, mapped.Value.Y, 2);

        var mid = vm.MapMouse(new Vector2(dest.Center.X, dest.Center.Y))!.Value;
        Assert.Equal(640f, mid.X, 1);
        Assert.Equal(360f, mid.Y, 1);

        // In the letterbox bar: not over the game.
        Assert.Null(vm.MapMouse(new Vector2(800, 40)));
    }

    // ---- 4. The cameras are where the scale is applied ----

    [Fact]
    public void LayoutCamera_MapsAnAuthoringPointToItsRenderPixel()
    {
        var vm = Manager(1920, 1080, 1920, 1080, 1280, 720);

        var rendered = Vector2.Transform(new Vector2(100, 50),
            vm.LayoutCamera.GetViewTransformationMatrix());

        Assert.Equal(150f, rendered.X, 3);
        Assert.Equal(75f, rendered.Y, 3);

        // Origin and far corner too — the mapping is a pure scale about the render-target origin.
        var origin = Vector2.Transform(Vector2.Zero, vm.LayoutCamera.GetViewTransformationMatrix());
        Assert.Equal(0f, origin.X, 3);
        Assert.Equal(0f, origin.Y, 3);
        var far = Vector2.Transform(new Vector2(1280, 720), vm.LayoutCamera.GetViewTransformationMatrix());
        Assert.Equal(1920f, far.X, 2);
        Assert.Equal(1080f, far.Y, 2);
    }

    [Fact]
    public void CreateLayoutCamera_DoesTheSameForASubTarget()
    {
        var vm = Manager(1920, 1080, 1920, 1080, 1280, 720);

        // A 360×220 authored scroll box rendered at 540×330 render pixels.
        var camera = vm.CreateLayoutCamera(540, 330);
        Assert.Equal(540, camera.VirtualWidth);
        Assert.Equal(1.5f, camera.RenderScale, 4);

        var rendered = Vector2.Transform(new Vector2(360, 220), camera.GetViewTransformationMatrix());
        Assert.Equal(540f, rendered.X, 2);
        Assert.Equal(330f, rendered.Y, 2);
    }

    [Fact]
    public void WorldCamera_ZoomStaysAnAuthoringNumber()
    {
        var vm = Manager(1920, 1080, 1920, 1080, 1280, 720);
        var camera = vm.CreateCamera();
        camera.Position = new Vector2(500, 300);
        camera.Zoom = 2f;

        // World → render pixels is (world − camera) × zoom × renderScale + renderCentre.
        var rendered = Vector2.Transform(new Vector2(600, 300), camera.GetViewTransformationMatrix());
        Assert.Equal(960f + 100f * 2f * 1.5f, rendered.X, 2);
        Assert.Equal(540f, rendered.Y, 2);

        // The camera still reports the destination it must match, and its authoring extent.
        Assert.Equal(1920, camera.VirtualWidth);
        Assert.Equal(1280, camera.LayoutWidth);
        Assert.Equal(720, camera.LayoutHeight);
    }

    // ---- 5. The headline: a render-resolution move moves nothing authored ----

    [Fact]
    public void PointerToWorld_IsIdenticalAcrossRenderResolutions()
    {
        var single = Manager(1600, 900, 1280, 720);
        var scaled = Manager(1600, 900, 1920, 1080, 1280, 720);

        var a = single.CreateCamera();
        var b = scaled.CreateCamera();
        foreach (var camera in new[] { a, b })
        {
            camera.Position = new Vector2(2000, 1500);
            camera.Zoom = 1.75f;
        }

        foreach (var screen in new[] { new Vector2(800, 450), new Vector2(20, 880), new Vector2(1590, 10) })
        {
            var worldA = a.VirtualScreenToWorld(single.MapMouse(screen)!.Value);
            var worldB = b.VirtualScreenToWorld(scaled.MapMouse(screen)!.Value);
            Assert.Equal(worldA.X, worldB.X, 2);
            Assert.Equal(worldA.Y, worldB.Y, 2);
        }
    }

    [Fact]
    public void CullingExtent_IsTheSameWorldRectangleAtAnyRenderResolution()
    {
        var single = new MonoDreams.Component.Camera(1280, 720);
        var scaled = new MonoDreams.Component.Camera(2560, 1440, renderScale: 2f);
        foreach (var camera in new[] { single, scaled })
        {
            camera.Position = new Vector2(100, 200);
            camera.Zoom = 0.5f;
        }

        Assert.Equal(single.ViewSize.X, scaled.ViewSize.X, 3);
        Assert.Equal(single.ViewSize.Y, scaled.ViewSize.Y, 3);
        Assert.Equal(single.VirtualScreenBounds, scaled.VirtualScreenBounds);

        // Zoom 0.5 over a 1280×720 authoring space sees 2560×1440 world units.
        Assert.Equal(2560f, scaled.ViewSize.X, 3);
    }
}
