using System;
using Microsoft.Xna.Framework;
using MonoDreams.Renderer;
using Xunit;

namespace MonoDreams.Tests.Rendering;

/// <summary>
/// Protects the rendering premise "The viewport inset moves compositing and mouse mapping
/// together" (Wave 7 editor shell). The <see cref="ViewportManager"/> is the single source of
/// truth for the aspect-fit game viewport: <c>SetViewportInset</c> reserves chrome margins and
/// BOTH the final-draw destination rectangle AND <c>MapMouse</c> follow the
/// same inset rectangle; zero inset must be byte-identical to the historical full-window
/// letterbox. Pure CPU math — the <c>Game</c> ctor argument is never dereferenced, so tests pass
/// null (no GraphicsDevice).
/// </summary>
public class ViewportInsetTests
{
    private static ViewportManager Manager(int screenWidth, int screenHeight,
        int virtualWidth = 800, int virtualHeight = 600)
    {
        var vm = new ViewportManager(null, virtualWidth, virtualHeight)
        {
            ScreenWidth = screenWidth,
            ScreenHeight = screenHeight,
        };
        return vm;
    }

    // The editor shell's margins (mirrors EditorChromeLayout.ViewportInset — kept literal here so
    // a layout retune fails THIS math test only if the math itself regresses).
    private const int Top = 44;
    private const int Right = 280;
    private const int Bottom = 24;

    // ---- Regression: no inset = the historical full-window letterbox ----

    [Fact]
    public void NoInset_MatchesLegacyLetterboxRect()
    {
        // 1600×900 window, 800×600 (4:3) virtual → pillarbox-free letterbox: full height,
        // width = 900 * 4/3 = 1200, centered → (200, 0, 1200, 900).
        var vm = Manager(1600, 900);
        Assert.False(vm.HasViewportInset);
        Assert.Equal(new Rectangle(200, 0, 1200, 900), vm.DestinationRectangle);

        // Matching aspect fills the window exactly.
        var exact = Manager(800, 600);
        Assert.Equal(new Rectangle(0, 0, 800, 600), exact.DestinationRectangle);
    }

    [Fact]
    public void NoInset_LegacyMouseMapping()
    {
        var vm = Manager(1600, 900);

        // Inside the letterboxed viewport: (800, 450) is the screen centre → virtual centre.
        var centre = vm.MapMouse(new Vector2(800, 450));
        Assert.NotNull(centre);
        Assert.Equal(400f, centre.Value.X, 3);
        Assert.Equal(300f, centre.Value.Y, 3);

        // In the pillarbox bar (x < 200): no mapping.
        Assert.Null(vm.MapMouse(new Vector2(100, 450)));
    }

    [Fact]
    public void SetThenClearInset_RestoresTheLegacyRect()
    {
        var vm = Manager(1600, 900);
        var legacy = vm.DestinationRectangle;

        vm.SetViewportInset(0, Top, Right, Bottom);
        Assert.True(vm.HasViewportInset);
        Assert.NotEqual(legacy, vm.DestinationRectangle);

        vm.ClearViewportInset();
        Assert.False(vm.HasViewportInset);
        Assert.Equal(legacy, vm.DestinationRectangle);
    }

    // ---- Inset math: centered + aspect-correct inside the available sub-rectangle ----

    [Fact]
    public void Inset_CentersAspectFitInsideTheAvailableArea()
    {
        // 1600×900 minus (top 44, right 280, bottom 24) → available (0, 44, 1320, 832).
        // 1320/832 ≈ 1.586 > 4/3 → full available height: destH = 832,
        // destW = 832 * 4/3 + 0.5 → 1109, centered in the available width → x = 105, y = 44.
        var vm = Manager(1600, 900);
        vm.SetViewportInset(0, Top, Right, Bottom);

        Assert.Equal(new Rectangle(105, 44, 1109, 832), vm.DestinationRectangle);
    }

