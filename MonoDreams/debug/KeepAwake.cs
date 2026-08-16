#nullable enable
using System;
using System.Runtime.InteropServices;
using MonoDreams.Platform;
using MonoDreams.State;

namespace MonoDreams.Debug;

/// <summary>
/// Holds a macOS <b>power-management activity assertion</b> for as long as the returned token lives —
/// the in-process equivalent of leaving <c>caffeinate -disu</c> running next to the game.
///
/// <para><b>The footgun this exists for.</b> An unattended run on macOS is not left alone by the OS:
/// App Nap throttles a process whose window is hidden or occluded (which every headless run is), and
/// display/idle sleep suspends the whole app. The observed failure is not a crash — the process
/// simply stops making progress, and a three-hour agent run was found hung inside
/// <c>Cocoa_GL_SwapWindow</c>, i.e. blocked mid-present with no log line to explain it. There is
/// nothing to see in the debug directory afterwards: the last capture is from the moment the machine
/// dozed off.</para>
///
/// <para><b>Opt-in only.</b> <see cref="FromEnvironment"/> reads <c>MONODREAMS_KEEP_AWAKE=1</c> and
/// returns <c>null</c> when the environment did not ask, so an ordinary game run never asserts
/// anything about the user's power settings. It is a no-op — a logged one — on every non-macOS
/// platform: Windows (<c>SetThreadExecutionState</c>) and Linux (systemd/DBus inhibitors) have their
/// own mechanisms, and the browser has none at all beyond a user-gesture-gated Wake Lock, so none of
/// them is covered here.</para>
/// </summary>
public static class KeepAwake
{
    /// <summary>The environment variable that asks for the assertion (<c>1</c> or <c>true</c>).</summary>
    public const string EnvironmentVariable = "MONODREAMS_KEEP_AWAKE";

    // NSActivityOptions, from <Foundation/NSProcessInfo.h>. Kept as literals because there is no
    // header to bind against from managed code — they are ABI, not implementation detail.
    private const ulong NSActivityIdleDisplaySleepDisabled = 1UL << 40;
    private const ulong NSActivityIdleSystemSleepDisabled = 1UL << 20;
    private const ulong NSActivityUserInitiated = 0x00FFFFFFUL | NSActivityIdleSystemSleepDisabled;

    /// <summary>
    /// User-initiated work that also keeps the display awake: idle system sleep off, idle display
    /// sleep off, and App Nap suppressed (App Nap only throttles processes that are NOT doing
    /// user-initiated work). That trio is what <c>caffeinate -disu</c> buys from the command line.
    /// </summary>
    private const ulong UnattendedRunOptions = NSActivityUserInitiated | NSActivityIdleDisplaySleepDisabled;

    /// <summary>
    /// Begins the assertion if <c>MONODREAMS_KEEP_AWAKE</c> asks for it, else returns <c>null</c>.
    /// Dispose the token to release it (hosts do it in their own <c>Dispose</c>); letting the process
    /// exit releases it too, because the assertion dies with the process that holds it.
    ///
    /// <list type="bullet">
    ///   <item><i>unset</i>, <c>0</c>, <c>off</c>, <c>false</c> — nothing, silently.</item>
    ///   <item><c>1</c>, <c>true</c>, <c>on</c> — assert (macOS), or log the no-op (elsewhere).</item>
    ///   <item>anything else — <c>Logger.Error</c> naming the valid values, then nothing.</item>
    /// </list>
    /// </summary>
    public static IDisposable? FromEnvironment()
    {
        var requested = PlatformServices.Current.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(requested)) return null;

