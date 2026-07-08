#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// Pure layout math for the editor's modal dialogs, in <b>physical screen pixels</b> (the dialog is
/// chrome on <c>RenderTargetID.Editor</c>, native window resolution — see <see cref="EditorChromeLayout"/>).
/// Two live shapes (UX-D):
/// <list type="bullet">
///   <item><b>Save</b> — a title over three stacked full-width action rows (Save Scene / Save Project /
///   Save Backup As…), each a title + subtitle line, and a bottom Cancel button. When the backup action
///   is armed a name field + a Confirm button are revealed above Cancel (the panel grows to fit them).</item>
///   <item><b>Confirm-on-switch</b> (UX-C) — a title + message + one row of three equal-width buttons
///   (Save &amp; Switch / Discard &amp; Switch / Cancel).</item>
/// </list>
/// Every function takes a <c>scale</c> (the viewport manager's device-pixel ratio) and multiplies the
/// logical-point metrics by it, so the dialog keeps its physical size on a HiDPI backbuffer. World-free
/// and cursor-free so it is unit-testable like <see cref="EditorChromeLayout"/> / <see cref="SystemsPanelLayout"/>.
/// </summary>
public static class EditorDialogLayout
{
    /// <summary>Dialog panel width, logical points.</summary>
    public const int PanelWidth = 460;

    /// <summary>Confirm-on-switch panel height, logical points (title + message + one button row) —
    /// the modal shown when a Scenes-panel switch would discard unsaved edits (UX-C §3.3).</summary>
    public const int ConfirmPanelHeight = 168;

    /// <summary>Inner padding, logical points.</summary>
    public const int Padding = 18;

    /// <summary>Title text row height, logical points.</summary>
    public const int TitleHeight = 26;

    /// <summary>A Save action row's height, logical points (a title line + a subtitle line).</summary>
    public const int ActionHeight = 58;

    /// <summary>Gap between the three Save action rows (and before the revealed field), logical points.</summary>
    public const int ActionGap = 10;

    /// <summary>The number of stacked Save actions (Save Scene / Save Project / Save Backup As…).</summary>
    public const int SaveActionCount = 3;

    /// <summary>Backup name-field height, logical points (shown only when the backup action is armed).</summary>
    public const int FieldHeight = 34;

    /// <summary>Dialog button height / width / gap, logical points.</summary>
    public const int ButtonHeight = 30;
    public const int ButtonWidth = 110;
    public const int ButtonGap = 12;

    /// <summary>Where an action row's title line is drawn (top line), left-inset.</summary>
    private const int ActionTitleOffsetY = 9;

    /// <summary>Where an action row's subtitle line is drawn (below the title), left-inset.</summary>
    private const int ActionSubtitleOffsetY = 31;

    /// <summary>Left inset of an action row's text, logical points.</summary>
    private const int ActionTextInsetX = 12;

    /// <summary>A logical-point metric in screen pixels at <paramref name="scale"/>, rounded.</summary>
    public static int Px(int points, float scale) => (int)MathF.Round(points * scale);

    /// <summary>The full-window modal backdrop rectangle.</summary>
    public static Rectangle Backdrop(int screenWidth, int screenHeight) =>
        new(0, 0, Math.Max(1, screenWidth), Math.Max(1, screenHeight));

    /// <summary>The title text position (top-left of the title row, inside the padding).</summary>
    public static Vector2 Title(Rectangle panel, float scale) =>
        new(panel.X + Px(Padding, scale), panel.Y + Px(Padding, scale));

    /// <summary>Where a row/button's text is drawn, left-inset and vertically centred for a glyph run
    /// of <paramref name="lineHeight"/> px.</summary>
    public static Vector2 TextInset(Rectangle rect, float lineHeight, float scale) =>
        new(rect.X + Px(8, scale), rect.Y + (rect.Height - lineHeight) / 2f);

    // ─── the three-action Save dialog (UX-D) ─────────────────────────────────────────────────────

    /// <summary>The Save panel's logical-point height for the given backup state (armed reveals the
    /// name field + a Confirm button above Cancel, so the panel grows by a field row).</summary>
    private static int SavePanelHeightPoints(bool backupActive)
    {
        var h = Padding + TitleHeight + Padding                          // top pad + title + gap
              + SaveActionCount * ActionHeight + (SaveActionCount - 1) * ActionGap // the action rows
              + Padding + ButtonHeight + Padding;                        // gap + button row + bottom pad
        if (backupActive) h += ActionGap + FieldHeight;                  // the revealed field row
        return h;
    }

