#nullable enable
using System;
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
    private delegate void SdlHideWindowDelegate(IntPtr window);

    /// <summary>Per-OS names of the SDL native library MonoGame DesktopGL already loaded into the
    /// process — TryLoad resolves to the loaded image, so this adds no second SDL.</summary>
    private static readonly string[] SdlLibraryNames =
        { "SDL2.dll", "libSDL2-2.0.so.0", "libSDL2.dylib", "SDL2" };

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
            foreach (var name in SdlLibraryNames)
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
