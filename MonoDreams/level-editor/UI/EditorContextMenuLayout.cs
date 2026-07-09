#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.UI;

/// <summary>
/// Pure layout math for the editor's context menus (UX2-D), in <b>physical screen pixels</b> (the menu
/// is chrome on <c>RenderTargetID.Editor</c>, native window resolution — see
/// <see cref="EditorChromeLayout"/>). A menu is a fixed-width vertical list of item rows (with thinner
/// separator rows), opened at the cursor or anchored below a header button, and <b>clamped to the
/// window</b> so it never spills off-screen; a ONE-level submenu opens BESIDE its parent item (to the
/// right, flipping to the left when there is no room). Every function takes a <c>scale</c> (the viewport
/// manager's device-pixel ratio) and multiplies the logical-point metrics by it, so the menu keeps its
/// physical size on a HiDPI backbuffer. World-free and cursor-free so it is unit-testable like
/// <see cref="EditorDialogLayout"/>.
/// </summary>
public static class EditorContextMenuLayout
{
    /// <summary>A regular item row height, logical points.</summary>
    public const int ItemHeight = 26;

    /// <summary>A separator row height, logical points (a thin divider centred in the row).</summary>
    public const int SeparatorHeight = 9;

    /// <summary>Fixed menu width, logical points (labels fit; a fixed width keeps the layout pure).</summary>
    public const int MenuWidth = 176;

    /// <summary>Top/bottom padding inside the menu box, logical points.</summary>
    public const int VerticalPadding = 4;

    /// <summary>Left inset of an item's label, logical points.</summary>
    public const int TextInsetX = 12;

    /// <summary>Right inset of a submenu item's ▸ caret, logical points (from the row's right edge).</summary>
    public const int CaretInsetX = 16;

    /// <summary>A logical-point metric in screen pixels at <paramref name="scale"/>, rounded.</summary>
    public static int Px(int points, float scale) => (int)MathF.Round(points * scale);

    /// <summary>The pixel height of a single item by kind (a separator is shorter).</summary>
    public static int RowHeight(EditorMenuItemKind kind, float scale) =>
        Px(kind == EditorMenuItemKind.Separator ? SeparatorHeight : ItemHeight, scale);

    /// <summary>The total pixel height of a menu box for <paramref name="items"/> (rows + top/bottom
    /// padding).</summary>
    public static int MenuHeight(IReadOnlyList<EditorMenuItem> items, float scale)
    {
        var h = Px(VerticalPadding, scale) * 2;
        foreach (var item in items) h += RowHeight(item.Kind, scale);
        return h;
    }

    /// <summary>
    /// The menu box for <paramref name="items"/> opened with its top-left at <paramref name="anchor"/>,
    /// <b>clamped</b> so the whole box stays inside <c>[0, screenWidth] × [0, screenHeight]</c> (it
    /// shifts left / up at the right / bottom edges, and never goes negative). Used for both the
    /// cursor-anchored context menu and the below-a-button dropdown (the caller passes the button's
    /// bottom-left as the anchor).
    /// </summary>
    public static Rectangle MenuRect(Point anchor, IReadOnlyList<EditorMenuItem> items,
        int screenWidth, int screenHeight, float scale)
    {
        var w = Px(MenuWidth, scale);
        var h = MenuHeight(items, scale);
        var x = Math.Min(anchor.X, Math.Max(0, screenWidth - w));
        var y = Math.Min(anchor.Y, Math.Max(0, screenHeight - h));
        x = Math.Max(0, x);
        y = Math.Max(0, y);
        return new Rectangle(x, y, w, h);
    }

    /// <summary>The rectangle of item <paramref name="index"/> inside <paramref name="menu"/> (rows
    /// stacked below the top padding; a separator row is shorter).</summary>
    public static Rectangle ItemRect(Rectangle menu, IReadOnlyList<EditorMenuItem> items, int index, float scale)
    {
        var y = menu.Y + Px(VerticalPadding, scale);
        for (var i = 0; i < index && i < items.Count; i++)
            y += RowHeight(items[i].Kind, scale);
        var h = RowHeight(items[index].Kind, scale);
        return new Rectangle(menu.X, y, menu.Width, h);
    }

    /// <summary>
    /// The submenu box for <paramref name="subItems"/> opened BESIDE the parent item
    /// <paramref name="parentItem"/> of <paramref name="parentMenu"/>: to the RIGHT of the parent menu
    /// by default, flipping to the LEFT when there is no room, aligned to the parent item's top and
    /// clamped vertically. The child list uses the same fixed <see cref="MenuWidth"/>.
    /// </summary>
    public static Rectangle SubmenuRect(Rectangle parentMenu, Rectangle parentItem,
        IReadOnlyList<EditorMenuItem> subItems, int screenWidth, int screenHeight, float scale)
    {
        var w = Px(MenuWidth, scale);
        var h = MenuHeight(subItems, scale);
        var x = parentMenu.Right; // open to the right of the parent menu
        if (x + w > screenWidth) x = parentMenu.Left - w; // no room → flip left
        x = Math.Max(0, Math.Min(x, Math.Max(0, screenWidth - w)));
        var y = Math.Min(parentItem.Y, Math.Max(0, screenHeight - h));
        y = Math.Max(0, y);
        return new Rectangle(x, y, w, h);
    }

    /// <summary>Where an item's label text is drawn: left-inset, vertically centred for a glyph run of
    /// <paramref name="lineHeight"/> px.</summary>
    public static Vector2 ItemText(Rectangle item, float lineHeight, float scale) =>
        new(item.X + Px(TextInsetX, scale), item.Y + (item.Height - lineHeight) / 2f);

    /// <summary>The small right-pointing ▸ caret rectangle for a submenu row (a square glyph box at the
    /// row's right edge), for the caller to draw a triangle mesh into.</summary>
    public static Rectangle CaretRect(Rectangle item, float scale)
    {
        var size = Px(8, scale);
        var right = item.Right - Px(CaretInsetX, scale);
        return new Rectangle(right, item.Y + (item.Height - size) / 2, size, size);
    }

    /// <summary>The small check-box square for a checkable row (UX3-D), in the left gutter BEFORE the
    /// label (inside the <see cref="TextInsetX"/> inset, so labels stay aligned across checkable and
    /// plain rows). A <see cref="EditorMenuItemKind.Toggle"/> draws its outline always (filled when on);
    /// a checked radio Action fills it — see <c>EditorContextMenuSystem</c>.</summary>
    public static Rectangle CheckRect(Rectangle item, float scale)
    {
        var size = Px(9, scale);
        return new Rectangle(item.X + Px(2, scale), item.Y + (item.Height - size) / 2, size, size);
    }

    /// <summary>The centred thin divider line rectangle inside a separator row.</summary>
    public static Rectangle SeparatorLine(Rectangle item, float scale)
    {
        var inset = Px(TextInsetX, scale);
        var thickness = Math.Max(1, Px(1, scale));
        return new Rectangle(item.X + inset, item.Y + (item.Height - thickness) / 2,
            Math.Max(1, item.Width - inset * 2), thickness);
    }
}
