using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MonoDreams.Platform;

namespace MonoDreams.Tests.Foundation;

/// <summary>
/// Protects the opt-in windowing helper (issue #86): a fixed window larger than the display is not
/// clamped by macOS, so a game whose backbuffer is its render resolution opens partly offscreen with
/// no crash and no log. <see cref="WindowFit"/> is the engine's answer — largest aspect-correct
/// window inside the display's USABLE bounds, snapped to multiples of 16, capped at the render
/// resolution, with a <c>MONODREAMS_WINDOW=WxH</c> escape hatch.
///
/// Everything here drives the pure halves (<see cref="WindowFit.Fit"/>,
/// <see cref="WindowFit.Compute"/>, <see cref="WindowFit.TryParseSize"/>) — no graphics device, no
/// SDL, no environment: the environment reader is injected. The device-touching half
/// (<see cref="WindowFit.Apply"/>) is exercised by the scaffolded desktop head, which calls it at boot.
/// </summary>
public class WindowFitTests
{
    private static Func<string, string?> Env(string? windowOverride = null)
    {
        var vars = new Dictionary<string, string>();
        if (windowOverride != null) vars[WindowFit.OverrideVariable] = windowOverride;
        return name => vars.TryGetValue(name, out var value) ? value : null;
    }

    // ---- Fit: the geometry ---------------------------------------------------------------------

    [Fact]
    public void Fit_ShrinksToTheUsableArea_WhenTheRenderResolutionIsTallerThanTheDisplay()
    {
        // The reported case: 1920x1080 render on a 1512x982-point MacBook (usable, menu bar excluded).
        var window = WindowFit.Fit(1920, 1080, 1512, 982);

        Assert.True(window.X <= 1512, $"window width {window.X} must fit the usable width");
        Assert.True(window.Y <= 982 - WindowFit.ReservedChromeHeight,
            $"window height {window.Y} must fit the usable height minus the title bar");
        Assert.Equal(new Point(1504, 846), window);
    }

    [Fact]
    public void Fit_KeepsTheRenderAspect_WithinARoundingPixel()
    {
        var window = WindowFit.Fit(1920, 1080, 1512, 982);

        var renderAspect = 1920d / 1080d;
        var windowAspect = window.X / (double)window.Y;
        Assert.True(Math.Abs(renderAspect - windowAspect) < 0.002,
            $"aspect drifted: render {renderAspect}, window {windowAspect}");
    }

    [Fact]
    public void Fit_SnapsTheWidthDownToAMultipleOfSixteen()
    {
        // 1500 usable width would allow 1500; the snap floors it to 1488.
        var window = WindowFit.Fit(1920, 1080, 1500, 1400);

        Assert.Equal(0, window.X % WindowFit.SnapTo);
        Assert.Equal(1488, window.X);
    }

    [Fact]
    public void Fit_NeverMagnifiesPastTheRenderResolution()
    {
        // A 4K display with a 1280x720 game: 1:1 is the sharpest presentation, so stop there.
        var window = WindowFit.Fit(1280, 720, 3840, 2160);

        Assert.Equal(new Point(1280, 720), window);
    }

    [Fact]
    public void Fit_ReservesTheTitleBarFromTheUsableHeight()
    {
        // Height-bound case: without the reservation this would be exactly 1000 tall and the title
        // bar would push the bottom edge off the usable area — the very failure being prevented.
        var window = WindowFit.Fit(1000, 1000, 4000, 1000);

        Assert.True(window.Y <= 1000 - WindowFit.ReservedChromeHeight);
    }

    [Fact]
    public void Fit_HandlesATallRenderResolution_OnAWideDisplay()
    {
        var window = WindowFit.Fit(1080, 1920, 1512, 982);

        Assert.True(window.X <= 1512);
        Assert.True(window.Y <= 982 - WindowFit.ReservedChromeHeight);
        Assert.Equal(0, window.X % WindowFit.SnapTo);
    }

    [Fact]
    public void Fit_NeverReturnsADegenerateSize_OnAnAbsurdlySmallDisplay()
    {
        var window = WindowFit.Fit(1920, 1080, 4, 4);

        Assert.True(window.X > 0 && window.Y > 0);
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(-1, -1)]
    public void Fit_RejectsANonPositiveRenderResolution(int renderWidth, int renderHeight)
        => Assert.Throws<ArgumentOutOfRangeException>(() => WindowFit.Fit(renderWidth, renderHeight, 1512, 982));

    // ---- Compute: mode selection ----------------------------------------------------------------

