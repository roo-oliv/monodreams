#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
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

    private delegate void SdlHideWindowDelegate(IntPtr window);

    /// <summary>The SDL images MonoGame DesktopGL may have loaded, MonoGame's own shipped names
    /// first (macOS: <c>libSDL2-2.0.0.dylib</c> under <c>runtimes/osx/native/</c> — the bare
    /// <c>libSDL2.dylib</c> alone silently missed it and Hide fell back to positioning). Each name
    /// is probed bare (dlopen search + the already-loaded image), then in the app directory, then
    /// in the deps <c>runtimes/&lt;rid&gt;/native</c> folder. dlopen of an already-loaded image
    /// returns it ref-counted — this never loads a second SDL.</summary>
    private static IEnumerable<string> SdlCandidates()
    {
        string[] names = OperatingSystem.IsMacOS()
            ? new[] { "libSDL2-2.0.0.dylib", "libSDL2.dylib" }
            : OperatingSystem.IsWindows()
                ? new[] { "SDL2.dll" }
                : new[] { "libSDL2-2.0.so.0", "libSDL2-2.0.so", "libSDL2.so" };
        var rid = OperatingSystem.IsMacOS() ? "osx"
            : OperatingSystem.IsWindows() ? (Environment.Is64BitProcess ? "win-x64" : "win-x86")
            : "linux-" + (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64");
        foreach (var name in names)
        {
            yield return name;
            yield return Path.Combine(AppContext.BaseDirectory, name);
            yield return Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", name);
        }
    }

    /// <summary>
    /// Hides the game's OS window via <c>SDL_HideWindow</c> (<see cref="GameWindow.Handle"/> IS the
    /// SDL window on DesktopGL). Best-effort and never throws: a platform without SDL (web, or an
    /// exotic backend) is a loud-logged no-op — callers keep their off-screen positioning as the
    /// fallback. Returns whether the window was hidden.
    /// </summary>
    public static bool Hide(GameWindow? window)
    {
        if (window == null || window.Handle == IntPtr.Zero) return false;
        try
        {
            foreach (var name in SdlCandidates())
            {
                if (!NativeLibrary.TryLoad(name, out var lib)) continue;
                try
                {
                    if (!NativeLibrary.TryGetExport(lib, "SDL_HideWindow", out var export)) continue;
                    Marshal.GetDelegateForFunctionPointer<SdlHideWindowDelegate>(export)(window.Handle);
                    Logger.Info("[debug] Headless: OS window hidden (SDL_HideWindow).");
                    return true;
                }
                finally
                {
                    // Balances TryLoad's ref count; SDL itself stays loaded (MonoGame owns it).
                    NativeLibrary.Free(lib);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Warning($"[debug] Headless: SDL_HideWindow unavailable ({e.GetType().Name}: " +
                           $"{e.Message}) — falling back to off-screen positioning.");
            return false;
        }
        Logger.Warning("[debug] Headless: no SDL library found to hide the window — " +
                       "falling back to off-screen positioning.");
        return false;
    }
}
