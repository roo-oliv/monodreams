#nullable enable
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.State;

namespace MonoDreams.Platform;

/// <summary>How <see cref="WindowFit"/> arrived at the window size it applied.</summary>
public enum WindowFitMode
{
    /// <summary>The largest aspect-correct window that fits inside the display's usable area,
    /// snapped down to a multiple of <see cref="WindowFit.SnapTo"/> and never larger than the
    /// render resolution. The normal mode; the window is left user-resizable.</summary>
    Fit,

    /// <summary>An explicit <c>WxH</c> from <see cref="WindowFit.OverrideVariable"/>, applied
    /// verbatim — no aspect fit, no snapping, no cap. The window is left fixed so the size an
    /// automated run asked for is the size it gets.</summary>
    Override,

    /// <summary>The display could not be measured (no adapter, or degenerate bounds), so the render
    /// resolution was applied unchanged — exactly what a game that never called
    /// <see cref="WindowFit"/> would have done.</summary>
    Unmeasured,
}

/// <summary>
/// What <see cref="WindowFit.Apply"/> decided, in the units the platform reports (see the
/// module premise "On macOS DesktopGL every window number is in points").
/// </summary>
/// <param name="Mode">How the size was chosen.</param>
/// <param name="Display">The display's full bounds; <see cref="Point.Zero"/> when unmeasurable.</param>
/// <param name="Usable">The display area excluding OS chrome (menu bar / taskbar / dock);
/// <see cref="Point.Zero"/> when unmeasurable.</param>
/// <param name="Window">The client size applied to the backbuffer.</param>
/// <param name="UsableFromSystem"><c>true</c> when <see cref="Usable"/> came from
/// <c>SDL_GetDisplayUsableBounds</c>, <c>false</c> when it came from the fixed-margin fallback.</param>
public readonly record struct WindowFitResult(
    WindowFitMode Mode,
    Point Display,
    Point Usable,
    Point Window,
    bool UsableFromSystem);

/// <summary>
/// Opt-in windowing helper: opens a desktop game at the largest aspect-correct window that actually
/// FITS on the player's display.
///
/// <para><b>The bug it exists to prevent.</b> MonoGame 3.8.4 DesktopGL leaves a <i>fixed</i> window
/// unclamped on macOS (a resizable one the OS clamps for you), so a game whose
/// <c>PreferredBackBuffer</c> is its render resolution — say 1920x1080 — opens a window taller than
/// a 1512x982-point MacBook screen. Nothing crashes and nothing logs; the bottom strip of the game,
/// which is where the Start button usually lives, simply renders below the physical screen. The
/// player sees a broken game and closes it. MonoGame also never bound
/// <c>SDL_GetDisplayUsableBounds</c> (the menu-bar- and dock-aware area) — only <c>GetBounds</c> and
/// <c>GetCurrentDisplayMode</c> — so a game cannot even ask the right question through the public
/// API.</para>
///
/// <para><b>What it does.</b> <see cref="Apply"/> reads the display mode, probes the usable bounds
/// through <see cref="SdlNative"/> (fixed-margin fallback when the export is missing), computes the
/// largest window of the render aspect that fits inside them — snapped down to a multiple of
/// <see cref="SnapTo"/> and capped at the render resolution, because 1:1 is the sharpest a game can
/// present — writes it to the <see cref="GraphicsDeviceManager"/>, and emits ONE boot log line
/// carrying display / usable / window / mode. That line is the feature's observable: it is how a
/// bug report ("the buttons are off-screen") is diagnosed from a log instead of a screenshot.</para>
///
/// <para><b>Strictly opt-in.</b> Nothing in the engine calls this. A game that never calls
/// <see cref="Apply"/> keeps whatever backbuffer it set, byte-for-byte — the helper has no system,
/// no component, and no pipeline presence. Scaffolded desktop heads (<c>monodreams init</c>) call it
/// by default so a new game is immune from day one.</para>
///
/// <para><b>Units.</b> On macOS DesktopGL everything this helper touches — the display mode, the SDL
/// window, and the GL backbuffer — is in <i>points</i>, never physical pixels; there is no Retina
/// conversion anywhere in this path. See the foundation premises.</para>
/// </summary>
public static class WindowFit
{
    /// <summary>Environment variable holding an explicit <c>WxH</c> window size (e.g.
    /// <c>MONODREAMS_WINDOW=1280x720</c>). Applied verbatim; an unparseable value is warned about and
    /// ignored. Its purpose is reproducible sizes for automated / scripted runs.</summary>
    public const string OverrideVariable = "MONODREAMS_WINDOW";

    /// <summary>The fitted window's width is snapped DOWN to a multiple of this. Nice round window
    /// sizes keep the present-time scale factor well-behaved and make screenshots comparable across
    /// machines; the derived height follows the render aspect, so the snap costs at most a fraction
    /// of a pixel of aspect (invisible behind the letterbox the viewport already applies).</summary>
    public const int SnapTo = 16;

