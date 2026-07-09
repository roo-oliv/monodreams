#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MonoDreams.LevelEditor.UI;

/// <summary>What a single context-menu entry is (drives its visuals + click behavior).</summary>
public enum EditorMenuItemKind
{
    /// <summary>A leaf action row: clicking it dispatches <see cref="EditorMenuItem.Path"/> and closes
    /// the menu.</summary>
    Action,

    /// <summary>A checkable leaf row (UX3-D): clicking it dispatches <see cref="EditorMenuItem.Path"/>
    /// but does NOT close the menu (Blender behavior — flip several overlays in one open); its
    /// <see cref="EditorMenuItem.Checked"/> state renders a check box before the label and refreshes in
    /// place after each toggle. Distinct from <see cref="Action"/> only in the no-close + the check.</summary>
    Toggle,

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

    /// <summary>The checked state (UX3-D): for a <see cref="EditorMenuItemKind.Toggle"/> it is the on/off
    /// of the setting (a filled vs empty check box); for a radio-style <see cref="EditorMenuItemKind.Action"/>
    /// (e.g. the current grid-spacing preset) it marks the selected value. Ignored for other kinds.</summary>
    public bool Checked { get; init; }

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

    // Prefab paths (PF-D). The card paths carry the prefab id as a suffix (the card knows which prefab).
    public const string CreatePrefabFromSelectionPath = "prefab/from-selection";
    public const string UnpackPrefabPath = "prefab/unpack";
    public const string CreateEmptyPrefabPath = "prefab/create-empty";
    public const string PrefabEditPathPrefix = "prefab-edit:";
    public const string PrefabDeletePathPrefix = "prefab-delete:";

    /// <summary>The entity context menu (the viewport right-click AND the header <c>Entity ▾</c>
    /// dropdown — one model, two anchors): <b>Order ▸</b> (Bring Forward / Send Backward), a separator,
    /// the prefab actions — <b>Create Prefab from Selection…</b> (PF-D, enabled with a selection) and
    /// <b>Unpack Prefab</b> (<see cref="EditorMenuItem.Danger"/>, enabled only when the selection is a
    /// prefab instance root, <paramref name="isPrefabInstance"/>) — a separator, then <b>Delete</b>
    /// (<see cref="EditorMenuItem.Danger"/>). Every selection-gated item is disabled when nothing is
    /// selected, so the header dropdown reads inert with no selection.</summary>
    public static IReadOnlyList<EditorMenuItem> EntityMenu(bool hasSelection, bool isPrefabInstance = false) => new[]
    {
        OrderSubmenu(hasSelection),
        Separator(),
        new EditorMenuItem
        {
            Kind = EditorMenuItemKind.Action, Label = "Create Prefab from Selection…",
            Path = CreatePrefabFromSelectionPath, Enabled = hasSelection,
        },
        new EditorMenuItem
        {
            Kind = EditorMenuItemKind.Action, Label = "Unpack Prefab", Path = UnpackPrefabPath,
            Enabled = isPrefabInstance, Danger = true,
        },
        Separator(),
        new EditorMenuItem
        {
            Kind = EditorMenuItemKind.Action, Label = "Delete", Path = DeletePath,
            Enabled = hasSelection, Danger = true,
        },
    };

    /// <summary>The per-card prefab menu on the Prefabs shelf (PF-D): <b>Edit Prefab</b> (opens its tab)
    /// and <b>Delete</b> (<see cref="EditorMenuItem.Danger"/>, file delete with a confirm). Both paths
    /// carry the <paramref name="prefabId"/> suffix so the dispatch knows which prefab the card is.</summary>
    public static IReadOnlyList<EditorMenuItem> PrefabCardMenu(string prefabId) => new[]
    {
        new EditorMenuItem { Kind = EditorMenuItemKind.Action, Label = "Edit Prefab", Path = PrefabEditPathPrefix + prefabId },
        Separator(),
        new EditorMenuItem
        {
            Kind = EditorMenuItemKind.Action, Label = "Delete", Path = PrefabDeletePathPrefix + prefabId, Danger = true,
        },
    };

