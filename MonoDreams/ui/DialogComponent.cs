using DefaultEcs;

namespace MonoDreams.UI;

/// <summary>
/// A modal dialog / alert: a set of renderable entities (backdrop, panel, title, buttons, …) that
/// show together when <see cref="IsOpen"/> and hide together when closed. Placed on a controller
/// entity; the game BUILDS the visuals (a full-screen <c>FilledRectangleMeshGenerator</c> backdrop
/// in an OPAQUE dark color, a <c>FilledRoundedRectangle</c> + <c>RoundedRectangleOutline</c> panel,
/// title text, and buttons) and assigns every one of them to <see cref="Content"/>.
/// <see cref="DialogSystem"/> keeps that set's <c>VisibleComponent</c> and any
/// <see cref="FocusableComponent.Disabled"/> in sync with <see cref="IsOpen"/>.
///
/// <para>The dialog is MODAL via focus groups, not via DialogSystem. The buttons carry
/// <see cref="FocusableComponent.Group"/> = <see cref="Group"/>; while open the game raises the
/// active group (the <c>Func&lt;int&gt;</c> passed to <see cref="UIFocusSystem"/>) to
/// <see cref="Group"/> so only dialog buttons are reachable, and the backdrop visually covers the
/// screen. Opening/closing (flipping <see cref="IsOpen"/> and the active group) is game-side — this
/// component only exposes the flag the game reads. This mirrors <see cref="TabBarComponent"/> /
/// <see cref="TabContentComponent"/>: the system owns show/hide + focus-gate, the game owns policy.</para>
/// </summary>
public sealed class DialogComponent
{
    /// True while the dialog is shown. The game flips this (and raises the active focus group to
    /// <see cref="Group"/>) to open/close; <see cref="DialogSystem"/> reads it each frame.
    public bool IsOpen;

    /// Focus scope id for this dialog's controls. The dialog's buttons set their
    /// <see cref="FocusableComponent.Group"/> to this value; raising the active group to it traps
    /// focus inside the dialog (the demo uses 100 for dialogs).
    public int Group;

    /// Every renderable entity making up the dialog — backdrop, panel, title, buttons, etc. — in any
    /// order. <see cref="DialogSystem"/> toggles each one's <c>VisibleComponent</c> with
    /// <see cref="IsOpen"/>, and for any entity that also has a <see cref="FocusableComponent"/> it
    /// gates <see cref="FocusableComponent.Disabled"/> so the controls are navigable only while open.
    public Entity[] Content = [];
}
