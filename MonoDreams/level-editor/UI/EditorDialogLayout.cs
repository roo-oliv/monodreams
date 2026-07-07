#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// Pure layout math for the editor's Save / Load <b>file-system navigator</b> dialog, in
/// <b>physical screen pixels</b> (the dialog is chrome on <c>RenderTargetID.Editor</c>, native window
/// resolution — see <see cref="EditorChromeLayout"/>). The dialog is a Blender-style directory
/// browser: a title, a <b>breadcrumb</b> row with an <b>up</b> button, a scrollable list of
/// <b>folder + scene-file rows</b>, (in Save mode) a <b>filename field</b>, and the confirm/cancel
/// buttons. Every function takes a <c>scale</c> (the viewport manager's device-pixel ratio) and
/// multiplies the logical-point metrics by it, so the dialog keeps its physical size on a HiDPI
/// backbuffer. World-free and cursor-free so it is unit-testable like
/// <see cref="EditorChromeLayout"/> / <see cref="SystemsPanelLayout"/>.
/// </summary>
public static class EditorDialogLayout
{
    /// <summary>Dialog panel width, logical points.</summary>
    public const int PanelWidth = 520;

    /// <summary>Load-dialog panel height, logical points (title + breadcrumb + list + button row).</summary>
    public const int LoadPanelHeight = 420;

    /// <summary>Save-dialog panel height, logical points (Load + a filename field row).</summary>
    public const int SavePanelHeight = 470;

    /// <summary>Inner padding, logical points.</summary>
    public const int Padding = 18;

    /// <summary>Title text row height, logical points.</summary>
    public const int TitleHeight = 26;

    /// <summary>Breadcrumb / up-button row height, logical points.</summary>
    public const int BreadcrumbHeight = 26;

    /// <summary>The up-directory button width, logical points (docked at the breadcrumb row's right).</summary>
    public const int UpButtonWidth = 52;

    /// <summary>Name-field height, logical points (Save mode).</summary>
    public const int FieldHeight = 34;

    /// <summary>Dialog button height / width / gap, logical points.</summary>
    public const int ButtonHeight = 30;
    public const int ButtonWidth = 96;
    public const int ButtonGap = 12;

    /// <summary>List row height (folder / file), logical points.</summary>
    public const int RowHeight = 26;

    /// <summary>A logical-point metric in screen pixels at <paramref name="scale"/>, rounded.</summary>
    public static int Px(int points, float scale) => (int)MathF.Round(points * scale);

    /// <summary>The full-window modal backdrop rectangle.</summary>
    public static Rectangle Backdrop(int screenWidth, int screenHeight) =>
        new(0, 0, Math.Max(1, screenWidth), Math.Max(1, screenHeight));

    /// <summary>The centred dialog panel for the given kind (Load is shorter; Save adds a field row).</summary>
    public static Rectangle Panel(int screenWidth, int screenHeight, bool isLoad, float scale = 1f)
    {
        var w = Px(PanelWidth, scale);
        var h = Px(isLoad ? LoadPanelHeight : SavePanelHeight, scale);
        return new Rectangle((screenWidth - w) / 2, (screenHeight - h) / 2, w, h);
    }

    /// <summary>The title text position (top-left of the title row, inside the padding).</summary>
    public static Vector2 Title(Rectangle panel, float scale) =>
        new(panel.X + Px(Padding, scale), panel.Y + Px(Padding, scale));

    /// <summary>The top Y of the breadcrumb row (below the title).</summary>
    private static int BreadcrumbTop(Rectangle panel, float scale) =>
        panel.Y + Px(Padding, scale) + Px(TitleHeight, scale) + Px(Padding, scale) / 2;

    /// <summary>The breadcrumb (current-path) rectangle — the left part of the breadcrumb row, left of
    /// the up button.</summary>
    public static Rectangle Breadcrumb(Rectangle panel, float scale)
    {
        var pad = Px(Padding, scale);
        var upW = Px(UpButtonWidth, scale) + pad / 2;
        return new Rectangle(panel.X + pad, BreadcrumbTop(panel, scale),
            panel.Width - pad * 2 - upW, Px(BreadcrumbHeight, scale));
    }

