using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Renderer;
using MonoDreams.System.Draw;
using Xunit;

namespace MonoDreams.Tests.Rendering;

/// <summary>
/// Protects the rendering premise "Presentation scaling is a declared policy resolved in one
/// place" (issue #89). The chain is overscan → letterbox → stretch, each step bounded by a
/// gamedev-set tolerance; <see cref="ViewportManager"/> places the rectangle the policy resolves
/// and <c>MapMouse</c> inverts that same rectangle, whichever step won. Pure CPU math — the
/// <c>Game</c> ctor argument is never dereferenced, so tests pass null (no GraphicsDevice).
/// </summary>
public class PresentationPolicyTests
{
    private static ViewportManager Manager(int screenWidth, int screenHeight,
        int virtualWidth = 1920, int virtualHeight = 1080, PresentationPolicy policy = null) =>
        new(null, virtualWidth, virtualHeight)
        {
            ScreenWidth = screenWidth,
            ScreenHeight = screenHeight,
            Policy = policy ?? PresentationPolicy.Default,
        };

    // ---- 1. The clean-scale ladder ----

    [Theory]
    // Upscale: whole steps, and the half step between them.
    [InlineData(2.4f, 2f, 2.5f)]
    [InlineData(1f, 1f, 1f)]
    [InlineData(3.5f, 3.5f, 3.5f)]
    // Downscale: the ladder is the RECIPROCAL one (1/1.5, 1/2, 1/2.5, …), which is what keeps a
    // fixed pixel pattern instead of an arbitrary one.
    [InlineData(0.7292f, 1f / 1.5f, 1f)]
    [InlineData(0.5f, 0.5f, 0.5f)]
    [InlineData(0.45f, 0.4f, 0.5f)]
    public void HalfSteps_LadderBracketsTheScale(float scale, float below, float above)
    {
        var policy = PresentationPolicy.Default;
        Assert.Equal(below, policy.CleanScaleAtOrBelow(scale), 4);
        Assert.Equal(above, policy.CleanScaleAtOrAbove(scale), 4);
    }

    [Theory]
    [InlineData(2.4f, 2f, 3f)]
    [InlineData(1.5f, 1f, 2f)]
    [InlineData(0.4f, 1f / 3f, 0.5f)]
    public void IntegerSteps_SkipTheHalves(float scale, float below, float above)
    {
        var policy = PresentationPolicy.PixelPerfect;
        Assert.Equal(below, policy.CleanScaleAtOrBelow(scale), 4);
        Assert.Equal(above, policy.CleanScaleAtOrAbove(scale), 4);
    }

    [Fact]
    public void CleanScales_AreTheirOwnBracket()
    {
        var policy = PresentationPolicy.Default;
        foreach (var clean in new[] { 1f / 3f, 0.5f, 1f / 1.5f, 1f, 1.5f, 2f, 4f })
        {
            Assert.True(policy.IsClean(clean), $"{clean} should be on the ladder");
            Assert.Equal(clean, policy.CleanScaleAtOrBelow(clean), 4);
            Assert.Equal(clean, policy.CleanScaleAtOrAbove(clean), 4);
        }

        Assert.False(policy.IsClean(0.7292f));
        Assert.False(policy.IsClean(1.24f));
    }

    // ---- 2. The chain, in declared preference order ----

    [Fact]
    public void Overscan_WinsWhenACleanStepIsWithinTolerance()
    {
        // 1830×1029 window, 1920×1080 frame: fit 0.9531 → 1× costs 4.9% of extra scale, inside the
        // 5% tolerance. The frame is presented 1:1 and sticks out on all four sides.
        var vm = Manager(1830, 1029);
        Assert.Equal(PresentationMode.Overscan, vm.Presentation);
        Assert.Equal(1f, vm.PresentScale, 4);
        Assert.Equal(new Rectangle(-45, -25, 1920, 1080), vm.DestinationRectangle);
    }

