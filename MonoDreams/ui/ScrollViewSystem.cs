using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.State;

namespace MonoDreams.UI;

/// <summary>
/// Drives every <see cref="ScrollViewComponent"/>. Each frame, for an enabled view, it reads the
/// cursor: when the cursor's <c>VirtualPosition</c> is inside the view's
/// <see cref="ScrollViewComponent.ViewportVirtualBounds"/>, the wheel delta scrolls the content;
/// <see cref="ScrollViewComponent.Offset"/> is clamped to
/// <c>[0, max(0, ContentHeight - ViewportHeight)]</c> and the view's
/// <see cref="ScrollViewComponent.ContentRoot"/> transform is moved to <c>-Offset</c> on Y (X
/// preserved). A disabled view is pushed far below the viewport so its render target renders empty,
/// letting the demo blank the panel when its tab is inactive (the Scroll target always renders
/// regardless of <c>VisibleComponent</c>).
///
/// <para>Wheel is the only built-in input. Keyboard scrolling is intentionally left to the demo:
/// nudge <see cref="ScrollViewComponent.Offset"/> directly and this system re-clamps and re-applies
/// it next frame.</para>
/// </summary>
public sealed class ScrollViewSystem : ISystem<GameState>
{
    /// Wheel delta is multiplied by this before being applied to <see cref="ScrollViewComponent.Offset"/>.
    private const float WheelScale = 0.5f;

    private readonly EntitySet _views;
    private readonly EntitySet _cursors;

    public bool IsEnabled { get; set; } = true;

    public ScrollViewSystem(World world)
    {
        _views = world.GetEntities().With<ScrollViewComponent>().AsSet();
        _cursors = world.GetEntities().With<CursorInputComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        var views = _views.GetEntities();
        if (views.Length == 0) return;

        // The cursor (if any) drives wheel scrolling and scrollbar click/drag.
        var hasCursor = false;
        CursorInputComponent cursor = default;
        var cursorEntities = _cursors.GetEntities();
        if (cursorEntities.Length > 0)
        {
            cursor = cursorEntities[0].Get<CursorInputComponent>();
            hasCursor = true;
        }

        foreach (var e in views)
        {
            var view = e.Get<ScrollViewComponent>();
            if (!view.ContentRoot.IsAlive || !view.ContentRoot.Has<TransformComponent>()) continue;

            var root = view.ContentRoot.Get<TransformComponent>();

            if (!view.Enabled)
            {
                // Park the content well below the viewport so the Scroll target renders empty.
                root.Position = new Vector2(root.Position.X, view.ViewportHeight + view.ContentHeight + 1000f);
                view.DraggingThumb = false;
                continue;
            }

            var max = Math.Max(0f, view.ContentHeight - view.ViewportHeight);

            // Wheel — hit-tested against the viewport in HUD-virtual space (the overlay's space).
            if (hasCursor && cursor.ScrollWheelDelta != 0 &&
                view.ViewportVirtualBounds.Contains(cursor.VirtualPosition))
            {
                view.Offset -= cursor.ScrollWheelDelta * WheelScale;
            }

            // Scrollbar — hit-tested in Main WORLD space (cursor.WorldPosition), matching the box
            // chrome and DropdownSystem.ContainsCursor. Only meaningful when there is overflow.
            if (hasCursor && max > 0f && view.TrackWorldBounds.Height > 0)
            {
                UpdateScrollbar(view, in cursor, max);
            }
            else
            {
                view.DraggingThumb = false;
            }

            view.Offset = MathHelper.Clamp(view.Offset, 0f, max);

            root.Position = new Vector2(root.Position.X, -view.Offset);

            // Drive the thumb's Y from the current offset over the remaining track travel.
            if (view.ScrollbarThumb.IsAlive && view.ScrollbarThumb.Has<TransformComponent>())
            {
                var travel = Math.Max(0f, view.TrackWorldBounds.Height - view.ThumbHeight);
                var frac = max > 0f ? view.Offset / max : 0f;
                var thumbY = view.TrackWorldBounds.Y + frac * travel;
                var thumb = view.ScrollbarThumb.Get<TransformComponent>();
                thumb.Position = new Vector2(thumb.Position.X, thumbY);
            }
        }
    }

    /// Click-drag on the thumb maps cursor-Y delta → Offset over the offset range; a click on the
    /// track above/below the thumb pages one viewport toward the click. Hit-test is Main world space.
    private static void UpdateScrollbar(ScrollViewComponent view, in CursorInputComponent cursor, float max)
    {
        var track = view.TrackWorldBounds;
        var travel = Math.Max(1f, track.Height - view.ThumbHeight);
        var thumbTop = track.Y + (view.Offset / max) * travel;
        var thumbRect = new Rectangle(track.X, (int)thumbTop, track.Width, (int)view.ThumbHeight);
        var cw = cursor.WorldPosition;

        // Start a drag when the press lands on the thumb.
        if (cursor.LeftButtonPressed && thumbRect.Contains(cw))
        {
            view.DraggingThumb = true;
            view.DragAnchorY = cw.Y - thumbTop; // grab offset inside the thumb
        }
        // Page toward a click on the track but off the thumb (jump one viewport).
        else if (cursor.LeftButtonPressed && track.Contains((int)cw.X, (int)cw.Y))
        {
            view.Offset += cw.Y < thumbTop ? -view.ViewportHeight : view.ViewportHeight;
        }

        if (!cursor.LeftButton) view.DraggingThumb = false;

        if (view.DraggingThumb)
        {
            // Map the new thumb-top (cursor minus grab offset) back to an offset over the travel.
            var newThumbTop = cw.Y - view.DragAnchorY;
            var frac = MathHelper.Clamp((newThumbTop - track.Y) / travel, 0f, 1f);
            view.Offset = frac * max;
        }
    }

    public void Dispose()
    {
        _views.Dispose();
        _cursors.Dispose();
    }
}