        switch (requested.Trim().ToLowerInvariant())
        {
            case "0":
            case "off":
            case "false":
                return null;
            case "1":
            case "on":
            case "true":
                return Begin("MonoDreams unattended run (MONODREAMS_KEEP_AWAKE=1)");
            default:
                Logger.Error($"{EnvironmentVariable}='{requested}' is not a keep-awake setting — the run " +
                             "may still be suspended by App Nap or display sleep. Valid values: 1, off.");
                return null;
        }
    }

    /// <summary>
    /// Begins an NSProcessInfo activity for <paramref name="reason"/> (which appears in
    /// <c>pmset -g assertions</c>) and returns the token that ends it. Returns <c>null</c> — after a
    /// log line saying so — when the platform is not macOS or the Objective-C runtime is unreachable.
    /// Never throws: a run that cannot assert must still run.
    /// </summary>
    public static IDisposable? Begin(string reason)
    {
        if (!OperatingSystem.IsMacOS())
        {
            Logger.Info("[debug] Keep-awake requested, but the assertion is macOS-only — no-op here. " +
                        "On Windows/Linux keep the machine awake outside the process (power settings, " +
                        "systemd-inhibit); a browser tab cannot be kept awake from engine code at all.");
            return null;
        }

        try
        {
            var activity = ObjC.BeginActivity(UnattendedRunOptions, reason);
            if (activity == IntPtr.Zero)
            {
                Logger.Warning("[debug] Keep-awake: NSProcessInfo did not return an activity token — " +
                               "the run is NOT protected from App Nap or display sleep.");
                return null;
            }

            Logger.Info("[debug] Keep-awake: NSProcessInfo activity held (idle system sleep, idle display " +
                        "sleep and App Nap disabled) — the in-process equivalent of `caffeinate -disu`.");
            return new Activity(activity);
        }
        catch (Exception e)
        {
            // A missing/blocked Objective-C runtime is a reason to log and carry on, never to take the
            // run down: the assertion is a comfort, the run is the point.
            Logger.Warning($"[debug] Keep-awake unavailable ({e.GetType().Name}: {e.Message}) — the run is " +
                           "NOT protected from App Nap or display sleep.");
            return null;
        }
    }

    /// <summary>The live assertion. Idempotent: a second <see cref="Dispose"/> does nothing.</summary>
    private sealed class Activity(IntPtr token) : IDisposable
    {
        private IntPtr _token = token;

        public void Dispose()
        {
            if (_token == IntPtr.Zero) return;
            var token = _token;
            _token = IntPtr.Zero;
            try
            {
                ObjC.EndActivity(token);
                Logger.Info("[debug] Keep-awake: NSProcessInfo activity released.");
            }
            catch (Exception e)
            {
                Logger.Warning($"[debug] Keep-awake: releasing the activity failed ({e.GetType().Name}: " +
                               $"{e.Message}). The process exit releases it regardless.");
            }
        }
    }

    /// <summary>
    /// The three Objective-C runtime entry points this needs, resolved through
    /// <see cref="NativeLibrary"/> rather than <c>DllImport</c> — the same shape
    /// <c>HeadlessWindow</c> uses for SDL, and for the same two reasons: a platform without the
    /// library is a caught miss instead of an unresolved import, and nothing is left for a WASM
    /// publish to try to bind at build time.
    /// </summary>
    private static class ObjC
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr LookupDelegate([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MsgSendDelegate(IntPtr receiver, IntPtr selector);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MsgSendUtf8Delegate(IntPtr receiver, IntPtr selector,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string argument);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MsgSendBeginActivityDelegate(IntPtr receiver, IntPtr selector,
            ulong options, IntPtr reason);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MsgSendPointerDelegate(IntPtr receiver, IntPtr selector, IntPtr argument);

        private const string LibObjC = "/usr/lib/libobjc.dylib";

        /// <summary>NSProcessInfo lives in Foundation, which a plain console process (a test host, say)
        /// has not necessarily loaded — so it is dlopen'd on demand before the class lookup.</summary>
        private const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";

        private static IntPtr _library;
        private static LookupDelegate? _getClass;
        private static LookupDelegate? _registerSelector;
        private static IntPtr _msgSend;

        private static bool Load()
        {
            if (_library != IntPtr.Zero) return true;
            if (!NativeLibrary.TryLoad(LibObjC, out var library)
                && !NativeLibrary.TryLoad("libobjc.dylib", out library)) return false;

            if (!NativeLibrary.TryGetExport(library, "objc_getClass", out var getClass)
                || !NativeLibrary.TryGetExport(library, "sel_registerName", out var registerSelector)
                || !NativeLibrary.TryGetExport(library, "objc_msgSend", out var msgSend))
                return false;

            NativeLibrary.TryLoad(Foundation, out _);

            _getClass = Marshal.GetDelegateForFunctionPointer<LookupDelegate>(getClass);
            _registerSelector = Marshal.GetDelegateForFunctionPointer<LookupDelegate>(registerSelector);
            _msgSend = msgSend;
            _library = library;
            return true;
        }

        private static T MsgSend<T>() where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(_msgSend);

        /// <summary>
        /// <c>[[[NSProcessInfo processInfo] beginActivityWithOptions:options reason:reason] retain]</c>.
        /// The token is RETAINED because the returned object is autoreleased: it must outlive the
        /// current autorelease pool, or the activity ends at the next run-loop turn — silently, which
        /// is the worst way for a keep-awake to fail. Zero when the runtime is unreachable.
        /// </summary>
        public static IntPtr BeginActivity(ulong options, string reason)
        {
            if (!Load()) return IntPtr.Zero;

            var processInfoClass = _getClass!("NSProcessInfo");
            var stringClass = _getClass("NSString");
            if (processInfoClass == IntPtr.Zero || stringClass == IntPtr.Zero) return IntPtr.Zero;

            var processInfo = MsgSend<MsgSendDelegate>()(processInfoClass, _registerSelector!("processInfo"));
            var reasonString = MsgSend<MsgSendUtf8Delegate>()(
                stringClass, _registerSelector("stringWithUTF8String:"), reason);
            if (processInfo == IntPtr.Zero || reasonString == IntPtr.Zero) return IntPtr.Zero;

            var activity = MsgSend<MsgSendBeginActivityDelegate>()(
                processInfo, _registerSelector("beginActivityWithOptions:reason:"), options, reasonString);
            if (activity == IntPtr.Zero) return IntPtr.Zero;

            return MsgSend<MsgSendDelegate>()(activity, _registerSelector("retain"));
        }

        /// <summary><c>[[NSProcessInfo processInfo] endActivity:token]; [token release];</c> — the
        /// mirror of <see cref="BeginActivity"/>, including the release of the retain it took.</summary>
        public static void EndActivity(IntPtr activity)
        {
            if (!Load() || activity == IntPtr.Zero) return;

            var processInfoClass = _getClass!("NSProcessInfo");
            if (processInfoClass == IntPtr.Zero) return;

            var processInfo = MsgSend<MsgSendDelegate>()(processInfoClass, _registerSelector!("processInfo"));
            if (processInfo != IntPtr.Zero)
                MsgSend<MsgSendPointerDelegate>()(processInfo, _registerSelector("endActivity:"), activity);
            MsgSend<MsgSendDelegate>()(activity, _registerSelector("release"));
        }
    }
}
