using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Input;
using MonoDreams.State;

namespace MonoDreams.UI;

/// <summary>
/// Single owner of UI focus across pointer and keyboard. Each frame it resolves which
/// <see cref="FocusableComponent"/> (within the active group) is focused, writes
/// <see cref="FocusableComponent.IsFocused"/> on every focusable, mirrors it onto a
/// <c>TextInputComponent.Focused</c>, sets <see cref="ButtonStateComponent.IsPressed"/> for the
/// pressed control, and publishes <see cref="UIFocusActivated"/> on activation (Enter/Space on
/// the focused control, or a pointer click on a control).
///
/// <para>Navigation supports BOTH models: directional (spatial nearest-neighbour) via the
/// up/down/left/right actions and ordinal (Tab / Shift-Tab) via next/prev. The pointer only
/// steals focus when the mouse physically moves, so keyboard navigation is not undone by a
/// stationary cursor sitting over a control.</para>
///
/// <para>Focus policy decisions (which group is active, what an activation does) stay with game
/// code: pass an <c>activeGroup</c> accessor (a dialog/dropdown raises it to trap focus) and
/// subscribe to <see cref="UIFocusActivated"/>. This is the configurable, game-owned dispatch the
/// ui premises call for — the system provides the mechanism, not the action.</para>
///
/// <para>It is also the owner of THE pointer pick: the pointer pass resolves the topmost focusable
/// under the cursor once and publishes it (plus when the hover started) on the cursor entity as
/// <see cref="PointerPickComponent"/>. Systems that react to what the pointer is over —
/// <see cref="TooltipSystem"/> and <see cref="CursorHoverSystem"/> — read that instead of
/// hit-testing again, so they can never disagree with what focus and click think is hovered. See
/// the ui premise "There is ONE pointer pick".</para>
/// </summary>
public sealed class UIFocusSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly EntitySet _focusables;
    private readonly EntitySet _cursors;
    private readonly AInputState _up, _down, _left, _right, _next, _prev, _activate;
    private readonly Func<int>? _activeGroup;

    private Entity _focused;

    // :focus-visible — true when the current _focused was set via the keyboard pass (nav / activate)
    // rather than a pointer hover. ButtonVisualSystem shows the keyboard-focus ring only when this is
    // set; pointer hover changes only the fill. Reset to false the moment the pointer steals focus.
    private bool _focusVisible;

    public bool IsEnabled { get; set; } = true;

    public UIFocusSystem(
        World world,
        AInputState up, AInputState down, AInputState left, AInputState right,
        AInputState next, AInputState prev, AInputState activate,
        Func<int>? activeGroup = null)
    {
        _world = world;
        _up = up; _down = down; _left = left; _right = right;
        _next = next; _prev = prev; _activate = activate;
        _activeGroup = activeGroup;
        _focusables = world.GetEntities().With<FocusableComponent>().With<TransformComponent>().AsSet();
        _cursors = world.GetEntities().With<CursorInputComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        var focusables = _focusables.GetEntities();
        if (focusables.Length == 0)
        {
            PublishPick(default, state); // nothing focusable ⇒ nothing is under the pointer
            return;
        }

        var group = _activeGroup?.Invoke() ?? 0;

        Entity activation = default;
        Entity pressed = default;

        // ── Pointer pass ────────────────────────────────────────────────────────
        // Resolves the pick ONCE — the topmost (last in iteration order) focusable under the pointer
        // — and drives hover-focus, press and activation from it, then publishes it for the systems
        // that react to what the pointer is over (tooltips).
        var cursorEntities = _cursors.GetEntities();
        if (cursorEntities.Length > 0)
        {
            ref readonly var cursor = ref cursorEntities[0].Get<CursorInputComponent>();
            var moved = cursor.Delta != Vector2.Zero;

            Entity hovered = default;
            foreach (var e in focusables)
            {
                ref readonly var f = ref e.Get<FocusableComponent>();
                if (f.Disabled || ControlDisabled(e) || f.Group != group) continue;

                var wp = e.Get<TransformComponent>().WorldPosition;
                var pos = f.Target == RenderTargetID.HUD ? cursor.VirtualPosition : cursor.WorldPosition;
                var bounds = new Rectangle((int)wp.X, (int)wp.Y, (int)f.Size.X, (int)f.Size.Y);
                if (!bounds.Contains(pos)) continue;

                hovered = e;
            }

            if (hovered.IsAlive)
            {
                if (moved) SetFocus(hovered, fromKeyboard: false); // mouse only steals focus when it actually moves (hover, no ring)
                if (cursor.LeftButton) pressed = hovered; // held → press animation
                if (cursor.LeftButtonReleased) activation = hovered;
            }

            PublishPick(hovered, state);
        }

        // ── Keyboard pass ───────────────────────────────────────────────────────
        // While editing a text field, directional keys belong to the caret / typed text, so only
        // Tab (ordinal) navigates out of it. Otherwise WASD/arrows move spatially.
        var onTextField = _focused.IsAlive && _focused.Has<TextInputComponent>() && InGroup(_focused, group);

        if (!onTextField)
        {
            if (_left.JustPressed()) MoveSpatial(group, new Vector2(-1, 0));
            else if (_right.JustPressed()) MoveSpatial(group, new Vector2(1, 0));
            else if (_up.JustPressed()) MoveSpatial(group, new Vector2(0, -1));
            else if (_down.JustPressed()) MoveSpatial(group, new Vector2(0, 1));

            if (_activate.JustPressed() && _focused.IsAlive && InGroup(_focused, group))
            {
                activation = _focused;
                _focusVisible = true; // keyboard activate is a keyboard interaction → show the ring
            }
        }

        if (_next.JustPressed()) MoveOrdinal(group, +1);
        if (_prev.JustPressed()) MoveOrdinal(group, -1);

        // ── Reconcile a valid focus in the active group ─────────────────────────
        if (!InGroup(_focused, group))
        {
            var first = FirstInGroup(group);
            _focused = first;
            // A focus that was forced by a group change (overlay open/close) is keyboard-style — show
            // the ring so a freshly trapped group has a visible affordance even before the user moves.
            _focusVisible = true;
        }

        // ── Write per-entity flags ──────────────────────────────────────────────
        foreach (var e in focusables)
        {
            ref var f = ref e.Get<FocusableComponent>();
            var isFocused = e == _focused;
            f.IsFocused = isFocused;
            f.FocusVisible = isFocused && _focusVisible;

            if (e.Has<TextInputComponent>())
                e.Get<TextInputComponent>().Focused = isFocused && !f.Disabled;

            if (e.Has<ButtonStateComponent>())
                e.Get<ButtonStateComponent>().IsPressed = e == pressed && !f.Disabled;
        }

        // ── Activation dispatch ─────────────────────────────────────────────────
        if (activation.IsAlive)
        {
            ref readonly var f = ref activation.Get<FocusableComponent>();
            if (!f.Disabled && !ControlDisabled(activation))
            {
                var id = activation.Has<ButtonStateComponent>() ? activation.Get<ButtonStateComponent>().Id : null;
                _world.Publish(new UIFocusActivated(activation, id ?? string.Empty));
            }
        }
    }

    /// Publishes THE pointer pick on the cursor entity: which focusable the pointer is over and, when
    /// that changes, when the hover started (so a consumer's dwell is one subtraction — see
    /// <see cref="PointerPickComponent"/>). Holding the same entity deliberately leaves the component
    /// untouched, so <c>HoverStartTime</c> keeps running and a consumer may subscribe to the
    /// component's Changed notification without a per-frame storm.
    private void PublishPick(Entity hovered, GameState state)
    {
        var cursorEntities = _cursors.GetEntities();
        if (cursorEntities.Length == 0) return;

        var cursorEntity = cursorEntities[0];
        if (cursorEntity.Has<PointerPickComponent>() &&
            cursorEntity.Get<PointerPickComponent>().Hovered == hovered)
            return; // same thing under the pointer ⇒ the dwell clock keeps running

        cursorEntity.Set(new PointerPickComponent { Hovered = hovered, HoverStartTime = state.TotalTime });
    }

    private bool InGroup(Entity e, int group) =>
        e.IsAlive && e.Has<FocusableComponent>() &&
        !e.Get<FocusableComponent>().Disabled && !ControlDisabled(e) &&
        e.Get<FocusableComponent>().Group == group;

    // A button flagged disabled is skipped by navigation too, independently of the tab-gating
    // FocusableComponent.Disabled flag that TabSystem owns — so an inactive tab re-enabling its
    // focusables never re-enables a genuinely disabled control.
    private static bool ControlDisabled(Entity e) =>
        e.Has<ButtonStateComponent>() && e.Get<ButtonStateComponent>().IsDisabled;

    /// Moves focus to <paramref name="e"/>. <paramref name="fromKeyboard"/> records whether this came
    /// from the keyboard pass (nav / activate) so visuals can distinguish keyboard focus (ring) from
    /// pointer hover (fill only) — the :focus-visible model. The flag is updated even when the target
    /// is already focused, so a hover-then-keyboard (or keyboard-then-hover) on the same control
    /// flips the ring on/off correctly.
    private void SetFocus(Entity e, bool fromKeyboard)
    {
        _focusVisible = fromKeyboard;
        if (e == _focused) return;
        var previous = _focused;
        _focused = e;
        _world.Publish(new FocusChanged(previous, e));
    }

    private Vector2 Center(Entity e)
    {
        var wp = e.Get<TransformComponent>().WorldPosition;
        return wp + e.Get<FocusableComponent>().Size * 0.5f;
    }

    /// Picks the nearest enabled focusable in <paramref name="dir"/> from the current one, scoring
    /// by distance along the direction plus a penalty for cross-axis offset.
    private void MoveSpatial(int group, Vector2 dir)
    {
        if (!InGroup(_focused, group))
        {
            var first = FirstInGroup(group);
            if (first.IsAlive) SetFocus(first, fromKeyboard: true);
            return;
        }

        var from = Center(_focused);
        Entity best = default;
        var bestScore = float.MaxValue;

        foreach (var e in _focusables.GetEntities())
        {
            if (e == _focused || !InGroup(e, group)) continue;
            var delta = Center(e) - from;
            var along = Vector2.Dot(delta, dir);
            if (along <= 1f) continue; // must lie in the pressed direction
            var cross = Math.Abs(Vector2.Dot(delta, new Vector2(-dir.Y, dir.X)));
            var score = along + 2f * cross;
            if (score < bestScore) { bestScore = score; best = e; }
        }

        if (best.IsAlive) SetFocus(best, fromKeyboard: true);
    }

    /// Cycles focus by TabIndex order within the group (wraps around).
    private void MoveOrdinal(int group, int dir)
    {
        var list = new List<Entity>();
        foreach (var e in _focusables.GetEntities())
            if (InGroup(e, group)) list.Add(e);
        if (list.Count == 0) return;

        list.Sort((a, b) => a.Get<FocusableComponent>().TabIndex.CompareTo(b.Get<FocusableComponent>().TabIndex));

        var idx = list.IndexOf(_focused);
        if (idx < 0) { SetFocus(list[0], fromKeyboard: true); return; }
        var next = ((idx + dir) % list.Count + list.Count) % list.Count;
        SetFocus(list[next], fromKeyboard: true);
    }

    private Entity FirstInGroup(int group)
    {
        Entity first = default;
        var bestIndex = int.MaxValue;
        foreach (var e in _focusables.GetEntities())
        {
            if (!InGroup(e, group)) continue;
            var idx = e.Get<FocusableComponent>().TabIndex;
            if (idx < bestIndex) { bestIndex = idx; first = e; }
        }
        return first;
    }

    public void Dispose()
    {
        _focusables.Dispose();
        _cursors.Dispose();
    }
}
