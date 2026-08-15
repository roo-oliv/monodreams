#nullable enable
using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using MonoDreams.Platform;
using MonoDreams.State;

namespace MonoDreams.Debug;

/// <summary>
/// Hides the OS window of a headless test/verification run. MonoGame DesktopGL has no null
/// graphics device, so "headless" keeps a real window hosting the GL context — but moving it
/// off-screen (<c>Window.Position = (-2000, -2000)</c>, the old trick) is not enough on macOS,
/// where Cocoa clamps window positions back onto the visible screen: spawned test games flashed
/// real windows the user could see and accidentally interact with. <c>SDL_HideWindow</c> removes
/// the window from the screen and the click path entirely while the GL context (and the full-res
/// backbuffer the capture path reads) stays live — the virtual-display behavior xvfb gives CI,
/// achieved locally.
/// </summary>
public static class HeadlessWindow
{
    /// <summary>
    /// Keeps a headless run from STEALING FOCUS at launch. On macOS the focus grab happens at app
    /// activation, during SDL video init — before any window exists to hide — so this must run
    /// BEFORE the <c>Game</c> is constructed (SDL initializes in the <c>Game</c> base ctor; call it
    /// first thing in <c>Program.cs</c>). It sets SDL's <c>SDL_MAC_BACKGROUND_APP</c> hint via its
    /// environment variable, making the app an accessory: no Dock icon, no menu bar, and no
    /// activation — the user's typing is never interrupted by a spawned test game. A no-op on other
    /// platforms (the hint is macOS-only) and never overrides an explicitly-set value.
    /// </summary>
    public static void PreventFocusSteal()
    {
        if (Environment.GetEnvironmentVariable("SDL_MAC_BACKGROUND_APP") == null)
            Environment.SetEnvironmentVariable("SDL_MAC_BACKGROUND_APP", "1");
    }

    /// <summary><c>void SDL_HideWindow(SDL_Window *window)</c>.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SdlHideWindowDelegate(IntPtr window);

    /// <summary>
    /// Hides the game's OS window via <c>SDL_HideWindow</c> (<see cref="GameWindow.Handle"/> IS the
    /// SDL window on DesktopGL). Best-effort and never throws: a platform without SDL (web, or an
    /// exotic backend) is a loud-logged no-op — callers keep their off-screen positioning as the
    /// fallback. Returns whether the window was hidden. Library resolution lives in
    /// <see cref="SdlNative"/>, the engine's single owner of "call an SDL export MonoGame never bound".
    /// </summary>
    public static bool Hide(GameWindow? window)
    {
        if (window == null || window.Handle == IntPtr.Zero) return false;

        var handle = window.Handle;
        var hidden = SdlNative.TryInvoke<SdlHideWindowDelegate>("SDL_HideWindow", hide =>
        {
            hide(handle);
            return true;
        });

        if (hidden) Logger.Info("[debug] Headless: OS window hidden (SDL_HideWindow).");
        else Logger.Warning("[debug] Headless: SDL_HideWindow unavailable — falling back to off-screen positioning.");
        return hidden;
    }
}
