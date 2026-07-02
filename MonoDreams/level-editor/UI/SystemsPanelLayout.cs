#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// Pure layout math for the editor's <b>systems panel</b> — the pipeline listing that lives in the
/// shell's right strip (<see cref="EditorChromeLayout.RightPanel"/>), in <b>physical screen
/// pixels</b> like the rest of the chrome. The panel is a vertical list of fixed-height lines
/// (section headers + one row per registrar entry, each row = checkbox + label) with an integer
/// line-scroll: only fully visible lines are shown (a partially scrolled line would bleed over the
/// top/bottom bars, which share the Editor target), so scrolling moves whole lines. World-free and
/// cursor-free, unit-testable like <see cref="EditorChromeLayout"/>.
/// </summary>
public static class SystemsPanelLayout
{
    /// <summary>Height of every panel line (header or entry row), px. Compact enough that the
    /// reference composition (~27 lines) fits an ≥720px-tall window without scrolling.</summary>
    public const int RowHeight = 22;

    /// <summary>Horizontal padding between the panel edge and its content, px.</summary>
    public const int PaddingX = 10;

    /// <summary>Vertical padding above the first line, px.</summary>
    public const int PaddingY = 8;

    /// <summary>The enabled-toggle checkbox is a square of this size, px.</summary>
    public const int CheckboxSize = 12;

    /// <summary>Gap between the checkbox and the row label, px.</summary>
    public const int CheckboxGap = 8;

    /// <summary>Lines scrolled per mouse-wheel notch (a notch = 120 wheel units).</summary>
    public const int LinesPerNotch = 3;

    /// <summary>The content area inside the panel rectangle (padding removed).</summary>
    public static Rectangle ContentArea(Rectangle panel) => new(
        panel.X + PaddingX,
        panel.Y + PaddingY,
        Math.Max(1, panel.Width - PaddingX * 2),
        Math.Max(1, panel.Height - PaddingY * 2));

    /// <summary>How many whole lines fit the panel (never less than 1).</summary>
    public static int VisibleLineCount(Rectangle panel) =>
        Math.Max(1, ContentArea(panel).Height / RowHeight);

    /// <summary>The maximum line-scroll offset for <paramref name="totalLines"/> lines.</summary>
    public static int MaxScroll(int totalLines, Rectangle panel) =>
        Math.Max(0, totalLines - VisibleLineCount(panel));

    /// <summary>Clamps a line-scroll offset into <c>[0, MaxScroll]</c>.</summary>
    public static int ClampScroll(int scroll, int totalLines, Rectangle panel) =>
        Math.Clamp(scroll, 0, MaxScroll(totalLines, panel));

    /// <summary>Wheel delta → signed line-scroll delta (wheel up = negative lines = scroll up).</summary>
    public static int ScrollLines(int wheelDelta) => -wheelDelta / 120 * LinesPerNotch;

    /// <summary>The rectangle of the line at <paramref name="visibleIndex"/> (0 = topmost visible).</summary>
    public static Rectangle LineRect(Rectangle panel, int visibleIndex)
    {
        var content = ContentArea(panel);
        return new Rectangle(content.X, content.Y + visibleIndex * RowHeight, content.Width, RowHeight);
    }

    /// <summary>The checkbox square inside an entry row's line rectangle, vertically centered.</summary>
    public static Rectangle CheckboxRect(Rectangle line) => new(
        line.X, line.Y + (RowHeight - CheckboxSize) / 2, CheckboxSize, CheckboxSize);

    /// <summary>Top-left of an entry row's label (after the checkbox), vertically centered for
    /// a label of <paramref name="labelHeight"/> px.</summary>
    public static Vector2 LabelPosition(Rectangle line, float labelHeight) => new(
        line.X + CheckboxSize + CheckboxGap, line.Y + (RowHeight - labelHeight) / 2f);

    /// <summary>Top-left of a section header label (no checkbox indent).</summary>
    public static Vector2 HeaderPosition(Rectangle line, float labelHeight) => new(
        line.X, line.Y + (RowHeight - labelHeight) / 2f);

    /// <summary>Where hidden (scrolled-out) lines are parked: far off-screen, so their meshes and
    /// text are GPU-clipped without any per-entity blanking (the mesh prep keeps rebuilding them
    /// at the parked position, which never intersects the window).</summary>
    public static readonly Vector2 ParkedPosition = new(-100000f, -100000f);
}
