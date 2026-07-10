#nullable enable

namespace MonoDreams.LevelEditor.UI;

/// <summary>The severity of a transient editor notification — maps to an <see cref="EditorTheme"/>
/// intent color when the status bar renders it (Info/Success/Warning/Danger).</summary>
public enum EditorNotifySeverity
{
    Info,
    Success,
    Warning,
    Danger,
}

/// <summary>
/// The transient <b>notification seam</b> (PF-F): a one-line message the editor's user-action sites
/// raise (a save refusal, a prefab confirmation, a guardrail hint, the multi-root message) AS WELL AS
/// logging it, so the designer sees WHY something happened without tailing the log. The status bar's
/// LEFT side shows the current notification (severity-colored) for <see cref="DisplaySeconds"/>, then
/// falls back to the normal contextual status; a <b>modal transform readout still wins</b> (the modal is
/// the live-editing readout). <b>Newest wins</b> — a fresh <see cref="Notify"/> replaces the current one
/// and resets the clock.
///
/// <para>Pure model (ECS purity — no world, no font, no GraphicsDevice), unit-testable directly. The
/// <see cref="EditorStatusBarSystem"/> owns rendering: it <see cref="Tick"/>s the clock once per frame
/// and reads <see cref="TryGetCurrent"/>. Messages are ASCII-only (the chrome bitmap font has no
/// <c>Δ</c>/<c>×</c>/<c>…</c>) — callers keep the notification text plain (the richer Logger line may
/// use anything).</para>
/// </summary>
public sealed class EditorNotifications
{
    /// <summary>How long (seconds) a notification stays on the status bar before the normal status
    /// returns. ~4.5s — long enough to read, short enough not to mask the contextual status.</summary>
    public const float DisplaySeconds = 4.5f;

    private string? _message;
    private EditorNotifySeverity _severity;
    private float _remaining;

    /// <summary>Raises a transient notification (newest wins — replaces any current one and resets the
    /// display clock). An empty message is ignored (nothing to show).</summary>
    public void Notify(string message, EditorNotifySeverity severity = EditorNotifySeverity.Info)
    {
        if (string.IsNullOrEmpty(message)) return;
        _message = message;
        _severity = severity;
        _remaining = DisplaySeconds;
    }

    /// <summary>Advances the display clock by one frame's <paramref name="elapsedSeconds"/>; when it
    /// runs out the notification clears (the normal status returns). The status bar calls this once per
    /// frame. A non-positive elapsed is treated as zero (no time passes — safe at a paused first frame).</summary>
    public void Tick(float elapsedSeconds)
    {
        if (_remaining <= 0f) return;
        _remaining -= elapsedSeconds > 0f ? elapsedSeconds : 0f;
        if (_remaining <= 0f)
        {
            _remaining = 0f;
            _message = null;
        }
    }

    /// <summary>The current notification when one is active, else false. The status bar shows it on the
    /// LEFT (unless a modal readout is active) in the severity color.</summary>
    public bool TryGetCurrent(out string message, out EditorNotifySeverity severity)
    {
        if (_remaining > 0f && _message != null)
        {
            message = _message;
            severity = _severity;
            return true;
        }
        message = string.Empty;
        severity = EditorNotifySeverity.Info;
        return false;
    }

    /// <summary>Whether a notification is currently showing (the display clock has not run out).</summary>
    public bool HasActive => _remaining > 0f && _message != null;
}