    [Fact]
    public void Overscan_IsRefusedPastItsTolerance_AndLetterboxTakesOver()
    {
        // 1400×788 window: fit 0.7292 → 1× would cost 37% of extra scale (way past 5%), so the
        // chain drops to the clean step below (1/1.5) and pads with bars: 1280×720 centered.
        var vm = Manager(1400, 788);
        Assert.Equal(PresentationMode.Letterbox, vm.Presentation);
        Assert.Equal(1f / 1.5f, vm.PresentScale, 3);
        Assert.Equal(new Rectangle(60, 34, 1280, 720), vm.DestinationRectangle);
    }

    [Fact]
    public void Letterbox_IsRefusedPastItsTolerance_AndStretchTakesOver()
    {
        // 1800×1013 window: fit 0.9375 → overscan to 1× costs 6.7% (past 5%), and the step below
        // (1/1.5) would give up 29% of the picture (past 25%), so the chain stretches — landing on
        // the exact rectangle the pre-policy engine drew, which is what the Stretch policy is.
        var vm = Manager(1800, 1013);
        Assert.Equal(PresentationMode.Stretch, vm.Presentation);
        Assert.Equal(Manager(1800, 1013, policy: PresentationPolicy.Stretch).DestinationRectangle,
            vm.DestinationRectangle);
        Assert.Equal(1800, vm.DestinationRectangle.Width);
    }

    [Fact]
    public void StretchPolicy_NeverLeavesTheAspectFitRectangle()
    {
        // The same three windows under the engine default: always the aspect-fit rectangle.
        foreach (var (w, h) in new[] { (1830, 1029), (1400, 788), (1800, 1013) })
        {
            var vm = Manager(w, h, policy: PresentationPolicy.Stretch);
            var dest = vm.DestinationRectangle;
            Assert.True(dest.Width <= w && dest.Height <= h, $"{dest} must fit inside {w}x{h}");
            Assert.Equal(1920f / 1080f, dest.Width / (float)dest.Height, 2);
        }
    }

    [Fact]
    public void CrispPolicy_NeverStretches_HoweverWideTheBars()
    {
        // The window that stretched above now drops to a clean 1/1.5 with 29% bars rather than
        // resample at a fractional ratio.
        var vm = Manager(1800, 1013, policy: PresentationPolicy.Crisp);
        Assert.Equal(PresentationMode.Letterbox, vm.Presentation);
        Assert.Equal(1f / 1.5f, vm.PresentScale, 3);
        Assert.Equal(new Rectangle(260, 146, 1280, 720), vm.DestinationRectangle);
    }

    [Fact]
    public void PixelPerfectPolicy_MatchesTheRetiredMode_AtOrAboveOneX()
    {
        // The retired ScalingMode.PixelPerfect was: scale = max(1, min(avail/virtual)) floored to a
        // whole number, centered. Above 1× the floor never binds, so policy and mode agree exactly.
        foreach (var (w, h) in new[] { (1920, 1080), (2560, 1440), (4000, 2160), (5760, 3240) })
        {
            var vm = Manager(w, h, policy: PresentationPolicy.PixelPerfect);
            var oldScale = Math.Max(1, Math.Min(w / 1920, h / 1080));
            var oldRect = new Rectangle((w - 1920 * oldScale) / 2, (h - 1080 * oldScale) / 2,
                1920 * oldScale, 1080 * oldScale);
            Assert.Equal(oldRect, vm.DestinationRectangle);
        }
    }

    [Fact]
    public void PixelPerfectPolicy_ShrinksInWholeSteps_WhereTheRetiredModeCropped()
    {
        // Below 1× the two DIVERGE, deliberately. The old mode clamped its integer scale to a floor
        // of 1, presenting 1920×1080 at (-160,-90) in a 1600×900 window — the frame's edges cropped
        // off-screen. A policy with overscan OFF may not crop, so the ladder keeps descending:
        // 1/2 of the frame, centered, with bars. (Cropping is the overscan step's business, and it
        // is bounded by a declared tolerance.)
        var vm = Manager(1600, 900, policy: PresentationPolicy.PixelPerfect);
        Assert.Equal(PresentationMode.Letterbox, vm.Presentation);
        Assert.Equal(0.5f, vm.PresentScale, 4);
        Assert.Equal(new Rectangle(320, 180, 960, 540), vm.DestinationRectangle);

        // …and being a real letterbox, the bars map to null — where the old mode's crop had no bars
        // at all and the pointer over an off-screen frame edge was simply unmappable.
        Assert.Null(vm.MapMouse(new Vector2(100, 450)));

        // The whole ladder below 1× is reciprocal whole steps, never the old floor of 1.
        foreach (var (w, h, scale) in new[] { (1280, 720, 0.5f), (700, 400, 1f / 3f), (500, 280, 0.25f) })
        {
            var below = Manager(w, h, policy: PresentationPolicy.PixelPerfect);
            Assert.Equal(scale, below.PresentScale, 3);
            Assert.True(below.DestinationRectangle.Width <= w && below.DestinationRectangle.Height <= h,
                $"{below.DestinationRectangle} must fit inside {w}x{h} — a no-overscan policy never crops");
        }
    }

