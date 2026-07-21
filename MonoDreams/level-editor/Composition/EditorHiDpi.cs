#nullable enable
using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// Device-pixel-ratio (HiDPI / Retina) support for the editor run configuration — the Flutter-style
/// "render at the device's real resolution" path. On macOS, MonoGame DesktopGL (3.8.4) creates its
/// SDL window <b>without</b> <c>SDL_WINDOW_ALLOW_HIGHDPI</c> (verified against the shipped
/// assembly: <c>SdlGameWindow.CreateWindow</c> passes flags <c>0x60A</c> — OpenGL | Hidden |
/// InputFocus | MouseFocus), so the GL drawable is allocated at the window's LOGICAL point size and
/// the OS upscales it ~2× onto the Retina panel — every pixel the game draws, including the
/// editor's "native-resolution" chrome, is blurred by that upscale. This helper flips the window's
/// backing to device resolution at runtime, entirely from the host side:
///
/// <list type="number">
///   <item>find the game's <c>NSWindow</c> (AppKit, via <c>objc_msgSend</c> — no SDL interop
///   needed) and read <c>backingScaleFactor</c> (the device-pixel ratio);</item>
///   <item>set <c>wantsBestResolutionOpenGLSurface = YES</c> on the content view, which re-backs
///   the GL surface at device pixels (the same switch SDL's own <c>ALLOW_HIGHDPI</c> flag turns
///   on at window creation), and <c>[NSOpenGLContext update]</c> so the live context picks the new
///   backing up;</item>
///   <item>widen <see cref="Microsoft.Xna.Framework.Graphics.PresentationParameters"/> to the
///   device size — <b>without</b> <c>GraphicsDeviceManager.ApplyChanges()</c>, which would resize
///   the OS window itself (MonoGame sets the SDL window size from the backbuffer size). The
///   window keeps its logical size; only the surface behind it gains pixels.</item>
/// </list>
///
/// <para>The host then feeds the returned device size into its renderer sizing (the
/// <c>ViewportManager</c> screen size + <c>DevicePixelRatio</c>) and re-invokes this on window
/// resize (idempotent — the backing switch sticks; only the sizes are recomputed). Mouse input
/// stays in logical points (SDL reports window coordinates), which is why
/// <c>CursorInputSystem</c> scales <c>ScreenPosition</c> by the viewport manager's
/// <c>DevicePixelRatio</c> — keeping the invariant that <c>ScreenPosition</c>, chrome layout, and
/// the backbuffer all share ONE space (physical device pixels).</para>
///
/// <para><b>Scope.</b> Editor runs only (the hosts call this under the editor run flag): the game
/// itself renders to virtual-resolution targets either way, so this changes nothing about the
/// game's look — it only sharpens the editor chrome/overlays (and the final composite's
/// filtering). Non-macOS desktops are a no-op with a log line (on Windows/Linux SDL without DPI
/// awareness the OS already hands the window physical-or-scaled pixels uniformly; the ratio
/// backbuffer/client is 1). The web head has its own DPR story (canvas
/// <c>devicePixelRatio</c> — a documented follow-up); this class self-guards with
/// <see cref="OperatingSystem.IsMacOS"/> so it compiles and no-ops there. Kill switch:
/// <c>MONODREAMS_EDITOR_HIDPI=0</c>.</para>
/// </summary>
public static class EditorHiDpi
{
    /// <summary>Env var kill switch: set to <c>0</c>/<c>false</c> to keep the logical-resolution
    /// backbuffer (e.g. to compare sharpness or to bisect a rendering issue).</summary>
    public const string KillSwitchVariable = "MONODREAMS_EDITOR_HIDPI";

    /// <summary>The outcome of <see cref="TryEnable"/>: whether the device-resolution backing was
    /// applied, the device-pixel ratio, and the resulting backbuffer size in device pixels.</summary>
    public readonly record struct Result(bool Applied, float Scale, int Width, int Height);

