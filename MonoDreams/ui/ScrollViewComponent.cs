using DefaultEcs;
using Microsoft.Xna.Framework;

namespace MonoDreams.UI;

/// <summary>
/// A vertically scrollable viewport rendered through a dedicated render target
/// (<see cref="MonoDreams.Component.Draw.RenderTargetID.Scroll"/>) and composited into a virtual
/// sub-rectangle by <c>RenderLayer.Overlay</c> — the same seam the camera demo's minimap uses, not
/// a scissor rect. Content entities live on the Scroll target, positioned in that target's OWN
/// top-left coordinate space (X in <c>0..ViewportWidth</c>, Y in <c>0..ContentHeight</c>), parented
/// under <see cref="ContentRoot"/>. Scrolling shifts the content up by driving
/// <c>ContentRoot.TransformComponent.Position.Y</c> to <c>-Offset</c>; anything outside
/// <c>0..ViewportHeight</c> falls outside the render target's bounds and is naturally clipped.
///
/// <para>Pure data: <see cref="ScrollViewSystem"/> reads the cursor wheel and writes
/// <see cref="Offset"/> and <see cref="ContentRoot"/>'s position; the demo owns the render-target,
/// the overlay layer, and the content rows (see this module's premises and the integration notes).</para>
/// </summary>
public sealed class ScrollViewComponent
{
    /// Current scroll offset in target-space pixels. 0 = top; increasing scrolls content up.
    /// Clamped by <see cref="ScrollViewSystem"/> to <c>[0, max(0, ContentHeight - ViewportHeight)]</c>.
    public float Offset;

    /// Total height of the scroll content (sum of all rows) in target-space pixels.
    public float ContentHeight;

    /// Visible height of the viewport in target-space pixels. Equals the Scroll render target's height.
    public float ViewportHeight;

    /// Visible width of the viewport in target-space pixels. Equals the Scroll render target's width.
    public float ViewportWidth;

    /// The <c>TransformComponent</c>-carrying entity the demo parents all scroll rows under. Its
    /// <c>Position.Y</c> is driven to <c>-Offset</c> each frame (X is preserved); when
    /// <see cref="Enabled"/> is false it is pushed far below the viewport so the target renders empty.
    public Entity ContentRoot;

    /// When true the view responds to the wheel and tracks the content to <c>-Offset</c>; when false
    /// the content is pushed out of the viewport (so the demo can blank the panel while its tab is
    /// inactive — the Scroll target always renders regardless of <c>VisibleComponent</c>).
    public bool Enabled = true;

    /// Where the wheel hit-test applies, in HUD virtual coordinates — the SAME space the demo passes
    /// to <c>RenderLayer.Overlay</c> when compositing the Scroll target, so the scrollable area the
    /// cursor reacts over matches the on-screen panel exactly.
    public Rectangle ViewportVirtualBounds;

    // ─── Scrollbar (optional; opt in by setting Track/Thumb entities + TrackWorldBounds) ──────────
    // The scrollbar is rendered as Main-target CHROME by the demo (a track entity + a thumb entity,
    // both opaque mesh fills), exactly like the viewport box. ScrollViewSystem hit-tests it in Main
    // WORLD space (cursor.WorldPosition) — matching DropdownSystem.ContainsCursor — and drives the
    // thumb entity's TransformComponent.Y from Offset each frame. The thumb mesh is built once at the
    // computed height (the demo bakes it sized to ThumbHeight); the system only moves it.

    /// The scrollbar track entity (a thin Main-target fill). Pure visual + hit area; the system
    /// reads <see cref="TrackWorldBounds"/> for hit-testing (the track entity itself is just chrome).
    /// Leave at <c>default</c> to run without a scrollbar (wheel still works).
    public Entity ScrollbarTrack;

    /// The scrollbar thumb entity (a Main-target fill, height = <see cref="ThumbHeight"/>). The system
    /// writes its <c>TransformComponent.Position.Y</c> from <see cref="Offset"/> each frame (X kept).
    public Entity ScrollbarThumb;

    /// The track's hit rectangle in Main WORLD space (top-left + size), used for click/drag hit-tests.
    /// The thumb travels vertically inside this rectangle. Set by the demo to the on-screen track rect.
    public Rectangle TrackWorldBounds;

    /// The computed thumb height in world/track pixels: <c>ViewportHeight/ContentHeight * track height</c>,
    /// clamped to a sane minimum. The demo bakes the thumb mesh at this height; the system uses it to
    /// map <see cref="Offset"/> → thumb Y over the remaining track travel.
    public float ThumbHeight;

    /// True while the user is dragging the thumb (set/cleared by <see cref="ScrollViewSystem"/>).
    public bool DraggingThumb;

    /// While dragging, the cursor-world Y at the drag start minus the thumb's top Y — the grab offset
    /// inside the thumb, so the thumb doesn't jump under the cursor on grab.
    public float DragAnchorY;
}