    [Fact]
    public void AnAlreadyCleanFit_IsPresentedAsIs()
    {
        // Window == render resolution: nothing to trade, and no policy can move it.
        foreach (var policy in new[]
                 {
                     PresentationPolicy.Default, PresentationPolicy.Crisp,
                     PresentationPolicy.PixelPerfect, PresentationPolicy.Stretch,
                 })
        {
            var vm = Manager(1920, 1080, policy: policy);
            Assert.Equal(new Rectangle(0, 0, 1920, 1080), vm.DestinationRectangle);
            Assert.Equal(1f, vm.PresentScale, 4);
            Assert.Equal(PresentationMode.Letterbox, vm.Presentation);
        }
    }

    // ---- 3. MapMouse inverts whichever step won ----

    [Fact]
    public void MapMouse_InvertsTheOverscannedDestination_WhenItCoversTheWindow()
    {
        var vm = Manager(1830, 1029, 1920, 1080);
        var dest = vm.DestinationRectangle;
        Assert.Equal(PresentationMode.Overscan, vm.Presentation);

        // This window's aspect is close enough to the frame's that the grown rectangle covers BOTH
        // axes, so no window pixel is "outside the game" — including the corners, which under a
        // letterbox would land in a bar. (That is a property of this window, not of overscan — see
        // MapMouse_StillNullsInABar_WhenOverscanOnlyCoversTheBindingAxis.)
        foreach (var screen in new[] { new Vector2(0, 0), new Vector2(1829, 1028), new Vector2(915, 514) })
            Assert.NotNull(vm.MapMouse(screen));

        // …and the mapping is the inverse of that destination: the screen point over the frame's
        // centre maps to the centre of authoring space.
        var centre = vm.MapMouse(new Vector2(dest.X + dest.Width / 2f, dest.Y + dest.Height / 2f))!.Value;
        Assert.Equal(960f, centre.X, 2);
        Assert.Equal(540f, centre.Y, 2);

        // The top-left window pixel maps INSIDE authoring space by the cropped amount (45 screen
        // pixels ÷ 1× present scale), not to (0,0) — the frame's own corner is off-screen.
        var corner = vm.MapMouse(new Vector2(0, 0))!.Value;
        Assert.Equal(45f, corner.X, 2);
        Assert.Equal(25f, corner.Y, 2);
    }

    [Fact]
    public void MapMouse_StillNullsInABar_WhenOverscanOnlyCoversTheBindingAxis()
    {
        // Overscan grows the frame past the aspect-fit rectangle, so it always covers the axis that
        // BOUND the fit — but only that one. A 2000×1029 window is bound by its height (fit 0.9528,
        // 1× costs 4.96%, inside the 5% tolerance), so the 1920×1080 frame overscans vertically and
        // is CROPPED top and bottom — while horizontally 1920 < 2000 leaves 40px pillarbars.
        var vm = Manager(2000, 1029, 1920, 1080);
        Assert.Equal(PresentationMode.Overscan, vm.Presentation);
        Assert.Equal(new Rectangle(40, -25, 1920, 1080), vm.DestinationRectangle);

        // The pointer in a surviving bar is NOT over the game, overscan or not.
        Assert.Null(vm.MapMouse(new Vector2(10, 500)));
        Assert.Null(vm.MapMouse(new Vector2(1990, 500)));

        // The cropped axis behaves the other way round: the top window row maps INSIDE authoring
        // space by the 25 rows that left the screen, and the frame's own edge is unreachable.
        var top = vm.MapMouse(new Vector2(1000, 0))!.Value;
        Assert.Equal(25f, top.Y, 2);
        Assert.Equal(0f, vm.MapMouse(new Vector2(40, 500))!.Value.X, 2);
    }

