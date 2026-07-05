#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// Pure layout math for the editor's Save / Load dialogs, in <b>physical screen pixels</b> (the
/// dialog is chrome on <c>RenderTargetID.Editor</c>, native window resolution — see
/// <see cref="EditorChromeLayout"/>). Every function takes a <c>scale</c> (the viewport manager's
/// device-pixel ratio) and multiplies the logical-point metrics by it, so the dialog keeps its
/// physical size on a HiDPI backbuffer, matching the rest of the shell. World-free and cursor-free
/// so it is unit-testable like <see cref="EditorChromeLayout"/> / <see cref="SystemsPanelLayout"/>.
/// </summary>
public static class EditorDialogLayout
{
    /// <summary>Dialog panel width, logical points.</summary>
    public const int PanelWidth = 480;

    /// <summary>Save-dialog panel height, logical points (title + one field + a button row).</summary>
    public const int SavePanelHeight = 176;

    /// <summary>Load-dialog panel height, logical points (title + a scrollable list + a button row).</summary>
    public const int LoadPanelHeight = 360;

    /// <summary>Inner padding, logical points.</summary>
    public const int Padding = 18;

    /// <summary>Title text row height, logical points.</summary>
    public const int TitleHeight = 26;

    /// <summary>Name-field height, logical points.</summary>
    public const int FieldHeight = 34;

    /// <summary>Dialog button height / width / gap, logical points.</summary>
    public const int ButtonHeight = 30;
    public const int ButtonWidth = 96;
    public const int ButtonGap = 12;

    /// <summary>Load-list row height, logical points.</summary>
    public const int RowHeight = 30;

    /// <summary>A logical-point metric in screen pixels at <paramref name="scale"/>, rounded.</summary>
    public static int Px(int points, float scale) => (int)MathF.Round(points * scale);

    /// <summary>The full-window modal backdrop rectangle.</summary>
    public static Rectangle Backdrop(int screenWidth, int screenHeight) =>
        new(0, 0, Math.Max(1, screenWidth), Math.Max(1, screenHeight));

    /// <summary>The centred dialog panel for the given kind.</summary>
    public static Rectangle Panel(int screenWidth, int screenHeight, bool isLoad, float scale = 1f)
    {
        var w = Px(PanelWidth, scale);
        var h = Px(isLoad ? LoadPanelHeight : SavePanelHeight, scale);
        return new Rectangle((screenWidth - w) / 2, (screenHeight - h) / 2, w, h);
    }

    /// <summary>The title text position (top-left of the title row, inside the padding).</summary>
    public static Vector2 Title(Rectangle panel, float scale) =>
        new(panel.X + Px(Padding, scale), panel.Y + Px(Padding, scale));

    /// <summary>The Save dialog's name-field rectangle.</summary>
    public static Rectangle Field(Rectangle panel, float scale)
    {
        var pad = Px(Padding, scale);
        var top = panel.Y + pad + Px(TitleHeight, scale) + pad / 2;
        return new Rectangle(panel.X + pad, top, panel.Width - pad * 2, Px(FieldHeight, scale));
    }

    /// <summary>Where the field's text (and trailing caret) is drawn, inset inside the field box.</summary>
    public static Vector2 FieldText(Rectangle field, float scale)
    {
        var inset = Px(6, scale);
        // Vertically centre a ~one-line glyph run inside the field box.
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

    /// <summary>The top Y of the Load dialog's scrollable list region (below the title).</summary>
    public static int ListTop(Rectangle panel, float scale) =>
        panel.Y + Px(Padding, scale) + Px(TitleHeight, scale) + Px(Padding, scale) / 2;

    /// <summary>The bottom Y of the list region (above the button row).</summary>
    public static int ListBottom(Rectangle panel, float scale) =>
        panel.Bottom - Px(Padding, scale) - Px(ButtonHeight, scale) - Px(Padding, scale) / 2;

    /// <summary>How many list rows fit in the region.</summary>
    public static int VisibleRowCount(Rectangle panel, float scale)
    {
        var region = ListBottom(panel, scale) - ListTop(panel, scale);
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

    /// <summary>Where the "no project root" / "no scenes" message is drawn (inside the list region).</summary>
    public static Vector2 Message(Rectangle panel, float scale) =>
        new(panel.X + Px(Padding, scale), ListTop(panel, scale) + Px(4, scale));
}