    [Fact]
    public void Inset_ResizeRecomputes()
    {
        var vm = Manager(1600, 900);
        vm.SetViewportInset(0, Top, Right, Bottom);
        _ = vm.DestinationRectangle; // force a calculation before the resize

        // Grow the window: 1920×1080 → available (0, 44, 1640, 1012) → destH = 1012,
        // destW = 1012 * 4/3 + 0.5 → 1349, x = (1640 - 1349) / 2 → 145 (float-centered, truncated).
        vm.ScreenWidth = 1920;
        vm.ScreenHeight = 1080;
        Assert.Equal(new Rectangle(145, 44, 1349, 1012), vm.DestinationRectangle);
    }

    // ---- Mouse mapping through the inset: same function, smaller rect ----

    [Fact]
    public void Inset_MouseInsideTheInsetViewport_MapsToTheCorrectVirtualPoint()
    {
        var vm = Manager(1600, 900);
        vm.SetViewportInset(0, Top, Right, Bottom);
        var dest = vm.DestinationRectangle; // (105, 44, 1109, 832)

        // The inset viewport's top-left corner maps to virtual (0, 0)...
        var corner = vm.MapMouse(new Vector2(dest.X, dest.Y));
        Assert.NotNull(corner);
        Assert.Equal(0f, corner.Value.X, 3);
        Assert.Equal(0f, corner.Value.Y, 3);

        // ...and its centre to the virtual centre (400, 300).
        var centre = vm.MapMouse(
            new Vector2(dest.X + dest.Width / 2f, dest.Y + dest.Height / 2f));
        Assert.NotNull(centre);
        Assert.Equal(400f, centre.Value.X, 1);
        Assert.Equal(300f, centre.Value.Y, 1);
    }

    [Fact]
    public void Inset_MouseInTheChromeMargins_MapsToNull()
    {
        var vm = Manager(1600, 900);
        vm.SetViewportInset(0, Top, Right, Bottom);

        Assert.Null(vm.MapMouse(new Vector2(10, 10)));      // top bar
        Assert.Null(vm.MapMouse(new Vector2(1500, 400)));   // right panel
        Assert.Null(vm.MapMouse(new Vector2(800, 890)));    // bottom strip
    }

    // ---- Pixel-perfect mode computes its integer-scaled rect inside the same available area ----

    [Fact]
    public void PixelPerfect_UsesTheAvailableArea()
    {
        var vm = Manager(1600, 900);
        vm.CurrentScalingMode = ViewportManager.ScalingMode.PixelPerfect;

        // Full window: integer scale = min(1600/800, 900/600) = 1 → 800×600 centered on screen.
        Assert.Equal(new Rectangle(400, 150, 800, 600), vm.PixelPerfectDestinationRectangle);

        // With the inset: available (0, 44, 1320, 832) → scale still 1, centered in the available
        // area → ((1320-800)/2, 44 + (832-600)/2) = (260, 160).
        vm.SetViewportInset(0, Top, Right, Bottom);
        Assert.Equal(new Rectangle(260, 160, 800, 600), vm.PixelPerfectDestinationRectangle);
        Assert.Equal(1, vm.IntegerScale);
    }

    // ---- Guardrails ----

    [Fact]
    public void NegativeInset_Throws()
    {
        var vm = Manager(1600, 900);
        Assert.Throws<ArgumentOutOfRangeException>(() => vm.SetViewportInset(-1, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => vm.SetViewportInset(0, 0, 0, -5));
    }

    [Fact]
    public void OversizedInset_ClampsToADegenerateButValidViewport()
    {
        // Margins larger than the window must not divide by zero or go negative.
        var vm = Manager(300, 200);
        vm.SetViewportInset(0, 150, 280, 100);
        var rect = vm.DestinationRectangle;
        Assert.True(rect.Width >= 1);
        Assert.True(rect.Height >= 1);
    }
}
