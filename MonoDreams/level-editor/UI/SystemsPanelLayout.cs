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
///
/// <para><b>Device-pixel-ratio scaling.</b> Metric constants are LOGICAL points; every function
/// takes a <c>scale</c> (the viewport manager's <c>DevicePixelRatio</c>, default 1 = the pre-DPR
/// layout byte-identically) that multiplies them into screen pixels — same physical size, denser
/// pixels on a HiDPI backbuffer. See <see cref="EditorChromeLayout"/>.</para>
/// </summary>
public static class SystemsPanelLayout
{
    /// <summary>Height of every panel line (header or entry row), logical points. Compact enough
    /// that the reference composition (~27 lines) fits an ≥720-point-tall window without
    /// scrolling.</summary>
    public const int RowHeight = 22;

    /// <summary>Horizontal padding between the panel edge and its content, logical points.</summary>
    public const int PaddingX = 10;

    /// <summary>Vertical padding above the first line, logical points.</summary>
    public const int PaddingY = 8;

    /// <summary>The enabled-toggle checkbox is a square of this size, logical points.</summary>
    public const int CheckboxSize = 12;

    /// <summary>Gap between the checkbox and the row label, logical points.</summary>
    public const int CheckboxGap = 8;

    /// <summary>Horizontal indent per tree depth level, logical points: a group's children shift
    /// one step right of the group row (the registrar's <c>EditorPipelineEntry.Depth</c>).</summary>
    public const int IndentPerDepth = 14;

    /// <summary>The collapse/expand caret glyph box, logical points. Reserved as a column at the
    /// left of every row (section headers, pipeline groups, inspector components) so a collapsible
    /// row's caret and a non-collapsible row's content stay vertically aligned.</summary>
    public const int CaretSize = 10;

    /// <summary>Gap between the caret column and the checkbox/label, logical points.</summary>
    public const int CaretGap = 6;

    /// <summary>The reserved caret column width (caret box + gap), logical points. Every row's
    /// checkbox/label is shifted right by this so carets and content align across row kinds.</summary>
    public const int CaretColumn = CaretSize + CaretGap;

    /// <summary>The indeterminate (mixed) minus bar inside a group checkbox, logical points (the
    /// Gmail/Material partial-selection mark).</summary>
    public const int MinusBarWidth = 8;

    /// <summary>See <see cref="MinusBarWidth"/>.</summary>
    public const int MinusBarHeight = 2;

    /// <summary>Lines scrolled per mouse-wheel notch (a notch = 120 wheel units).</summary>
    public const int LinesPerNotch = 3;

    private static int Px(int points, float scale) => EditorChromeLayout.Px(points, scale);

    /// <summary>The content area inside the panel rectangle (padding removed).</summary>
    public static Rectangle ContentArea(Rectangle panel, float scale = 1f) => new(
        panel.X + Px(PaddingX, scale),
        panel.Y + Px(PaddingY, scale),
        Math.Max(1, panel.Width - Px(PaddingX, scale) * 2),
        Math.Max(1, panel.Height - Px(PaddingY, scale) * 2));

    /// <summary>How many whole lines fit the panel (never less than 1).</summary>
    public static int VisibleLineCount(Rectangle panel, float scale = 1f) =>
        Math.Max(1, ContentArea(panel, scale).Height / Px(RowHeight, scale));

    /// <summary>The maximum line-scroll offset for <paramref name="totalLines"/> lines.</summary>
    public static int MaxScroll(int totalLines, Rectangle panel, float scale = 1f) =>
        Math.Max(0, totalLines - VisibleLineCount(panel, scale));

    /// <summary>Clamps a line-scroll offset into <c>[0, MaxScroll]</c>.</summary>
    public static int ClampScroll(int scroll, int totalLines, Rectangle panel, float scale = 1f) =>
        Math.Clamp(scroll, 0, MaxScroll(totalLines, panel, scale));

    /// <summary>Wheel delta → signed line-scroll delta (wheel up = negative lines = scroll up).</summary>
    public static int ScrollLines(int wheelDelta) => -wheelDelta / 120 * LinesPerNotch;

