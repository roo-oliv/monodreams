#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MonoDreams.State;

namespace MonoDreams.Platform;

/// <summary>
/// Best-effort access to exports of the SDL library the DesktopGL backend has <b>already loaded</b>.
///
/// <para>MonoGame's DesktopGL platform is SDL2 underneath, but it binds only a subset of SDL's API
/// and exposes none of the handle plumbing. When the engine needs a call MonoGame never bound
/// (<c>SDL_HideWindow</c> for the headless window, <c>SDL_GetDisplayUsableBounds</c> for the
/// menu-bar-aware display area), the cheapest correct route is to look the export up on the SDL
/// image that is already in the process and invoke it through a delegate. <c>dlopen</c> of an
/// already-loaded image returns it ref-counted, so this never loads a <i>second</i> SDL.</para>
///
/// <para><b>Best-effort by contract.</b> Every entry point returns <c>false</c> instead of throwing
/// when SDL is absent (a web/WASM head, an exotic backend) or when the export is missing (an older
/// SDL). Callers must always carry a fallback — the engine never depends on an SDL call succeeding.
/// This type is the single owner of SDL library resolution in the engine; do not open SDL by hand
/// anywhere else.</para>
/// </summary>
public static class SdlNative
{
    /// <summary>
    /// Resolves <paramref name="exportName"/> on the loaded SDL image and hands it to
    /// <paramref name="call"/> as a delegate of type <typeparamref name="TDelegate"/>.
    /// </summary>
    /// <param name="exportName">The SDL C export, e.g. <c>"SDL_GetDisplayUsableBounds"</c>.</param>
    /// <param name="call">
    /// Invoked with the resolved function pointer; returns whether the native call SUCCEEDED. It runs
    /// while the library reference is held, so it is safe to invoke the delegate from inside it (and
    /// only from inside it — the delegate must not outlive the callback).
    /// </param>
    /// <returns>
    /// <c>true</c> only when the export was found AND <paramref name="call"/> reported success;
    /// <c>false</c> for "no SDL", "no such export", "the native call failed", or any exception.
    /// </returns>
    public static bool TryInvoke<TDelegate>(string exportName, Func<TDelegate, bool> call)
        where TDelegate : Delegate
    {
        if (string.IsNullOrEmpty(exportName) || call == null) return false;
        // WASM has no dynamic native loading and no SDL; skip the probe entirely rather than
        // walking a candidate list that can only fail.
        if (OperatingSystem.IsBrowser()) return false;

        try
        {
            foreach (var candidate in Candidates())
            {
                if (!NativeLibrary.TryLoad(candidate, out var lib)) continue;
                try
                {
                    if (!NativeLibrary.TryGetExport(lib, exportName, out var export)) continue;
                    return call(Marshal.GetDelegateForFunctionPointer<TDelegate>(export));
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
            Logger.Warning($"[foundation] SDL: '{exportName}' unavailable ({e.GetType().Name}: {e.Message}).");
        }
        return false;
    }

    /// <summary>
    /// The SDL images MonoGame DesktopGL may have loaded, MonoGame's own shipped names first (macOS:
    /// <c>libSDL2-2.0.0.dylib</c> under <c>runtimes/osx/native/</c> — probing the bare
    /// <c>libSDL2.dylib</c> alone silently missed it). Each name is tried bare (dlopen search + the
    /// already-loaded image), then in the app directory, then in the deps
    /// <c>runtimes/&lt;rid&gt;/native</c> folder.
    /// </summary>
    private static IEnumerable<string> Candidates()
    {
        var services = PlatformServices.Current;
        string[] names = OperatingSystem.IsMacOS()
            ? new[] { "libSDL2-2.0.0.dylib", "libSDL2.dylib" }
            : OperatingSystem.IsWindows()
                ? new[] { "SDL2.dll" }
                : new[] { "libSDL2-2.0.so.0", "libSDL2-2.0.so", "libSDL2.so" };
        var rid = OperatingSystem.IsMacOS() ? "osx"
            : OperatingSystem.IsWindows()
                ? (RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "win-x86" : "win-x64")
                : "linux-" + (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64");

        foreach (var name in names)
        {
            yield return name;
            yield return services.CombinePath(services.BaseDirectory, name);
            yield return services.CombinePath(services.BaseDirectory, "runtimes", rid, "native", name);
        }
    }
}
