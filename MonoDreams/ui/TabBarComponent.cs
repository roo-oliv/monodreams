using DefaultEcs;

namespace MonoDreams.UI;

/// <summary>
/// A set of tab headers and the index of the active one. Placed on a controller entity; the
/// header entities are buttons (<see cref="ButtonStateComponent"/> + <see cref="FocusableComponent"/>)
/// whose activation switches the active tab. Each tab's body is a set of entities tagged with a
/// matching <see cref="TabContentComponent"/>. <see cref="TabSystem"/> keeps headers and bodies in
/// sync with <see cref="Active"/>.
/// </summary>
public sealed class TabBarComponent
{
    /// Header button entities, in tab order. Index in this array is the tab index.
    public Entity[] Tabs = [];

    /// Currently selected tab index.
    public int Active;
}

/// <summary>
/// Tags an entity as belonging to tab <see cref="TabIndex"/>'s body. <see cref="TabSystem"/> shows
/// the active tab's tagged entities (adds <c>VisibleComponent</c>) and hides the rest, and disables
/// the inactive tabs' focusables so navigation stays within the visible tab. Put this on every
/// renderable entity of a tab's content (Main-target entities, which honor <c>VisibleComponent</c>).
/// </summary>
public struct TabContentComponent
{
    public int TabIndex;
}
