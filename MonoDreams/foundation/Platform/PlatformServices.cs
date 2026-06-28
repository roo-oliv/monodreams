using System;

namespace MonoDreams.Platform;

/// <summary>
/// Static holder for the active <see cref="IPlatformServices"/>. Engine modules read
/// <see cref="Current"/> instead of touching <c>File</c> / <c>Directory</c> /
/// <c>AppDomain</c> / <c>Environment</c> / <c>Console</c> directly.
///
/// It defaults to <see cref="DesktopPlatformServices"/> so a desktop head, and every
/// existing test, behaves exactly as before with no setup. A non-desktop head (web)
/// assigns its own implementation to <see cref="Current"/> at the very start of
/// startup — before <c>Logger.Initialize</c> or any system construction — so the
/// platform is selected by the head, never baked into MonoDreams source.
///
/// The holder is a deliberate static singleton, matching the existing
/// <c>Logger</c>/<c>SettingsManager</c> patterns: it keeps the portability seam from
/// forcing an <see cref="IPlatformServices"/> parameter through every system
/// constructor (which would churn the whole engine). It is set once at startup and
/// read-only thereafter.
/// </summary>
public static class PlatformServices
{
    private static IPlatformServices _current = new DesktopPlatformServices();

    /// <summary>The active platform services. Never null; defaults to desktop.</summary>
    public static IPlatformServices Current
    {
        get => _current;
        set => _current = value ?? throw new ArgumentNullException(nameof(value));
    }
}
