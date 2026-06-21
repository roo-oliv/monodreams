using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.UI;

/// <summary>
/// Drives <see cref="DropdownComponent"/>s. Each frame, for every dropdown it reconciles the popup
/// to <see cref="DropdownComponent.IsOpen"/> exactly the way <see cref="TabSystem"/> reconciles a
/// tab's body: the <see cref="DropdownComponent.Overlay"/> entities get <c>VisibleComponent</c>
/// when open and lose it when closed, and the option entities' <see cref="FocusableComponent"/>
/// are <c>Disabled = !IsOpen</c> so closed lists stay out of navigation. It also closes an open
/// list on an outside click: if the left button is released this frame and the cursor is not over
/// the <see cref="DropdownComponent.Trigger"/> nor any <see cref="DropdownComponent.Items"/> entry,
/// <see cref="DropdownComponent.IsOpen"/> is set to false.
///
/// <para>This system owns only show/hide, focus-gating, and outside-click close. Opening (on
/// trigger activation), keyboard up/down within the list (handled by <see cref="UIFocusSystem"/>
/// once the items share the dropdown's group), and selecting an item (the screen subscribes to
/// <see cref="UIFocusActivated"/>) are game-owned — the same seam as button click dispatch and
/// text-input focus (see the ui premises).</para>
/// </summary>
public sealed class DropdownSystem : ISystem<GameState>
{
    private readonly EntitySet _dropdowns;
    private readonly EntitySet _cursors;

    public bool IsEnabled { get; set; } = true;

    public DropdownSystem(World world)
    {
        _dropdowns = world.GetEntities().With<DropdownComponent>().AsSet();
        _cursors = world.GetEntities().With<CursorInputComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        var dropdowns = _dropdowns.GetEntities();
        if (dropdowns.Length == 0) return;

        // The dropdown's popup lives on the Main target, so the outside-click test compares against
        // the cursor's world position (see ui premises — Main honors VisibleComponent / WorldPosition).
        var cursorEntities = _cursors.GetEntities();
        var hasCursor = cursorEntities.Length > 0;
        var clickReleased = false;
        var cursorWorld = Vector2.Zero;
        if (hasCursor)
        {
            ref readonly var cursor = ref cursorEntities[0].Get<CursorInputComponent>();
            clickReleased = cursor.LeftButtonReleased;
            cursorWorld = cursor.WorldPosition;
        }

        foreach (var dropdownEntity in dropdowns)
        {
            var dropdown = dropdownEntity.Get<DropdownComponent>();

            // Outside-click close: a release that landed off the trigger and off every item.
            if (dropdown.IsOpen && hasCursor && clickReleased &&
                !ContainsCursor(dropdown.Trigger, cursorWorld) &&
                !AnyItemContainsCursor(dropdown.Items, cursorWorld))
            {
                dropdown.IsOpen = false;
            }

            // Show/hide the popup and gate its focus (mirrors TabSystem).
            foreach (var e in dropdown.Overlay)
            {
                if (!e.IsAlive) continue;

                var hasVisible = e.Has<VisibleComponent>();
                if (dropdown.IsOpen && !hasVisible) e.Set<VisibleComponent>();
                else if (!dropdown.IsOpen && hasVisible) e.Remove<VisibleComponent>();
            }

            foreach (var item in dropdown.Items)
            {
                if (item.IsAlive && item.Has<FocusableComponent>())
                    item.Get<FocusableComponent>().Disabled = !dropdown.IsOpen;
            }
        }
    }

    private static bool AnyItemContainsCursor(Entity[] items, Vector2 cursorWorld)
    {
        foreach (var item in items)
            if (ContainsCursor(item, cursorWorld)) return true;
        return false;
    }

    /// On-screen rect is Rectangle(WorldPosition, size), where size comes from the focusable (the
    /// hit-test source) or the button visual — matching the bounds UIFocusSystem hit-tests against.
    private static bool ContainsCursor(Entity e, Vector2 cursorWorld)
    {
        if (!e.IsAlive || !e.Has<TransformComponent>()) return false;

        var size = Vector2.Zero;
        if (e.Has<FocusableComponent>()) size = e.Get<FocusableComponent>().Size;
        else if (e.Has<SimpleButtonComponent>()) size = e.Get<SimpleButtonComponent>().Size;
        if (size == Vector2.Zero) return false;

        var wp = e.Get<TransformComponent>().WorldPosition;
        var bounds = new Rectangle((int)wp.X, (int)wp.Y, (int)size.X, (int)size.Y);
        return bounds.Contains(cursorWorld);
    }

    public void Dispose()
    {
        _dropdowns.Dispose();
        _cursors.Dispose();
    }
}