    /// <summary>
    /// Requests a device-resolution backbuffer for the game's window (see the class doc).
    /// Idempotent — call once after the <c>GraphicsDevice</c> exists and again from the window
    /// resize handler. Returns <c>Applied = false</c> (and logs why) when the platform can't or
    /// needn't re-back: non-macOS, a 1.0 backing scale, the kill switch, or any AppKit failure —
    /// the caller then keeps its ordinary logical-size path.
    /// </summary>
    public static Result TryEnable(Game game, Func<string, string?>? getEnvironmentVariable = null)
    {
        var getEnv = getEnvironmentVariable ?? global::System.Environment.GetEnvironmentVariable;
        var kill = getEnv(KillSwitchVariable);
        if (kill is "0" or "false" or "False")
        {
            Logger.Info("[level-editor] HiDPI: disabled by MONODREAMS_EDITOR_HIDPI.");
            return new Result(false, 1f, 0, 0);
        }

        var client = game.Window.ClientBounds;
        var pp = game.GraphicsDevice.PresentationParameters;
        Logger.Info($"[level-editor] HiDPI probe: window client={client.Width}x{client.Height}, " +
                    $"backbuffer={pp.BackBufferWidth}x{pp.BackBufferHeight}, " +
                    $"viewport={game.GraphicsDevice.Viewport.Width}x{game.GraphicsDevice.Viewport.Height}.");

        if (!OperatingSystem.IsMacOS())
        {
            Logger.Info("[level-editor] HiDPI: non-macOS — backbuffer already matches the window's pixels; nothing to do.");
            return new Result(false, 1f, 0, 0);
        }

        try
        {
            var window = FindMainNsWindow();
            if (window == IntPtr.Zero)
            {
                Logger.Warning("[level-editor] HiDPI: no NSWindow found; keeping the logical-resolution backbuffer.");
                return new Result(false, 1f, 0, 0);
            }

            var scale = (float)SendDouble(window, Sel("backingScaleFactor"));
            Logger.Info($"[level-editor] HiDPI: NSWindow backingScaleFactor={scale}.");
            if (scale <= 1f)
                return new Result(false, 1f, 0, 0); // non-Retina display: logical == device

            var contentView = SendPtr(window, Sel("contentView"));
            if (contentView == IntPtr.Zero)
            {
                Logger.Warning("[level-editor] HiDPI: NSWindow has no contentView; keeping the logical backbuffer.");
                return new Result(false, 1f, 0, 0);
            }

            // The Retina switch (what SDL_WINDOW_ALLOW_HIGHDPI would have set at creation), then
            // refresh the live GL context so it re-reads the view's backing size.
            SendVoidBool(contentView, Sel("setWantsBestResolutionOpenGLSurface:"), 1);
            var glContext = SendPtr(ObjcClass("NSOpenGLContext"), Sel("currentContext"));
            if (glContext != IntPtr.Zero)
                SendVoid(glContext, Sel("update"));
            else
                Logger.Warning("[level-editor] HiDPI: no current NSOpenGLContext to update — the backing switch may need a resize to take.");

            // Size from the CONTENT VIEW's live bounds, not Game.Window.ClientBounds — MonoGame's
            // cached client size lags the real window early in startup (it reports the pre-resize
            // default until the platform applies the preferred size), and a stale size here would
            // allocate a mismatched backbuffer.
            var bounds = ViewBounds(contentView);
            Logger.Info($"[level-editor] HiDPI: contentView bounds={bounds.Width}x{bounds.Height} points.");
            if (bounds.Width < 1 || bounds.Height < 1)
            {
                Logger.Warning("[level-editor] HiDPI: degenerate contentView bounds; keeping the logical backbuffer.");
                return new Result(false, 1f, 0, 0);
            }

            var deviceWidth = (int)Math.Round(bounds.Width * scale);
            var deviceHeight = (int)Math.Round(bounds.Height * scale);

            // Widen the presentation parameters directly. GraphicsDevice.SetRenderTarget(null)
            // reads these for the default-framebuffer viewport, so the final composite now covers
            // the full device-pixel drawable. Deliberately NOT ApplyChanges(): that would push the
            // backbuffer size back into the SDL window size, physically growing the window.
            pp.BackBufferWidth = deviceWidth;
            pp.BackBufferHeight = deviceHeight;

            Logger.Info($"[level-editor] HiDPI: device-resolution backbuffer applied — " +
                        $"{deviceWidth}x{deviceHeight} device px behind a {bounds.Width}x{bounds.Height} point window (scale {scale}).");
            return new Result(true, scale, deviceWidth, deviceHeight);
        }
        catch (Exception e)
        {
            Logger.Warning($"[level-editor] HiDPI: AppKit interop failed ({e.GetType().Name}: {e.Message}); keeping the logical backbuffer.");
            return new Result(false, 1f, 0, 0);
        }
    }

    // ---- Minimal Objective-C runtime interop (macOS only; never invoked elsewhere) ----

    private const string ObjcLib = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjcLib, EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjcClass(string name);

    [DllImport(ObjcLib, EntryPoint = "sel_registerName")]
    private static extern IntPtr Sel(string name);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(IntPtr receiver, IntPtr selector);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidBool(IntPtr receiver, IntPtr selector, sbyte value);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendPtrIndex(IntPtr receiver, IntPtr selector, nuint index);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern nuint SendNuint(IntPtr receiver, IntPtr selector);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern double SendDouble(IntPtr receiver, IntPtr selector);

    /// <summary>CoreGraphics <c>CGRect</c>/AppKit <c>NSRect</c> (4 doubles, blittable).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NsRect
    {
        public double X, Y, Width, Height;
    }

    // Struct-returning objc_msgSend: arm64 returns a 4-double HFA in registers (plain
    // objc_msgSend); x86_64 returns large structs via a hidden pointer (objc_msgSend_stret).
    [DllImport(ObjcLib, EntryPoint = "objc_msgSend")]
    private static extern NsRect SendRect(IntPtr receiver, IntPtr selector);

    [DllImport(ObjcLib, EntryPoint = "objc_msgSend_stret")]
    private static extern void SendRectStret(out NsRect result, IntPtr receiver, IntPtr selector);

    /// <summary>An NSView's <c>bounds</c> in points, architecture-aware (see the msgSend pair).</summary>
    private static NsRect ViewBounds(IntPtr view)
    {
        var selector = Sel("bounds");
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            SendRectStret(out var rect, view, selector);
            return rect;
        }
        return SendRect(view, selector);
    }

    /// <summary>The game's <c>NSWindow</c>: the first window of the shared NSApplication that has
    /// a content view (SDL creates exactly one for a MonoGame DesktopGL game).</summary>
    private static IntPtr FindMainNsWindow()
    {
        var app = SendPtr(ObjcClass("NSApplication"), Sel("sharedApplication"));
        if (app == IntPtr.Zero) return IntPtr.Zero;
        var windows = SendPtr(app, Sel("windows"));
        if (windows == IntPtr.Zero) return IntPtr.Zero;
        var count = SendNuint(windows, Sel("count"));
        for (nuint i = 0; i < count; i++)
        {
            var candidate = SendPtrIndex(windows, Sel("objectAtIndex:"), i);
            if (candidate != IntPtr.Zero && SendPtr(candidate, Sel("contentView")) != IntPtr.Zero)
                return candidate;
        }
        return IntPtr.Zero;
    }
}
