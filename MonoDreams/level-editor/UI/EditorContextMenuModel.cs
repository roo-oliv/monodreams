#nullable enable
using System.Collections.Generic;

namespace MonoDreams.LevelEditor.UI;

/// <summary>What a single context-menu entry is (drives its visuals + click behavior).</summary>
public enum EditorMenuItemKind
{
    /// <summary>A leaf action row: clicking it dispatches <see cref="EditorMenuItem.Path"/> and closes
    /// the menu.</summary>
    Action,

    /// <summary>A thin horizontal divider (non-interactive).</summary>
    Separator,

    /// <summary>A row that opens a ONE-level <see cref="EditorMenuItem.Submenu"/> beside it on hover
    /// (e.g. "Order ▸"). Not itself dispatchable.</summary>
    Submenu,
}

/// <summary>
/// One pure-data entry of an <see cref="EditorContextMenu"/> (UX2-D). An item is a leaf
/// <see cref="EditorMenuItemKind.Action"/> (with an action-id <see cref="Path"/> the menu dispatches),
/// a <see cref="EditorMenuItemKind.Separator"/>, or a <see cref="EditorMenuItemKind.Submenu"/> parent
/// carrying ONE level of child items. State follows the UX-A model: <see cref="Enabled"/> gates the
/// dispatch + the <c>TextDisabled</c> label, and <see cref="Danger"/> paints a destructive item in the
/// <c>Danger</c> role. This is DATA only — the SAME item list renders as a right-click context menu or a
/// header dropdown (one model, two anchors).
/// </summary>
public sealed class EditorMenuItem
{
    public required EditorMenuItemKind Kind { get; init; }

    /// <summary>The row label (empty for a separator).</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>The action-id the leaf dispatches (e.g. <c>order/forward</c>, <c>delete</c>,
    /// <c>add-empty</c>, <c>create-scene</c>); empty for separators/submenu parents. The
    /// <c>menu:pick &lt;path&gt;</c> op matches this.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Whether the item is clickable (a disabled item renders <c>TextDisabled</c> and never
    /// dispatches).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Whether the item is destructive (renders in the <c>Danger</c> role).</summary>
    public bool Danger { get; init; }

    /// <summary>The child items of a <see cref="EditorMenuItemKind.Submenu"/> parent (one level only);
    /// null otherwise.</summary>
    public IReadOnlyList<EditorMenuItem>? Submenu { get; init; }
}

/// <summary>
/// The pure builders for the editor's context menus (UX2-D §4). Each returns a flat item list — the
/// SAME model renders as a right-click context menu (opened at the cursor) or the header <c>Entity ▾</c>
/// dropdown (anchored below the button). Action-id <see cref="EditorMenuItem.Path"/>s are the stable ids
/// the <c>menu:pick &lt;path&gt;</c> op matches and the overlay maps to concrete behaviour, so the menus
/// stay game-agnostic. World-free / cursor-free — unit-testable directly.
/// </summary>
public static class EditorContextMenuModel
{
    // Action-id paths (the menu:pick grammar + the overlay's dispatch map).
    public const string OrderForwardPath = "order/forward";
    public const string OrderBackPath = "order/back";
    public const string OrderSubmenuPath = "order";
    public const string DeletePath = "delete";
    public const string AddEmptyPath = "add-empty";
    public const string CreateScenePath = "create-scene";

    /// <summary>The entity context menu (the viewport right-click AND the header <c>Entity ▾</c>
    /// dropdown — one model, two anchors): <b>Order ▸</b> (Bring Forward / Send Backward), a separator,
    /// then <b>Delete</b> (<see cref="EditorMenuItem.Danger"/>). Every item is disabled when nothing is
    /// selected (<paramref name="hasSelection"/> false), so the header dropdown reads inert with no
    /// selection.</summary>
    public static IReadOnlyList<EditorMenuItem> EntityMenu(bool hasSelection) => new[]
    {
        OrderSubmenu(hasSelection),
        Separator(),
        new EditorMenuItem
        {
            Kind = EditorMenuItemKind.Action, Label = "Delete", Path = DeletePath,
            Enabled = hasSelection, Danger = true,
        },
    };

    /// <summary>The Entities-panel context menu (UX2-D §4): <b>Add Empty Entity</b>, and — when a tree
    /// row is under the click (<paramref name="hasRowEntity"/>) — the entity items (Order ▸ / Delete)
    /// for that row's entity ABOVE a separator (cheap + consistent with the viewport menu).</summary>
    public static IReadOnlyList<EditorMenuItem> EntitiesPanelMenu(bool hasRowEntity)
    {
        var items = new List<EditorMenuItem>();
        if (hasRowEntity)
        {
            items.Add(OrderSubmenu(enabled: true));
            items.Add(new EditorMenuItem
            {
                Kind = EditorMenuItemKind.Action, Label = "Delete", Path = DeletePath,
                Enabled = true, Danger = true,
            });
            items.Add(Separator());
        }
        items.Add(new EditorMenuItem
        {
            Kind = EditorMenuItemKind.Action, Label = "Add Empty Entity", Path = AddEmptyPath,
        });
        return items;
    }

    /// <summary>The Scenes-panel context menu (UX2-D §4): <b>Create Empty Scene…</b> (opens the small
    /// name modal on the dialog machinery).</summary>
    public static IReadOnlyList<EditorMenuItem> ScenesPanelMenu() => new[]
    {
        new EditorMenuItem
        {
            Kind = EditorMenuItemKind.Action, Label = "Create Empty Scene…", Path = CreateScenePath,
        },
    };

    /// <summary>Finds the leaf item with the given action-id <see cref="EditorMenuItem.Path"/> anywhere
    /// in the model (top level OR inside a submenu) — the <c>menu:pick &lt;path&gt;</c> lookup. Returns
    /// null when no such leaf exists.</summary>
    public static EditorMenuItem? FindByPath(IReadOnlyList<EditorMenuItem> items, string path)
    {
        foreach (var item in items)
        {
            if (item.Kind == EditorMenuItemKind.Action && item.Path == path) return item;
            if (item.Submenu != null)
            {
                var inner = FindByPath(item.Submenu, path);
                if (inner != null) return inner;
            }
        }
        return null;
    }

    private static EditorMenuItem OrderSubmenu(bool enabled) => new()
    {
        Kind = EditorMenuItemKind.Submenu, Label = "Order", Path = OrderSubmenuPath, Enabled = enabled,
        Submenu = new[]
        {
            new EditorMenuItem
            {
                Kind = EditorMenuItemKind.Action, Label = "Bring Forward", Path = OrderForwardPath, Enabled = enabled,
            },
            new EditorMenuItem
            {
                Kind = EditorMenuItemKind.Action, Label = "Send Backward", Path = OrderBackPath, Enabled = enabled,
            },
        },
    };

    private static EditorMenuItem Separator() => new() { Kind = EditorMenuItemKind.Separator };
}