    [Fact]
    public void Compute_WithoutAnOverride_FitsTheUsableBounds()
    {
        var result = WindowFit.Compute(1920, 1080, new Point(1512, 982), new Point(1512, 944), true, Env());

        Assert.Equal(WindowFitMode.Fit, result.Mode);
        Assert.True(result.UsableFromSystem);
        Assert.Equal(WindowFit.Fit(1920, 1080, 1512, 944), result.Window);
    }

    [Fact]
    public void Compute_AppliesTheEnvironmentOverrideVerbatim_NoSnapNoCap()
    {
        // Deliberately not a multiple of 16 AND larger than the render resolution: an explicit
        // override is an instruction, not a hint — a scripted run must get the size it asked for.
        var result = WindowFit.Compute(1920, 1080, new Point(1512, 982), new Point(1512, 944), true, Env("2001x999"));

        Assert.Equal(WindowFitMode.Override, result.Mode);
        Assert.Equal(new Point(2001, 999), result.Window);
    }

    [Fact]
    public void Compute_IgnoresAnUnparseableOverride_AndFallsBackToFitting()
    {
        var result = WindowFit.Compute(1920, 1080, new Point(1512, 982), new Point(1512, 944), true, Env("not-a-size"));

        Assert.Equal(WindowFitMode.Fit, result.Mode);
        Assert.Equal(WindowFit.Fit(1920, 1080, 1512, 944), result.Window);
    }

    [Fact]
    public void Compute_WithUnmeasurableBounds_AppliesTheRenderResolutionUnchanged()
    {
        // No adapter / degenerate display: behave exactly like a game that never called WindowFit.
        var result = WindowFit.Compute(1920, 1080, Point.Zero, Point.Zero, false, Env());

        Assert.Equal(WindowFitMode.Unmeasured, result.Mode);
        Assert.Equal(new Point(1920, 1080), result.Window);
    }

    [Fact]
    public void Compute_WithUnmeasurableBounds_StillHonoursTheOverride()
    {
        var result = WindowFit.Compute(1920, 1080, Point.Zero, Point.Zero, false, Env("800x600"));

        Assert.Equal(WindowFitMode.Override, result.Mode);
        Assert.Equal(new Point(800, 600), result.Window);
    }

    [Fact]
    public void Compute_CarriesDisplayAndUsableThrough_ForTheBootLogLine()
    {
        var result = WindowFit.Compute(1920, 1080, new Point(1512, 982), new Point(1512, 944), false, Env());

        Assert.Equal(new Point(1512, 982), result.Display);
        Assert.Equal(new Point(1512, 944), result.Usable);
        Assert.False(result.UsableFromSystem); // the fixed-margin fallback path
    }

    // ---- TryParseSize --------------------------------------------------------------------------

    [Theory]
    [InlineData("1280x720", 1280, 720)]
    [InlineData("1280X720", 1280, 720)]
    [InlineData(" 1280 x 720 ", 1280, 720)]
    public void TryParseSize_AcceptsWxH(string value, int width, int height)
    {
        Assert.True(WindowFit.TryParseSize(value, out var size));
        Assert.Equal(new Point(width, height), size);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1280")]
    [InlineData("1280x")]
    [InlineData("1280x720x60")]
    [InlineData("0x720")]
    [InlineData("1280x-720")]
    [InlineData("1280*720")]
    public void TryParseSize_RejectsAnythingElse(string? value)
    {
        Assert.False(WindowFit.TryParseSize(value, out var size));
        Assert.Equal(Point.Zero, size);
    }

    // ---- Usable-bounds probe --------------------------------------------------------------------

    [Fact]
    public void ProbeUsableBounds_FallsBackToAFixedMargin_WhenSdlCannotAnswer()
    {
        // In a test host SDL is not loaded (or the export is missing), so this exercises the
        // fallback branch: the display bounds minus the documented margins, never a degenerate size.
        var usable = WindowFit.ProbeUsableBounds(new Point(1512, 982), out var fromSystem);

        if (fromSystem)
        {
            Assert.True(usable.X > 0 && usable.Y > 0);
        }
        else
        {
            Assert.Equal(new Point(1512 - WindowFit.FallbackMarginWidth, 982 - WindowFit.FallbackMarginHeight), usable);
        }
    }

    [Fact]
    public void ProbeUsableBounds_NeverReturnsANonPositiveSize_ForATinyDisplay()
    {
        var usable = WindowFit.ProbeUsableBounds(new Point(8, 8), out _);

        Assert.True(usable.X > 0 && usable.Y > 0);
    }
}
