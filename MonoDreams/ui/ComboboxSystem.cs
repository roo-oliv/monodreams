using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.State;

namespace MonoDreams.UI;

/// <summary>
/// Drives <see cref="ComboboxComponent"/>s. Filters the attached dropdown's options against the
/// input field's live query, EVERY FRAME while the dropdown is open (not only on
/// <see cref="TextInputChanged"/>): for each option, if its label in
/// <see cref="ComboboxComponent.ItemLabels"/> contains the query (case-insensitive) it is a "match".
/// Filtering every frame is what makes typing narrow the list reliably — <see cref="DropdownSystem"/>
/// re-adds <c>VisibleComponent</c> to the whole overlay each open frame, so this system must run
/// AFTER it (it does, per the screen's pipeline) and re-apply the authoritative filter so its hides
/// win.
///
/// <para>When <see cref="ComboboxComponent.MaxVisible"/> is 0 it shows ALL matches (the original
/// behavior). When it is &gt; 0 it WINDOWS the matches: only up to <c>MaxVisible</c> matching options
/// are shown at once (a list longer than the popup can fit), each repositioned into a fixed row slot
/// from <see cref="ComboboxComponent.PanelTopLeft"/>, and an optional scrollbar thumb is sized +
/// positioned from the window. The wheel and a thumb click-drag move the window (hit-tested in Main
/// world space via <c>cursor.WorldPosition</c>, matching <see cref="DropdownSystem"/>).</para>
///
/// <para>This system does filtering + windowing only. <see cref="DropdownSystem"/> still owns the
/// popup's show/hide, focus-gating, and outside-click close; opening on focus, clearing the field on
/// open, and filling the field on selection are game-owned (see the ui premises).</para>
/// </summary>
public sealed class ComboboxSystem : ISystem<GameState>
{
    private const float WheelScale = 0.5f;

    private readonly EntitySet _comboboxes;
    private readonly EntitySet _cursors;

    public bool IsEnabled { get; set; } = true;

    public ComboboxSystem(World world)
    {
        _comboboxes = world.GetEntities().With<ComboboxComponent>().AsSet();
        _cursors = world.GetEntities().With<CursorInputComponent>().AsSet();
        world.Subscribe(this);
    }

    [Subscribe]
    private void OnTextChanged(in TextInputChanged msg)
    {
        if (!IsEnabled) return;
        // A keystroke resets the window to the top so the first matches are always shown.
        foreach (var comboEntity in _comboboxes.GetEntities())
        {
            var combo = comboEntity.Get<ComboboxComponent>();
            if (combo.Input != msg.Input) continue;
            combo.WindowStart = 0;
            return;
        }
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        var hasCursor = false;
        CursorInputComponent cursor = default;
        var cursorEntities = _cursors.GetEntities();
        if (cursorEntities.Length > 0) { cursor = cursorEntities[0].Get<CursorInputComponent>(); hasCursor = true; }

        foreach (var comboEntity in _comboboxes.GetEntities())
        {
            var combo = comboEntity.Get<ComboboxComponent>();
            if (!combo.DropdownEntity.IsAlive || !combo.DropdownEntity.Has<DropdownComponent>()) continue;
            var dropdown = combo.DropdownEntity.Get<DropdownComponent>();

            // Filter only matters while the list is open; when closed DropdownSystem hides everything.
            if (!dropdown.IsOpen) { combo.DraggingThumb = false; continue; }

            var query = combo.Input.IsAlive && combo.Input.Has<TextInputComponent>()
                ? combo.Input.Get<TextInputComponent>().Text ?? string.Empty
                : string.Empty;

            Filter(combo, dropdown, query, hasCursor, in cursor);
        }
    }