    /// <summary>The centred Save panel (taller when the backup name field is revealed).</summary>
    public static Rectangle SavePanel(int screenWidth, int screenHeight, bool backupActive, float scale = 1f)
    {
        var w = Px(PanelWidth, scale);
        var h = Px(SavePanelHeightPoints(backupActive), scale);
        return new Rectangle((screenWidth - w) / 2, (screenHeight - h) / 2, w, h);
    }

    /// <summary>The rectangle of Save action row <paramref name="index"/> (0 = Save Scene, 1 = Save
    /// Project, 2 = Save Backup As…), full inner width, stacked below the title.</summary>
    public static Rectangle SaveAction(Rectangle panel, int index, float scale)
    {
        var pad = Px(Padding, scale);
        var top = panel.Y + pad + Px(TitleHeight, scale) + pad
                  + index * (Px(ActionHeight, scale) + Px(ActionGap, scale));
        return new Rectangle(panel.X + pad, top, panel.Width - pad * 2, Px(ActionHeight, scale));
    }

    /// <summary>The action row's title-line text position (top line).</summary>
    public static Vector2 ActionTitle(Rectangle action, float scale) =>
        new(action.X + Px(ActionTextInsetX, scale), action.Y + Px(ActionTitleOffsetY, scale));

    /// <summary>The action row's subtitle-line text position (below the title).</summary>
    public static Vector2 ActionSubtitle(Rectangle action, float scale) =>
        new(action.X + Px(ActionTextInsetX, scale), action.Y + Px(ActionSubtitleOffsetY, scale));

    /// <summary>The backup name-field rectangle (revealed below the action rows when backup is armed).</summary>
    public static Rectangle BackupField(Rectangle panel, float scale)
    {
        var pad = Px(Padding, scale);
        var top = SaveAction(panel, SaveActionCount - 1, scale).Bottom + Px(ActionGap, scale);
        return new Rectangle(panel.X + pad, top, panel.Width - pad * 2, Px(FieldHeight, scale));
    }

    /// <summary>Where the field's text (and trailing caret) is drawn, inset inside the field box.</summary>
    public static Vector2 FieldText(Rectangle field, float scale)
    {
        var inset = Px(6, scale);
        var textH = Px(TitleHeight, scale);
        return new Vector2(field.X + inset, field.Y + (field.Height - textH) / 2f);
    }

    /// <summary>The Cancel button, docked bottom-right (always present in the Save dialog).</summary>
    public static Rectangle SaveCancelButton(Rectangle panel, float scale)
    {
        var pad = Px(Padding, scale);
        var w = Px(ButtonWidth, scale);
        var h = Px(ButtonHeight, scale);
        return new Rectangle(panel.Right - pad - w, panel.Bottom - pad - h, w, h);
    }

    /// <summary>The backup Confirm button, left of Cancel (shown only when the backup name field is armed).</summary>
    public static Rectangle BackupConfirmButton(Rectangle panel, float scale)
    {
        var w = Px(ButtonWidth, scale);
        var h = Px(ButtonHeight, scale);
        var cancel = SaveCancelButton(panel, scale);
        return new Rectangle(cancel.X - Px(ButtonGap, scale) - w, panel.Bottom - Px(Padding, scale) - h, w, h);
    }

    // ─── confirm-on-switch modal (UX-C) ────────────────────────────────────────────────────────

    /// <summary>The centred confirm-on-switch panel (title + message + one button row).</summary>
    public static Rectangle ConfirmPanel(int screenWidth, int screenHeight, float scale = 1f)
    {
        var w = Px(PanelWidth, scale);
        var h = Px(ConfirmPanelHeight, scale);
        return new Rectangle((screenWidth - w) / 2, (screenHeight - h) / 2, w, h);
    }

    /// <summary>Where the confirm dialog's body message is drawn (below the title).</summary>
    public static Vector2 ConfirmMessage(Rectangle panel, float scale) =>
        new(panel.X + Px(Padding, scale),
            panel.Y + Px(Padding, scale) + Px(TitleHeight, scale) + Px(Padding, scale));

    /// <summary>The confirm dialog's three bottom-row buttons, equal width across the inner panel:
    /// <c>[0]</c> = the primary action (Save &amp; Switch), <c>[1]</c> = Discard &amp; Switch,
    /// <c>[2]</c> = Cancel.</summary>
    public static Rectangle[] ConfirmButtons(Rectangle panel, float scale)
    {
        var pad = Px(Padding, scale);
        var gap = Px(ButtonGap, scale);
        var h = Px(ButtonHeight, scale);
        var y = panel.Bottom - pad - h;
        var inner = panel.Width - pad * 2;
        var w = (inner - gap * 2) / 3;
        var x0 = panel.X + pad;
        return new[]
        {
            new Rectangle(x0, y, w, h),
            new Rectangle(x0 + w + gap, y, w, h),
            new Rectangle(x0 + 2 * (w + gap), y, w, h),
        };
    }
}