    [Fact]
    public void MapMouse_InvertsTheLetterboxedDestination_AndNullsInTheBars()
    {
        var vm = Manager(1400, 788, 1920, 1080);
        var dest = vm.DestinationRectangle;
        Assert.Equal(new Rectangle(60, 34, 1280, 720), dest);

        var origin = vm.MapMouse(new Vector2(dest.X, dest.Y))!.Value;
        Assert.Equal(0f, origin.X, 2);
        Assert.Equal(0f, origin.Y, 2);

        var centre = vm.MapMouse(new Vector2(dest.X + dest.Width / 2f, dest.Y + dest.Height / 2f))!.Value;
        Assert.Equal(960f, centre.X, 1);
        Assert.Equal(540f, centre.Y, 1);

        // The bars the policy introduced are outside the game, exactly like an aspect-ratio bar.
        Assert.Null(vm.MapMouse(new Vector2(10, 400)));
        Assert.Null(vm.MapMouse(new Vector2(700, 10)));
    }

    [Fact]
    public void MapMouse_IsAuthoringSpace_UnderEveryStep()
    {
        // Two spaces: author at 1280×720, render at 1920×1080 (RenderScale 1.5). Whichever step
        // presents, the mapped point is an AUTHORING one — the pointer's centre is (640, 360).
        foreach (var (w, h) in new[] { (1830, 1029), (1400, 788), (1800, 1013) })
        {
            var vm = new ViewportManager(null, 1920, 1080, 1280, 720)
            {
                ScreenWidth = w, ScreenHeight = h, Policy = PresentationPolicy.Default,
            };
            var dest = vm.DestinationRectangle;
            var centre = vm.MapMouse(new Vector2(dest.X + dest.Width / 2f, dest.Y + dest.Height / 2f))!.Value;
            Assert.Equal(640f, centre.X, 1);
            Assert.Equal(360f, centre.Y, 1);
        }
    }

    // ---- 4. The editor's inset vetoes overscan (a grown frame would paint over the chrome) ----

    [Fact]
    public void ViewportInset_VetoesOverscan_ButNotTheRestOfTheChain()
    {
        var vm = Manager(1830, 1029);
        Assert.Equal(PresentationMode.Overscan, vm.Presentation);

        vm.SetViewportInset(0, 44, 280, 24);
        Assert.NotEqual(PresentationMode.Overscan, vm.Presentation);

        // The frame stays inside the area left for it — the chrome margins are never painted over.
        var dest = vm.DestinationRectangle;
        Assert.True(dest.X >= 0 && dest.Y >= 44, $"{dest} must start inside the inset area");
        Assert.True(dest.Right <= 1830 - 280 && dest.Bottom <= 1029 - 24, $"{dest} must end inside it");

        // Clearing the inset restores the overscan decision — the veto is per window, not sticky.
        vm.ClearViewportInset();
        Assert.Equal(PresentationMode.Overscan, vm.Presentation);
    }

    // ---- 5. The other end of the overscan dial: render MORE world instead of cropping ----

    [Fact]
    public void ResolveRenderSize_GrowsTheRenderResolutionSoNothingIsCropped()
    {
        // Same 1830×1029 window that overscanned above. Sized this way, the render targets ARE the
        // window at a clean 1× — the 5% is spent revealing more world, not on losing frame edges.
        var policy = PresentationPolicy.Default;
        var size = policy.ResolveRenderSize(1920, 1080, 1830, 1029);
        Assert.Equal(new Point(1920, 1080), size); // the window is SMALLER: nothing to gain

        // A window slightly LARGER than the design frame is the case that gains view: at 1× the
        // extra 4% of window becomes extra world instead of a 4% upscale.
        var grown = policy.ResolveRenderSize(1920, 1080, 2000, 1120);
        Assert.Equal(new Point(2000, 1120), grown);

        // …and a manager rendering at that size presents it 1:1, filling the window with no bars
        // and no crop — the zero-overflow overscan the tolerance is really buying.
        var vm = Manager(2000, 1120, grown.X, grown.Y);
        Assert.Equal(new Rectangle(0, 0, 2000, 1120), vm.DestinationRectangle);
        Assert.Equal(1f, vm.PresentScale, 4);
    }