    /// <summary>The rectangle of the line at <paramref name="visibleIndex"/> (0 = topmost visible).</summary>
    public static Rectangle LineRect(Rectangle panel, int visibleIndex, float scale = 1f)
    {
        var content = ContentArea(panel, scale);
        var row = Px(RowHeight, scale);
        return new Rectangle(content.X, content.Y + visibleIndex * row, content.Width, row);
    }

    /// <summary>The collapse/expand caret box inside a row, vertically centered and indented
    /// <paramref name="depth"/> tree levels — it sits at the row's left edge, before the checkbox.
    /// Rendered only for collapsible rows (section headers, pipeline groups, inspector components);
    /// the column is reserved on every row so content aligns.</summary>
    public static Rectangle CaretRect(Rectangle line, int depth = 0, float scale = 1f)
    {
        var size = Px(CaretSize, scale);
        return new Rectangle(
            line.X + depth * Px(IndentPerDepth, scale),
            line.Y + (Px(RowHeight, scale) - size) / 2,
            size, size);
    }

    /// <summary>The checkbox square inside an entry row's line rectangle, vertically centered,
    /// indented <paramref name="depth"/> tree levels and shifted past the reserved caret column.</summary>
    public static Rectangle CheckboxRect(Rectangle line, int depth = 0, float scale = 1f)
    {
        var size = Px(CheckboxSize, scale);
        return new Rectangle(
            line.X + depth * Px(IndentPerDepth, scale) + Px(CaretColumn, scale),
            line.Y + (Px(RowHeight, scale) - size) / 2,
            size, size);
    }

    /// <summary>The indeterminate minus bar, centered inside a checkbox rectangle.</summary>
    public static Rectangle MinusBarRect(Rectangle checkbox, float scale = 1f)
    {
        var w = Px(MinusBarWidth, scale);
        var h = Px(MinusBarHeight, scale);
        return new Rectangle(
            checkbox.X + (checkbox.Width - w) / 2,
            checkbox.Y + (checkbox.Height - h) / 2,
            w, h);
    }

    /// <summary>Top-left of an entry row's label (after the reserved caret column + the checkbox),
    /// vertically centered for a label of <paramref name="labelHeight"/> px, indented
    /// <paramref name="depth"/> levels — the pipeline-entry (checkbox-bearing) rows.</summary>
    public static Vector2 LabelPosition(Rectangle line, float labelHeight, int depth = 0, float scale = 1f) => new(
        line.X + depth * Px(IndentPerDepth, scale) + Px(CaretColumn, scale) + Px(CheckboxSize, scale) + Px(CheckboxGap, scale),
        line.Y + (Px(RowHeight, scale) - labelHeight) / 2f);

    /// <summary>Top-left of a label for a checkbox-less row (section header, scene entity, inspector
    /// component/member) — after the reserved caret column, indented <paramref name="depth"/> levels.
    /// A collapsible such row draws its caret in the caret column (<see cref="CaretRect"/>).</summary>
    public static Vector2 ContentLabelPosition(Rectangle line, float labelHeight, int depth = 0, float scale = 1f) => new(
        line.X + depth * Px(IndentPerDepth, scale) + Px(CaretColumn, scale),
        line.Y + (Px(RowHeight, scale) - labelHeight) / 2f);

    /// <summary>Top-left of a section sub-header label (UPDATE / DRAW inside SYSTEMS) — flush left,
    /// no caret column.</summary>
    public static Vector2 HeaderPosition(Rectangle line, float labelHeight, float scale = 1f) => new(
        line.X, line.Y + (Px(RowHeight, scale) - labelHeight) / 2f);

    /// <summary>Where hidden (scrolled-out) lines are parked: far off-screen, so their meshes and
    /// text are GPU-clipped without any per-entity blanking (the mesh prep keeps rebuilding them
    /// at the parked position, which never intersects the window).</summary>
    public static readonly Vector2 ParkedPosition = new(-100000f, -100000f);
}