    /// <summary>Points of usable HEIGHT reserved for the window's own title bar. The backbuffer size
    /// is the CLIENT size, but the OS positions the whole window frame — a client exactly as tall as
    /// the usable area pushes its bottom edge off-screen by the title bar's height, which is the very
    /// failure being prevented.</summary>
    public const int ReservedChromeHeight = 28;

    /// <summary>Width margin subtracted from the display bounds when <c>SDL_GetDisplayUsableBounds</c>
    /// is unavailable (side docks / panels).</summary>
    public const int FallbackMarginWidth = 32;

    /// <summary>Height margin subtracted from the display bounds when
    /// <c>SDL_GetDisplayUsableBounds</c> is unavailable (menu bar + dock/taskbar).</summary>
    public const int FallbackMarginHeight = 96;

    /// <summary>
    /// Computes and applies a window size that fits the player's display, then logs the one boot line.
    /// Call it from the head's constructor (after <c>Logger.Initialize</c>, so the line is not
    /// dropped) INSTEAD of setting <c>PreferredBackBufferWidth/Height</c> by hand.
    /// </summary>
    /// <param name="graphics">The game's graphics device manager; its preferred backbuffer is set and
    /// <c>ApplyChanges</c> is called.</param>
    /// <param name="renderWidth">The width the game renders at (its virtual resolution).</param>
    /// <param name="renderHeight">The height the game renders at (its virtual resolution).</param>
    /// <param name="window">Optional. When given, <c>AllowUserResizing</c> is turned ON for every mode
    /// except <see cref="WindowFitMode.Override"/> — a resizable window is the one macOS clamps to the
    /// screen for you, so it doubles as the second line of defence (and it is what
    /// <see cref="WindowFitMode.Unmeasured"/> falls back on). Override keeps the window fixed, because
    /// the caller asked for an exact size.</param>
    /// <param name="getEnvironmentVariable">Optional environment reader; defaults to
    /// <see cref="PlatformServices.Current"/> (tests inject their own).</param>
    public static WindowFitResult Apply(
        GraphicsDeviceManager graphics,
        int renderWidth,
        int renderHeight,
        GameWindow? window = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        if (graphics == null) throw new ArgumentNullException(nameof(graphics));

        var display = ReadDisplayBounds();
        var usable = Point.Zero;
        var usableFromSystem = false;
        if (display != Point.Zero) usable = ProbeUsableBounds(display, out usableFromSystem);
        var result = Compute(renderWidth, renderHeight, display, usable, usableFromSystem, getEnvironmentVariable);

        graphics.PreferredBackBufferWidth = result.Window.X;
        graphics.PreferredBackBufferHeight = result.Window.Y;
        graphics.ApplyChanges();
        if (window != null) window.AllowUserResizing = result.Mode != WindowFitMode.Override;

        var source = result.Mode == WindowFitMode.Unmeasured
            ? "unavailable"
            : usableFromSystem ? "SDL_GetDisplayUsableBounds" : "fallback margin";
        Logger.Info($"[foundation] WindowFit: render {renderWidth}x{renderHeight}, " +
                    $"display {result.Display.X}x{result.Display.Y}, " +
                    $"usable {result.Usable.X}x{result.Usable.Y} ({source}), " +
                    $"window {result.Window.X}x{result.Window.Y}, mode {result.Mode}.");
        return result;
    }

    /// <summary>
    /// The pure decision behind <see cref="Apply"/>: the environment override wins; otherwise the
    /// window is <see cref="Fit"/> inside <paramref name="usable"/>; a non-positive
    /// <paramref name="usable"/> means the display could not be measured and the render resolution is
    /// applied unchanged. Exposed so a head can reuse the policy without touching the device, and so
    /// it is testable without a graphics device.
    /// </summary>
    public static WindowFitResult Compute(
        int renderWidth,
        int renderHeight,
        Point display,
        Point usable,
        bool usableFromSystem,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        if (renderWidth <= 0) throw new ArgumentOutOfRangeException(nameof(renderWidth));
        if (renderHeight <= 0) throw new ArgumentOutOfRangeException(nameof(renderHeight));

        var getEnv = getEnvironmentVariable ?? (name => PlatformServices.Current.GetEnvironmentVariable(name));
        var raw = getEnv(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(raw))
        {
            if (TryParseSize(raw, out var forced))
                return new WindowFitResult(WindowFitMode.Override, display, usable, forced, usableFromSystem);
            Logger.Warning($"[foundation] WindowFit: ignoring {OverrideVariable}='{raw}' — expected WxH (e.g. 1280x720).");
        }

        if (usable.X <= 0 || usable.Y <= 0)
            return new WindowFitResult(WindowFitMode.Unmeasured, display, Point.Zero,
                new Point(renderWidth, renderHeight), false);

        return new WindowFitResult(WindowFitMode.Fit, display, usable,
            Fit(renderWidth, renderHeight, usable.X, usable.Y), usableFromSystem);
    }