    [Fact]
    public void ResolveRenderSize_RefusesPastTolerance_AndNeverShrinksTheDesign()
    {
        var policy = PresentationPolicy.Default;

        // 9.4% more view — past the 5% tolerance, so the design resolution stands.
        Assert.Equal(new Point(1280, 720), policy.ResolveRenderSize(1280, 720, 1400, 788));

        // A policy that declares it can afford 10% takes the same window.
        var generous = PresentationPolicy.Default with { OverscanTolerance = 0.10f };
        Assert.Equal(new Point(1400, 788), generous.ResolveRenderSize(1280, 720, 1400, 788));

        // Overscan off ⇒ the design resolution, always.
        Assert.Equal(new Point(1280, 720),
            PresentationPolicy.Stretch.ResolveRenderSize(1280, 720, 2000, 1120));

        // Idempotent: feeding the resolved size back in resolves to itself (no ratchet).
        var once = generous.ResolveRenderSize(1280, 720, 1400, 788);
        Assert.Equal(once, generous.ResolveRenderSize(once.X, once.Y, 1400, 788));
    }

    // ---- 6. The per-layer sampler policy ----

    [Theory]
    [InlineData(1f, true)]
    [InlineData(2f, true)]
    [InlineData(3f, true)]
    [InlineData(1.5f, false)]
    [InlineData(0.8f, false)]
    [InlineData(1f / 1.5f, false)]
    [InlineData(0.5f, false)] // a clean downscale still drops rows: filter it
    public void AutoSampler_IsPointAtAnIntegerScale_LinearOtherwise(float scale, bool point)
    {
        var expected = point ? SamplerState.PointClamp : SamplerState.LinearClamp;
        Assert.Same(expected, SamplerPolicy.Auto.Resolve(scale));
    }

    [Fact]
    public void ExplicitSamplers_IgnoreTheScale()
    {
        foreach (var scale in new[] { 0.8f, 1f, 2f })
        {
            Assert.Same(SamplerState.PointClamp, SamplerPolicy.Point.Resolve(scale));
            Assert.Same(SamplerState.LinearClamp, SamplerPolicy.Linear.Resolve(scale));
        }
    }

    [Fact]
    public void StandardLayers_DefaultToAuto_AndTheEditorChromeLayerToPoint()
    {
        Assert.Equal(SamplerPolicy.Auto, RenderLayer.Main(null).Sampler);
        Assert.Equal(SamplerPolicy.Auto, RenderLayer.UI(null).Sampler);
        Assert.Equal(SamplerPolicy.Auto, RenderLayer.HUD(null).Sampler);
        Assert.Equal(SamplerPolicy.Auto, RenderLayer.Overlay(null, new Rectangle(0, 0, 10, 10)).Sampler);
        Assert.Equal(SamplerPolicy.Linear,
            RenderLayer.Overlay(null, new Rectangle(0, 0, 10, 10), SamplerPolicy.Linear).Sampler);
        Assert.Equal(SamplerPolicy.Point, RenderLayer.Native(() => null).Sampler);
    }

    // ---- 7. Guardrails ----

    [Fact]
    public void NegativeTolerances_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PresentationPolicy.Default with { OverscanTolerance = -0.01f });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PresentationPolicy.Default with { LetterboxTolerance = -0.01f });
        // A letterbox tolerance of 1 would allow a zero-size present.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PresentationPolicy.Default with { LetterboxTolerance = 1f });
    }

    [Fact]
    public void ANullPolicy_Throws()
    {
        var vm = Manager(1600, 900);
        Assert.Throws<ArgumentNullException>(() => vm.Policy = null);
    }

    [Fact]
    public void ADegenerateWindow_StillResolvesAPresentableRectangle()
    {
        // The headless 1×1 window: no division by zero, no empty rectangle.
        var vm = Manager(1, 1);
        var dest = vm.DestinationRectangle;
        Assert.True(dest.Width >= 1 && dest.Height >= 1, $"{dest} must be presentable");
    }
}
