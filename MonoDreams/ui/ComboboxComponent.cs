using DefaultEcs;
using Microsoft.Xna.Framework;

namespace MonoDreams.UI;

/// <summary>
/// A combobox: a text field that filters the options of an attached <see cref="DropdownComponent"/>
/// as you type. Pure data. <see cref="ComboboxSystem"/> listens for <see cref="TextInputChanged"/>
/// on the <see cref="Input"/> field and reconciles each dropdown option to the query — an option
/// whose label (in <see cref="ItemLabels"/>, index-aligned with the dropdown's
/// <c>DropdownComponent.Items</c>) contains the query (case-insensitive) stays shown and focusable;
/// the rest lose <c>VisibleComponent</c> and have their <see cref="FocusableComponent.Disabled"/>
/// raised so navigation skips them. While the field has a query it also opens the dropdown
/// (<c>DropdownComponent.IsOpen = true</c>) so the filtered list is visible.
///
/// <para>The dropdown itself is reused as-is for show/hide, outside-click close, and focus-trapping
/// (it uses <c>Group</c> 300 by convention for a combobox); <see cref="DropdownSystem"/> still drives
/// those. The screen owns the rest of the policy the same way it does for a plain dropdown: it focuses
/// the <see cref="Input"/> (opening the list on focus), and on item activation it fills the
/// <see cref="Input"/>'s text with the chosen label and closes the list — the game-owned dispatch the
/// ui premises call for (see "<c>ButtonInteractionSystem</c> is deliberately NOT in this module" and
/// "Text-input focus is game-owned").</para>
/// </summary>
public sealed class ComboboxComponent
{
    /// The filter field — an entity carrying a <see cref="TextInputComponent"/> (focusable). Typing
    /// here drives the filter; <see cref="ComboboxSystem"/> matches <see cref="TextInputChanged"/>
    /// against this entity.
    public Entity Input;

    /// The entity carrying the <see cref="DropdownComponent"/> whose <c>Items</c> are the options.
    /// The dropdown is reused unchanged; <see cref="ComboboxSystem"/> only filters its items and
    /// opens it — <see cref="DropdownSystem"/> still handles show/hide and outside-click close.
    public Entity DropdownEntity;

    /// The option label strings, index-aligned with the dropdown's <c>DropdownComponent.Items</c>.
    /// An item is kept when its label contains the typed query (case-insensitive).
    public string[] ItemLabels = [];

    /// Optional. The per-item label entities, index-aligned with <see cref="ItemLabels"/> (and the
    /// dropdown's <c>Items</c>). When set, <see cref="ComboboxSystem"/> toggles each label's
    /// <c>VisibleComponent</c> alongside its item button so a filtered-out option's text hides too.
    /// Leave entries at <c>default</c> (or the whole array empty) when labels are parented under the
    /// item buttons and need no separate toggle.
    public Entity[] ItemLabelEntities = [];

    // ─── Item windowing (issue 15): show up to N of the FILTERED options at once, scrollable ──────
    // When MaxVisible > 0, ComboboxSystem shows only a window of the matching options (a list longer
    // than the popup can fit), repositioning each visible matching item into a fixed row slot inside
    // the panel and driving an optional scrollbar thumb. With MaxVisible == 0 it shows ALL matches
    // (the original behavior — back-compatible default).

    /// Max option rows visible at once. 0 (default) = show all matches (no windowing).
    public int MaxVisible;

    /// The window start, in MATCH rank (index into the filtered set). Owned by <see cref="ComboboxSystem"/>:
    /// clamped to a valid range each frame and advanced by wheel / thumb-drag while open.
    public int WindowStart;

    /// Row height (target-space pixels) for a windowed item slot. Each visible match is repositioned
    /// to <c>PanelTopLeft + (0, rank-within-window * ItemHeight)</c>.
    public float ItemHeight;

    /// Top-left of the first item row in the popup, in the items' WORLD space (matches how the demo
    /// positions the option buttons). Visible matches are stacked downward from here by row.
    public Vector2 PanelTopLeft;

    // Optional scrollbar for the windowed list (mirrors the scroll view's bar). When the track/thumb
    // entities are set, <see cref="ComboboxSystem"/> sizes + positions the thumb from WindowStart and
    // processes click/drag in Main world space.
    /// The scrollbar thumb entity (height set by the system from the visible fraction). 0/default = none.
    public Entity ScrollbarThumb;
    /// Opaque color the system bakes the thumb mesh with (premultiplied-alpha rule). Default white.
    public Color ThumbColor = Color.White;
    /// The scrollbar track's hit rect in Main WORLD space (top-left + size). Thumb travels inside it.
    public Rectangle TrackWorldBounds;
    /// True while the user is dragging the windowed-list thumb (system-owned).
    public bool DraggingThumb;
    /// Grab offset inside the thumb while dragging (system-owned).
    public float DragAnchorY;
}