    /// <summary>The Prefabs-shelf background menu (PF-D): <b>Create Empty Prefab…</b> (name modal → a
    /// minimal one-root <c>.mdprefab</c> → opens its tab).</summary>
    public static IReadOnlyList<EditorMenuItem> PrefabShelfMenu() => new[]
    {
        new EditorMenuItem { Kind = EditorMenuItemKind.Action, Label = "Create Empty Prefab…", Path = CreateEmptyPrefabPath },
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

    /// <summary>
    /// The viewport <b>Overlays</b> dropdown (UX3-D §3, opened below the header Overlays button) —
    /// Blender's per-viewport Overlays menu, adapted: a <b>Grid</b> toggle, a <b>Grid Spacing ▸</b>
    /// submenu of preset Action items (the current value <see cref="EditorMenuItem.Checked"/>), an
    /// <b>Outline Selected</b> toggle, and a <b>Camera</b> toggle. The toggles carry the current settings
    /// so the check boxes render on; rebuilt after each toggle so the check flips in place. The spacing
    /// presets edit the SHARED grid quantum (<see cref="GizmoStateComponent.GridStep"/>), so the
    /// displayed grid is the grid things snap to.
    /// </summary>
    public static IReadOnlyList<EditorMenuItem> OverlaysMenu(
        bool showGrid, float gridSpacing, bool outlineSelected, bool showCameraGlyph) => new[]
    {
        new EditorMenuItem
        {
            Kind = EditorMenuItemKind.Toggle, Label = "Grid",
            Path = ViewportOverlayOps.GridTogglePath, Checked = showGrid,
        },
        GridSpacingSubmenu(gridSpacing),
        new EditorMenuItem
        {
            Kind = EditorMenuItemKind.Toggle, Label = "Outline Selected",
            Path = ViewportOverlayOps.OutlineTogglePath, Checked = outlineSelected,
        },
        new EditorMenuItem
        {
            Kind = EditorMenuItemKind.Toggle, Label = "Camera",
            Path = ViewportOverlayOps.CameraTogglePath, Checked = showCameraGlyph,
        },
    };

    /// <summary>The "Grid Spacing ▸" submenu (UX3-D): one Action item per preset in
    /// <see cref="ViewportOverlayOps.SpacingPresets"/>; the item whose value matches the current shared
    /// grid step is <see cref="EditorMenuItem.Checked"/>. Clicking one closes the menu (Action) and
    /// writes the shared step.</summary>
    private static EditorMenuItem GridSpacingSubmenu(float current)
    {
        var presets = ViewportOverlayOps.SpacingPresets;
        var items = new EditorMenuItem[presets.Length];
        for (var i = 0; i < presets.Length; i++)
        {
            var preset = presets[i];
            items[i] = new EditorMenuItem
            {
                Kind = EditorMenuItemKind.Action,
                Label = ((int)preset).ToString(CultureInfo.InvariantCulture),
                Path = ViewportOverlayOps.SpacingPath(preset),
                Checked = Math.Abs(current - preset) < 1e-3f,
            };
        }
        return new EditorMenuItem
        {
            Kind = EditorMenuItemKind.Submenu, Label = "Grid Spacing",
            Path = ViewportOverlayOps.SpacingSubmenuPath, Submenu = items,
        };
    }

    /// <summary>Finds the leaf item with the given action-id <see cref="EditorMenuItem.Path"/> anywhere
    /// in the model (top level OR inside a submenu) — the <c>menu:pick &lt;path&gt;</c> lookup. Returns
    /// null when no such leaf exists.</summary>
    public static EditorMenuItem? FindByPath(IReadOnlyList<EditorMenuItem> items, string path)
    {
        foreach (var item in items)
        {
            if (item.Kind is EditorMenuItemKind.Action or EditorMenuItemKind.Toggle && item.Path == path)
                return item;
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