    /// <summary>The up-directory button rectangle (docked at the breadcrumb row's right).</summary>
    public static Rectangle UpButton(Rectangle panel, float scale)
    {
        var pad = Px(Padding, scale);
        var w = Px(UpButtonWidth, scale);
        return new Rectangle(panel.Right - pad - w, BreadcrumbTop(panel, scale), w, Px(BreadcrumbHeight, scale));
    }

    /// <summary>Where a row/button's text is drawn, left-inset and vertically centred for a glyph run
    /// of <paramref name="lineHeight"/> px.</summary>
    public static Vector2 TextInset(Rectangle rect, float lineHeight, float scale) =>
        new(rect.X + Px(8, scale), rect.Y + (rect.Height - lineHeight) / 2f);

    /// <summary>The Save dialog's filename-field rectangle (above the button row).</summary>
    public static Rectangle Field(Rectangle panel, float scale)
    {
        var pad = Px(Padding, scale);
        var h = Px(FieldHeight, scale);
        var top = panel.Bottom - pad - Px(ButtonHeight, scale) - pad / 2 - h;
        return new Rectangle(panel.X + pad, top, panel.Width - pad * 2, h);
    }

    /// <summary>Where the field's text (and trailing caret) is drawn, inset inside the field box.</summary>
    public static Vector2 FieldText(Rectangle field, float scale)
    {
        var inset = Px(6, scale);
        var textH = Px(TitleHeight, scale);
        return new Vector2(field.X + inset, field.Y + (field.Height - textH) / 2f);
    }

    /// <summary>The confirm button (Save), docked bottom-right inside the panel.</summary>
    public static Rectangle ConfirmButton(Rectangle panel, float scale)
    {
        var pad = Px(Padding, scale);
        var w = Px(ButtonWidth, scale);
        var h = Px(ButtonHeight, scale);
        return new Rectangle(panel.Right - pad - w, panel.Bottom - pad - h, w, h);
    }

    /// <summary>The cancel button, left of the confirm button (Load reuses it as its only button).</summary>
    public static Rectangle CancelButton(Rectangle panel, bool isLoad, float scale)
    {
        var pad = Px(Padding, scale);
        var w = Px(ButtonWidth, scale);
        var h = Px(ButtonHeight, scale);
        var right = isLoad
            ? panel.Right - pad                                      // Load: cancel is the rightmost button
            : ConfirmButton(panel, scale).X - Px(ButtonGap, scale);  // Save: cancel sits left of Save
        return new Rectangle(right - w, panel.Bottom - pad - h, w, h);
    }

    /// <summary>The top Y of the scrollable folder/file list region (below the breadcrumb row).</summary>
    public static int ListTop(Rectangle panel, float scale) =>
        BreadcrumbTop(panel, scale) + Px(BreadcrumbHeight, scale) + Px(Padding, scale) / 2;

    /// <summary>The bottom Y of the list region — above the button row (Load) or above the filename
    /// field (Save, which reserves an extra field row).</summary>
    public static int ListBottom(Rectangle panel, bool isSave, float scale)
    {
        var pad = Px(Padding, scale);
        var bottom = panel.Bottom - pad - Px(ButtonHeight, scale) - pad / 2;
        if (isSave) bottom -= Px(FieldHeight, scale) + pad / 2;
        return bottom;
    }

    /// <summary>How many list rows fit in the region for the given mode.</summary>
    public static int VisibleRowCount(Rectangle panel, bool isSave, float scale)
    {
        var region = ListBottom(panel, isSave, scale) - ListTop(panel, scale);
        var rowH = Px(RowHeight, scale);
        return rowH <= 0 ? 0 : Math.Max(0, region / rowH);
    }

    /// <summary>The rectangle of the <paramref name="visibleIndex"/>-th visible list row.</summary>
    public static Rectangle Row(Rectangle panel, int visibleIndex, float scale)
    {
        var pad = Px(Padding, scale);
        var rowH = Px(RowHeight, scale);
        return new Rectangle(panel.X + pad, ListTop(panel, scale) + visibleIndex * rowH,
            panel.Width - pad * 2, rowH);
    }

    /// <summary>Where the "no project root" / "empty folder" message is drawn (inside the list region).</summary>
    public static Vector2 Message(Rectangle panel, float scale) =>
        new(panel.X + Px(Padding, scale), ListTop(panel, scale) + Px(4, scale));
}
