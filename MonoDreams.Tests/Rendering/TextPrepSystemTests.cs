using MonoDreams.System.Draw;
using Xunit;

namespace MonoDreams.Tests.Rendering;

/// <summary>
/// Guards the rendering-text premise "The reveal gate is scoped to revealing text" (UX2-G Part 1).
/// Before the fix, <c>TextPrepSystem</c> sliced EVERY text by <c>VisibleCharacterCount</c> and rendered
/// NOTHING when it was ≤ 0 — so a pooled / reassigned chrome label kept a STALE count under the editor's
/// Freeze-gated <c>TextUpdateSystem</c> (the healer that saturates the count for non-revealing text) and
/// rendered truncated ("Dialogu") or blank (inspector rows, the tooltip). The fix scopes the count gate
/// to REVEALING text (<c>RevealingSpeed &gt; 0</c>): static text renders its full content regardless of
/// the count. <see cref="TextPrepSystem.TryGetVisibleText"/> is the pure, font-free decision, so this is
/// a direct TextPrepSystem-level regression (no GraphicsDevice, no world).
/// </summary>
public class TextPrepSystemTests
{
    [Fact]
    public void StaticText_StaleLowCount_RendersFullContent()
    {
        // (a) A non-revealing label reassigned to a longer string keeps a stale short count (3) — the
        // user's "Dialogu" truncation. It must render the FULL content now.
        Assert.True(TextPrepSystem.TryGetVisibleText(
            revealingSpeed: 0f, visibleCharacterCount: 3, "Dialogue", out var visible));
        Assert.Equal("Dialogue", visible);
    }

    [Fact]
    public void StaticText_ZeroCount_NonEmptyContent_RendersFull()
    {
        // (b) A non-revealing label created empty (count defaults to 0) then given content — the blank
        // inspector-row / tooltip case. It must render, not blank.
        Assert.True(TextPrepSystem.TryGetVisibleText(
            revealingSpeed: 0f, visibleCharacterCount: 0, "Inspector", out var visible));
        Assert.Equal("Inspector", visible);
    }

    [Fact]
    public void RevealingText_MidReveal_RespectsTheCount()
    {
        // (c) A configured reveal (RevealingSpeed > 0) still slices by the count — the dialogue
        // typewriter keeps working. This is the behavior the fix must NOT change.
        Assert.True(TextPrepSystem.TryGetVisibleText(
            revealingSpeed: 20f, visibleCharacterCount: 3, "Hello world", out var visible));
        Assert.Equal("Hel", visible);
    }

    [Fact]
    public void RevealingText_NotStarted_RendersNothing()
    {
        // A configured reveal that has not advanced (count 0) still renders nothing — reveal-start
        // behavior preserved (a dialogue line begins hidden and types in).
        Assert.False(TextPrepSystem.TryGetVisibleText(
            revealingSpeed: 20f, visibleCharacterCount: 0, "Hello", out var visible));
        Assert.Equal(string.Empty, visible);
    }

    [Fact]
    public void RevealingText_CountBeyondLength_ClampsToLength()
    {
        Assert.True(TextPrepSystem.TryGetVisibleText(
            revealingSpeed: 20f, visibleCharacterCount: 999, "Hi", out var visible));
        Assert.Equal("Hi", visible);
    }

    [Fact]
    public void EmptyOrNullContent_RendersNothing_RevealingOrNot()
    {
        // (d) Empty/null content renders nothing whether revealing or static.
        Assert.False(TextPrepSystem.TryGetVisibleText(0f, 5, "", out var s1));
        Assert.Equal(string.Empty, s1);
        Assert.False(TextPrepSystem.TryGetVisibleText(20f, 3, "", out var s2));
        Assert.Equal(string.Empty, s2);
        Assert.False(TextPrepSystem.TryGetVisibleText(0f, int.MaxValue, null!, out _));
    }
}
