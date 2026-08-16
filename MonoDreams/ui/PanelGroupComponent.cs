using DefaultEcs;
using Microsoft.Xna.Framework;

namespace MonoDreams.UI;

/// <summary>
/// A group of mutually exclusive panels — tabs, settings pages, wizard steps, an inventory /
/// character / map switcher — of which <b>at most one</b> is active. Placed on a controller entity;
/// each member is the ROOT entity of one panel (its content rides along through the
/// <c>TransformComponent</c> hierarchy). <see cref="PanelGroupSystem"/> keeps the group honest:
/// the active member sits at its own position, every other member is <b>parked</b> — translated by
/// <see cref="ParkOffset"/> so it renders outside the viewport while staying fully alive.
///
/// <para><b>Park, don't hide.</b> Hiding is the instinct and it is wrong here: dropping
/// <c>VisibleComponent</c> does nothing at all on the UI/HUD/Scroll targets (they render regardless)
/// and, where it does work, it takes the panel out of the prep systems — so it comes back as a frame
/// of stale or re-solving content. A parked panel is a fully-set dinner table moved into the next
/// room: every component intact, still sized and laid out, its widget state untouched, so switching
/// back is a single transform write. That premise is the whole reason this component exists — game
/// code only ever writes <see cref="Active"/>, and never hand-writes the parking dance.</para>
///
/// <para><b>"None active" is first class.</b> <see cref="Active"/> = <see cref="None"/> (or any
/// out-of-range index) parks every member — a closed menu is a panel group with no active member,
/// not a special case bolted on the side.</para>
///
/// <para>Pure data. Members are switched by game code (typically from a
/// <see cref="UIFocusActivated"/> handler routing a header button's id to an index) — the same
/// mechanism-here / action-game-side seam the rest of the module uses.</para>
/// </summary>
public sealed class PanelGroupComponent
{
    /// <see cref="Active"/> value meaning "no member is active" — every member is parked.
    public const int None = -1;

    /// Park translation used when a group does not set its own <see cref="ParkOffset"/>: far
    /// up-left of any viewport, matching the magnitude the editor chrome parks at.
    public static readonly Vector2 DefaultParkOffset = new(-100_000f, -100_000f);

    /// The panel root entities, in panel order. Each one needs a <c>TransformComponent</c>; its
    /// content is parented under it (transform hierarchy) so parking moves the whole subtree.
    /// A member belongs to exactly one group.
    public Entity[] Members = [];

    /// Index into <see cref="Members"/> of the active panel, or <see cref="None"/> for "none
    /// active". Out-of-range values are treated as <see cref="None"/>. This is the ONLY field game
    /// code writes.
    public int Active = None;

    /// Local-space translation applied to a parked member (added to the position it would otherwise
    /// hold). Must land the panel outside the viewport; the default does for any sane resolution.
    public Vector2 ParkOffset = DefaultParkOffset;
}

/// <summary>
/// Engine-owned bookkeeping <see cref="PanelGroupSystem"/> writes on a member <b>while it is
/// parked</b> (added when the member is parked, removed when it is restored). <see cref="Home"/> is
/// the local position the member held before parking — the value restored on switch-back, which is
/// what makes the round trip exact. <see cref="Parked"/> is the position the system itself last
/// wrote: when a member's position differs from it, another writer (typically
/// <c>AutoLayoutSystem</c>, which rewrites every root slot's position each frame) has moved the
/// panel while it was parked, and that fresher value becomes the new <see cref="Home"/>.
///
/// <para>Never set this by hand — it is the system's memory, not configuration.</para>
/// </summary>
public struct PanelParkedComponent
{
    /// The member's local position before it was parked; restored verbatim when it becomes active.
    public Vector2 Home;

    /// The local position <see cref="PanelGroupSystem"/> last wrote while parking this member.
    public Vector2 Parked;
}
