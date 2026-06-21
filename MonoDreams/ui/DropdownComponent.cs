using DefaultEcs;

namespace MonoDreams.UI;

/// <summary>
/// A dropdown (select) list: a single always-visible trigger button that opens a popup of option
/// buttons. Pure data. <see cref="DropdownSystem"/> shows/hides the popup and gates its focus from
/// <see cref="IsOpen"/> (mirroring <see cref="TabSystem"/>), and closes the list on an outside
/// click. Opening (on <see cref="Trigger"/> activation) and selection are game-side: the screen
/// subscribes to <see cref="UIFocusActivated"/>, and when the activated entity is one of
/// <see cref="Items"/>, it sets <see cref="SelectedIndex"/>, updates the trigger's label text, and
/// closes the list (<see cref="IsOpen"/> = false) — the same game-owned dispatch the ui premises
/// call for (see "<c>ButtonInteractionSystem</c> is deliberately NOT in this module").
///
/// <para>While open, the option buttons share <see cref="Group"/> with each other so
/// <see cref="UIFocusSystem"/> traps keyboard up/down navigation inside the list — the screen
/// raises the active group to <see cref="Group"/> while this dropdown is the topmost open popup
/// (the convention is 200 for a dropdown, 300 for a combobox dropdown). The system never owns the
/// active-group value; it only exposes <see cref="IsOpen"/> for the screen to read.</para>
/// </summary>
public sealed class DropdownComponent
{
    /// True while the option popup is shown. Opened by the screen (on <see cref="Trigger"/>
    /// activation); closed by the screen on selection or by <see cref="DropdownSystem"/> on an
    /// outside click.
    public bool IsOpen;

    /// Focus scope id for the option buttons while open. The screen raises the active group to
    /// this value when the dropdown is the topmost open popup so navigation is trapped in the list.
    public int Group;

    /// The always-visible button that opens the list. Stays visible and focusable regardless of
    /// <see cref="IsOpen"/>; the outside-click test keeps the list open when the cursor is over it.
    public Entity Trigger;

    /// The option button entities, in list order. Focusable in <see cref="Group"/> while open;
    /// the index here is the option's index. The outside-click test keeps the list open when the
    /// cursor is over any of these.
    public Entity[] Items = [];

    /// Every renderable entity of the open popup — the panel background, the option buttons, and
    /// their labels — toggled together via <c>VisibleComponent</c> by <see cref="DropdownSystem"/>.
    /// These live on the Main target, which honors <c>VisibleComponent</c>.
    public Entity[] Overlay = [];

    /// The index into <see cref="Items"/> of the current selection. Set by the screen on selection;
    /// the system does not write it.
    public int SelectedIndex;
}