    /// <summary>
    /// The largest window of the render aspect that fits inside the usable area: never magnified past
    /// 1:1 with the render resolution (upscaling only costs sharpness), width snapped DOWN to a
    /// multiple of <paramref name="snapTo"/>, height derived from the render aspect, and
    /// <paramref name="reservedHeight"/> points of the usable height held back for the title bar.
    /// Pure — no device, no environment, no logging.
    /// </summary>
    public static Point Fit(
        int renderWidth,
        int renderHeight,
        int usableWidth,
        int usableHeight,
        int snapTo = SnapTo,
        int reservedHeight = ReservedChromeHeight)
    {
        if (renderWidth <= 0) throw new ArgumentOutOfRangeException(nameof(renderWidth));
        if (renderHeight <= 0) throw new ArgumentOutOfRangeException(nameof(renderHeight));
        if (snapTo < 1) snapTo = 1;

        var availableWidth = Math.Max(snapTo, usableWidth);
        var availableHeight = Math.Max(snapTo, usableHeight - Math.Max(0, reservedHeight));

        // One uniform scale for both axes keeps the aspect; capping it at 1 keeps the window from
        // growing past the render resolution, which would only blur the presented image.
        var scale = Math.Min(1d, Math.Min(availableWidth / (double)renderWidth, availableHeight / (double)renderHeight));

        var width = (int)Math.Floor(renderWidth * scale / snapTo) * snapTo;
        if (width < snapTo) width = Math.Min(snapTo, renderWidth); // absurdly small display: don't emit 0
        if (width > renderWidth) width = renderWidth;

        var height = HeightFor(width, renderWidth, renderHeight);
        // Rounding the derived height can push it a pixel past the box; step the width down until it fits.
        while (height > availableHeight && width > snapTo)
        {
            width -= snapTo;
            height = HeightFor(width, renderWidth, renderHeight);
        }
        return new Point(width, height);
    }

    /// <summary>Parses a <c>WxH</c> size (e.g. <c>"1280x720"</c>, case-insensitive separator).
    /// Both parts must be positive integers.</summary>
    public static bool TryParseSize(string? value, out Point size)
    {
        size = Point.Zero;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Trim().Split('x', 'X');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var w)) return false;
        if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)) return false;
        if (w <= 0 || h <= 0) return false;
        size = new Point(w, h);
        return true;
    }

    /// <summary>
    /// The display area left over after the OS chrome (macOS menu bar + dock, Windows taskbar, Linux
    /// panels), via <c>SDL_GetDisplayUsableBounds</c> on the SDL image DesktopGL already loaded.
    /// Falls back to <paramref name="display"/> minus <see cref="FallbackMarginWidth"/> /
    /// <see cref="FallbackMarginHeight"/> when the export is unavailable — an older SDL, or a backend
    /// that is not SDL at all.
    /// </summary>
    /// <param name="display">The display's full bounds, used for the fallback.</param>
    /// <param name="fromSystem">Whether the returned bounds came from SDL rather than the fallback.</param>
    public static Point ProbeUsableBounds(Point display, out bool fromSystem)
    {
        var bounds = default(SdlRect);
        fromSystem = SdlNative.TryInvoke<SdlGetDisplayUsableBounds>("SDL_GetDisplayUsableBounds", fn =>
        {
            // Display 0 is the primary display, which is the one SDL opens a window on by default.
            if (fn(0, out var probed) != 0) return false;
            if (probed.W <= 0 || probed.H <= 0) return false;
            bounds = probed;
            return true;
        });

        if (fromSystem) return new Point(bounds.W, bounds.H);
        return new Point(Math.Max(1, display.X - FallbackMarginWidth), Math.Max(1, display.Y - FallbackMarginHeight));
    }

    private static int HeightFor(int width, int renderWidth, int renderHeight)
        => Math.Max(1, (int)Math.Round(width * (double)renderHeight / renderWidth, MidpointRounding.AwayFromZero));

    /// <summary>The primary display's bounds, or <see cref="Point.Zero"/> when the backend cannot
    /// report them (no adapter yet, a headless/exotic device). Never throws.</summary>
    private static Point ReadDisplayBounds()
    {
        try
        {
            var mode = GraphicsAdapter.DefaultAdapter?.CurrentDisplayMode;
            if (mode == null || mode.Width <= 0 || mode.Height <= 0) return Point.Zero;
            return new Point(mode.Width, mode.Height);
        }
        catch (Exception e)
        {
            Logger.Warning($"[foundation] WindowFit: display mode unavailable ({e.GetType().Name}: {e.Message}).");
            return Point.Zero;
        }
    }

    // ---- SDL interop (see SdlNative; every call is best-effort and has a fallback) ----

    /// <summary><c>SDL_Rect</c> — four 32-bit ints, blittable.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct SdlRect
    {
        public int X, Y, W, H;
    }

    /// <summary><c>int SDL_GetDisplayUsableBounds(int displayIndex, SDL_Rect *rect)</c> — 0 on success.
    /// Present since SDL 2.0.5; MonoGame never bound it.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SdlGetDisplayUsableBounds(int displayIndex, out SdlRect rect);
}