    /// Reconciles each option to the query + window, and (when windowed) drives the scrollbar.
    private static void Filter(ComboboxComponent combo, DropdownComponent dropdown, string query,
        bool hasCursor, in CursorInputComponent cursor)
    {
        var items = dropdown.Items;
        var labels = combo.ItemLabels;
        var labelEntities = combo.ItemLabelEntities;

        // 1) Compute the matching option indices (the filtered set), in list order.
        var matches = new List<int>(items.Length);
        for (var i = 0; i < items.Length; i++)
        {
            var label = i < labels.Length ? labels[i] ?? string.Empty : string.Empty;
            if (query.Length == 0 || label.Contains(query, StringComparison.OrdinalIgnoreCase))
                matches.Add(i);
        }

        var windowed = combo.MaxVisible > 0;
        var visibleCount = windowed ? Math.Min(combo.MaxVisible, matches.Count) : matches.Count;

        // 2) Scrollbar input (windowed only) — wheel + thumb drag move the window over the match set.
        if (windowed)
        {
            var maxStart = Math.Max(0, matches.Count - combo.MaxVisible);

            if (hasCursor && cursor.ScrollWheelDelta != 0 && combo.TrackWorldBounds.Contains((int)cursor.WorldPosition.X, (int)cursor.WorldPosition.Y))
                combo.WindowStart -= Math.Sign(cursor.ScrollWheelDelta);

            if (hasCursor) UpdateThumbDrag(combo, in cursor, matches.Count);

            combo.WindowStart = MathHelper.Clamp(combo.WindowStart, 0, maxStart);
        }

        // 3) Build the set of indices to actually show (the window of matches), and their row slot.
        // rowOf[optionIndex] = 0..visibleCount-1, or -1 if hidden.
        var rowOf = new int[items.Length];
        for (var i = 0; i < items.Length; i++) rowOf[i] = -1;
        var first = windowed ? combo.WindowStart : 0;
        for (var r = 0; r < visibleCount; r++)
        {
            var rank = first + r;
            if (rank < 0 || rank >= matches.Count) break;
            rowOf[matches[rank]] = r;
        }

        // 4) Apply visibility + reposition each visible match into its row slot.
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (!item.IsAlive) continue;
            var show = rowOf[i] >= 0;

            SetShown(item, show);
            if (item.Has<FocusableComponent>())
                item.Get<FocusableComponent>().Disabled = !show;
            if (i < labelEntities.Length) SetShown(labelEntities[i], show);

            if (show && windowed && item.Has<TransformComponent>())
            {
                var y = combo.PanelTopLeft.Y + rowOf[i] * combo.ItemHeight;
                item.Get<TransformComponent>().Position = new Vector2(combo.PanelTopLeft.X, y);
            }
        }

        // 5) Drive the scrollbar thumb (windowed only): height = visible/total * track, pos = window.
        if (windowed && combo.ScrollbarThumb.IsAlive && combo.ScrollbarThumb.Has<TransformComponent>())
        {
            var track = combo.TrackWorldBounds;
            var totalRows = Math.Max(1, matches.Count);
            var thumbH = MathHelper.Max(16f, (float)combo.MaxVisible / totalRows * track.Height);
            // Rebuild the thumb mesh at the computed height (it changes as the match count changes).
            var draw = combo.ScrollbarThumb.Get<DrawComponent>();
            draw.SetMeshData(new FilledRectangleMeshGenerator(new Rectangle(0, 0, track.Width, (int)thumbH), combo.ThumbColor));
            combo.ScrollbarThumb.Set(draw);

            var maxStart = Math.Max(0, matches.Count - combo.MaxVisible);
            var travel = Math.Max(0f, track.Height - thumbH);
            var frac = maxStart > 0 ? (float)combo.WindowStart / maxStart : 0f;
            var thumb = combo.ScrollbarThumb.Get<TransformComponent>();
            thumb.Position = new Vector2(track.X, track.Y + frac * travel);
        }
    }

    /// Thumb click-drag for the windowed list: maps cursor-Y over the track travel back to WindowStart.
    private static void UpdateThumbDrag(ComboboxComponent combo, in CursorInputComponent cursor, int matchCount)
    {
        var track = combo.TrackWorldBounds;
        if (track.Height <= 0 || combo.MaxVisible <= 0) { combo.DraggingThumb = false; return; }

        var maxStart = Math.Max(0, matchCount - combo.MaxVisible);
        var totalRows = Math.Max(1, matchCount);
        var thumbH = MathHelper.Max(16f, (float)combo.MaxVisible / totalRows * track.Height);
        var travel = Math.Max(1f, track.Height - thumbH);
        var thumbTop = track.Y + (maxStart > 0 ? (float)combo.WindowStart / maxStart : 0f) * travel;
        var thumbRect = new Rectangle(track.X, (int)thumbTop, track.Width, (int)thumbH);
        var cw = cursor.WorldPosition;

        if (cursor.LeftButtonPressed && thumbRect.Contains((int)cw.X, (int)cw.Y))
        {
            combo.DraggingThumb = true;
            combo.DragAnchorY = cw.Y - thumbTop;
        }
        else if (cursor.LeftButtonPressed && track.Contains((int)cw.X, (int)cw.Y))
        {
            combo.WindowStart += cw.Y < thumbTop ? -combo.MaxVisible : combo.MaxVisible;
        }

        if (!cursor.LeftButton) combo.DraggingThumb = false;

        if (combo.DraggingThumb && maxStart > 0)
        {
            var newTop = cw.Y - combo.DragAnchorY;
            var frac = MathHelper.Clamp((newTop - track.Y) / travel, 0f, 1f);
            combo.WindowStart = (int)Math.Round(frac * maxStart);
        }
    }

    /// Adds or removes <c>VisibleComponent</c> on a Main-target entity to show/hide it — the same
    /// reconcile <see cref="TabSystem"/> and <see cref="DropdownSystem"/> use.
    private static void SetShown(Entity e, bool show)
    {
        if (!e.IsAlive) return;
        var hasVisible = e.Has<VisibleComponent>();
        if (show && !hasVisible) e.Set<VisibleComponent>();
        else if (!show && hasVisible) e.Remove<VisibleComponent>();
    }

    public void Dispose()
    {
        _comboboxes.Dispose();
        _cursors.Dispose();
    }
}
