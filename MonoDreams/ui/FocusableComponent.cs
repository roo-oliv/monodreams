using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;

namespace MonoDreams.UI;

/// <summary>
/// Marks an entity as keyboard/pointer focusable. Pure data: <see cref="UIFocusSystem"/> owns
/// the single "currently focused" entity within the active <see cref="Group"/>, writes
/// <see cref="IsFocused"/> here (read by button/widget visuals), and mirrors it onto a
/// <c>TextInputComponent.Focused</c> when present. WHAT activation does stays game-side — the
/// system only publishes <see cref="UIFocusActivated"/> (the same seam as text-input focus and
/// button click dispatch; see the ui premises).
/// </summary>
public struct FocusableComponent
{
    /// Ordinal order for Tab / Shift-Tab cycling within the group (lower first).
    public int TabIndex;

    /// Focus scope id. Navigation (spatial and ordinal) stays within the active group, so a modal
    /// dialog or open dropdown can trap focus by raising the active group to its own id.
    public int Group;

    /// When true the focusable is skipped by navigation and never activates (disabled control).
    public bool Disabled;

    /// Owned by <see cref="UIFocusSystem"/>: true on the single focused entity in the active group.
    public bool IsFocused;

    /// Owned by <see cref="UIFocusSystem"/>: true when the current focus was set via the KEYBOARD
    /// pass (spatial / ordinal nav or keyboard activate) rather than a pointer hover. This is the
    /// <c>:focus-visible</c> signal — visuals (e.g. <see cref="ButtonVisualSystem"/>'s focus ring)
    /// show the focus affordance only when <c>IsFocused &amp;&amp; FocusVisible</c>, so a mouse
    /// hover changes the background fill but does not draw the keyboard-focus ring. Written every
    /// frame alongside <see cref="IsFocused"/>; meaningful only on the focused entity.
    public bool FocusVisible;

    /// Cursor to show while the pointer hovers this focusable (e.g. <see cref="CursorType.Hand"/>
    /// for a link). <see cref="CursorType.Default"/> (the default) means "no override — keep the
    /// arrow". A UI-side cursor-hover system reads this and swaps the cursor mesh; the mechanism is
    /// reusable across any focusable that wants a custom hover cursor.
    public CursorType HoverCursor;

    /// On-screen size of the focusable, top-left anchored at the entity's <c>WorldPosition</c>.
    /// Used for pointer hit-testing and for spatial (direction-based) navigation. The creator sets
    /// this — for a <see cref="SimpleButtonComponent"/> it is the button's <c>Size</c>.
    public Vector2 Size;

    /// Which space the bounds live in. HUD/UI compare against the cursor's VirtualPosition; Main
    /// compares against WorldPosition.
    public RenderTargetID Target;
}

/// Published by <see cref="UIFocusSystem"/> when a focusable is activated (Enter/Space on the
/// focused one, or a pointer click on it). Carries the entity and its id (from
/// <see cref="ButtonStateComponent.Id"/> when present) so game code routes the action by id —
/// the configurable, game-owned dispatch the ui premises call for.
public readonly record struct UIFocusActivated(Entity Focused, string Id);

/// Published by <see cref="UIFocusSystem"/> when focus moves from one entity to another.
public readonly record struct FocusChanged(Entity Previous, Entity Current);
