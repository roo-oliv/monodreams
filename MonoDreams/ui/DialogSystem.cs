using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.UI;

/// <summary>
/// Drives <see cref="DialogComponent"/>s. Each frame, for every dialog and every entity in its
/// <see cref="DialogComponent.Content"/>: adds <c>VisibleComponent</c> when the dialog
/// <see cref="DialogComponent.IsOpen"/> and removes it when closed (the demo lives on the Main
/// target, which honors <c>VisibleComponent</c>); and for any content entity that has a
/// <see cref="FocusableComponent"/>, sets <see cref="FocusableComponent.Disabled"/> to
/// <c>!IsOpen</c> so the dialog's buttons are navigable only while it is open.
///
/// <para>This is the show/hide + focus-gate mechanism ONLY — exactly mirroring
/// <see cref="TabSystem"/>. The dialog is MODAL via focus groups, which is game-owned: while open
/// the game raises the active focus group (the <c>Func&lt;int&gt;</c> passed to
/// <see cref="UIFocusSystem"/>) to <see cref="DialogComponent.Group"/> so only the dialog's
/// controls (which carry that group) are reachable, and the backdrop the game builds visually
/// covers the screen. This system never opens, closes, or owns the active group.</para>
/// </summary>
public sealed class DialogSystem : ISystem<GameState>
{
    private readonly EntitySet _dialogs;

    public bool IsEnabled { get; set; } = true;

    public DialogSystem(World world)
    {
        _dialogs = world.GetEntities().With<DialogComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        foreach (var dialogEntity in _dialogs.GetEntities())
        {
            var dialog = dialogEntity.Get<DialogComponent>();
            var show = dialog.IsOpen;

            foreach (var e in dialog.Content)
            {
                if (!e.IsAlive) continue;

                // Show the whole dialog's content while open, hide it while closed (Main target
                // consults VisibleComponent).
                var hasVisible = e.Has<VisibleComponent>();
                if (show && !hasVisible) e.Set<VisibleComponent>();
                else if (!show && hasVisible) e.Remove<VisibleComponent>();

                // Gate any focusable content (the dialog's buttons) so they navigate only while open.
                if (e.Has<FocusableComponent>())
                    e.Get<FocusableComponent>().Disabled = !show;
            }
        }
    }

    public void Dispose()
    {
        _dialogs.Dispose();
    }
}
