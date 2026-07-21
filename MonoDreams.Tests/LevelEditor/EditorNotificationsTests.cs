using MonoDreams.LevelEditor.UI;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// The pure transient-notification model (PF-F): newest-wins replacement, a bounded display clock the
/// status bar ticks, and the current-notification read the status bar renders. No world / font / device.
/// </summary>
public class EditorNotificationsTests
{
    [Fact]
    public void Notify_ThenTryGetCurrent_ReturnsMessageAndSeverity()
    {
        var n = new EditorNotifications();
        Assert.False(n.HasActive);
        Assert.False(n.TryGetCurrent(out _, out _));

        n.Notify("Created prefab 'npc' (6 entities)", EditorNotifySeverity.Success);

        Assert.True(n.HasActive);
        Assert.True(n.TryGetCurrent(out var message, out var severity));
        Assert.Equal("Created prefab 'npc' (6 entities)", message);
        Assert.Equal(EditorNotifySeverity.Success, severity);
    }

    [Fact]
    public void EmptyMessage_IsIgnored()
    {
        var n = new EditorNotifications();
        n.Notify("", EditorNotifySeverity.Danger);
        Assert.False(n.HasActive);
        Assert.False(n.TryGetCurrent(out _, out _));
    }

    [Fact]
    public void Tick_ExpiresAfterDisplaySeconds_ThenFallsBack()
    {
        var n = new EditorNotifications();
        n.Notify("selection appears empty - nothing captured", EditorNotifySeverity.Warning);

        // Not yet expired.
        n.Tick(EditorNotifications.DisplaySeconds - 0.1f);
        Assert.True(n.HasActive);
        Assert.True(n.TryGetCurrent(out _, out _));

        // Past the display window → clears (the normal status returns).
        n.Tick(0.2f);
        Assert.False(n.HasActive);
        Assert.False(n.TryGetCurrent(out _, out _));
    }

    [Fact]
    public void Notify_NewestWins_ReplacesAndResetsTheClock()
    {
        var n = new EditorNotifications();
        n.Notify("first", EditorNotifySeverity.Info);
        n.Tick(EditorNotifications.DisplaySeconds - 0.05f); // nearly expired

        n.Notify("second", EditorNotifySeverity.Danger); // newest wins, clock reset

        Assert.True(n.TryGetCurrent(out var message, out var severity));
        Assert.Equal("second", message);
        Assert.Equal(EditorNotifySeverity.Danger, severity);

        // The reset clock means the OLD near-expiry does not carry over.
        n.Tick(0.1f);
        Assert.True(n.HasActive);
        Assert.Equal("second", (n.TryGetCurrent(out var m, out _), m).m);
    }

    [Fact]
    public void Tick_NonPositiveElapsed_DoesNotAdvance()
    {
        var n = new EditorNotifications();
        n.Notify("held", EditorNotifySeverity.Info);
        n.Tick(0f);
        n.Tick(-1f);
        Assert.True(n.HasActive);
    }
}
