#nullable enable
using System;
using System.Collections.Generic;

namespace MonoDreams.LevelEditor.Component;

/// <summary>The right strip's active tab (a stack of panels the tab hosts). Scene = the entity tree
/// + Inspector; Systems = the pipeline listing; Project = project info (the Scenes list lands in
/// UX-C).</summary>
public enum EditorRightTab
{
    /// <summary>Entity tree + selection-bound Inspector (today's Scene + Inspector sections).</summary>
    Scene,

    /// <summary>The pipeline registrar listing (today's Systems section).</summary>
    Systems,

    /// <summary>Project info (root path, levels dir, current scene id) — the Scenes list is UX-C.</summary>
    Project,
}

/// <summary>The bottom shelf's active tab. One tab today (Assets — the palette); more shelves are
/// marked terrain.</summary>
public enum EditorBottomTab
{
    /// <summary>The asset palette (the bottom shelf's only tab in UX-B).</summary>
    Assets,
}

/// <summary>Which shell drag interaction currently owns the pointer — the SINGLE ownership token so a
/// splitter drag never also fires a panel row / card / toolbar click, and two drags can never run at
/// once. A drag is claimed on the press edge and held through the release edge; the owner clears it
/// back to <see cref="None"/> the frame AFTER the release (when the button is fully up), so on the
/// release edge every other consumer still sees a non-<see cref="None"/> value and stands down —
/// making the exclusion independent of pipeline weave order (pre-mortem #3).</summary>
public enum ShellDragKind
{
    /// <summary>No drag in progress — chrome clicks fire normally.</summary>
    None,

    /// <summary>Resizing the right strip via its viewport-facing (left-edge) splitter.</summary>
    RightSplitter,

    /// <summary>Resizing the bottom shelf via its viewport-facing (top-edge) splitter.</summary>
    BottomSplitter,

    /// <summary>Dragging the right strip's scrollbar thumb.</summary>
    RightScrollbar,

    /// <summary>Dragging the bottom shelf's scrollbar thumb.</summary>
    BottomScrollbar,
}

/// <summary>A shell region — the marked-terrain layout model. Only <see cref="Right"/> and
/// <see cref="Bottom"/> are built in UX-B; <see cref="Left"/> and <see cref="MenuBar"/> are reserved
/// at size 0 so future drag-docking is a state mutation, not a rearchitect.</summary>
public enum EditorRegion
{
    /// <summary>A menu-bar strip above the toolbar (reserved at 0pt today).</summary>
    MenuBar,

    /// <summary>A left panel strip (reserved at 0pt today).</summary>
    Left,

    /// <summary>The right panel strip (Scene / Systems / Project tabs).</summary>
    Right,

    /// <summary>The bottom shelf (Assets tab).</summary>
    Bottom,
}

/// <summary>A logical panel a region can host — the marked-terrain data model. A future
/// drag-rearrange is a reassignment in <see cref="EditorShellStateComponent.RegionPanels"/>, not new
/// code.</summary>
public enum EditorPanelKind
{
    /// <summary>The pipeline registrar listing.</summary>
    Systems,

    /// <summary>The world's entities as a tree.</summary>
    Scene,

    /// <summary>The selected entity's components + members.</summary>
    Inspector,

    /// <summary>Project info (root, levels dir, current scene id).</summary>
    Project,

    /// <summary>The asset palette.</summary>
    Assets,
}

/// <summary>
/// The editor shell's region-layout state — pure data on a single editor-owned entity, the ONE
/// source of truth for the resizable region sizes, the active tab per region, and the current drag
/// ownership (ECS purity: the <b>state</b> lives here, the <b>behaviour</b> in the shell / panel /
/// palette systems). <c>EditorChromeLayout</c> derives every region rect and the
/// <c>ViewportManager</c> inset from these sizes, so compositing, mouse mapping, and every chrome
/// system keep agreeing frame to frame (the existing single-source-of-truth invariant, now
/// runtime-adjustable).
///
/// <para><b>Marked terrain.</b> <see cref="RegionPanels"/> models the region → panels assignment as
/// data, and <see cref="Left"/>/<see cref="EditorRegion.MenuBar"/> are reserved at size 0 — day-1
/// builds neither, but the architecture already permits future drag-docking / a left strip / a menu
/// bar as pure state mutations.</para>
///
/// <para>All state is <b>in-session</b> (no persistence). The clamp ranges keep a resized strip
/// usable — the defaults reproduce the pre-UX-B constants (<c>EditorChromeLayout.RightPanelWidth</c>
/// / <c>BottomBarHeight</c>) byte-for-byte when nothing touches them.</para>
/// </summary>
public sealed class EditorShellStateComponent
{
    // ── Region-size clamps (logical points) ─────────────────────────────────────────────────────
    /// <summary>The right strip's minimum width, logical points.</summary>
    public const int MinRightWidthPt = 180;
    /// <summary>The right strip's maximum width, logical points.</summary>
    public const int MaxRightWidthPt = 600;
    /// <summary>The bottom shelf's minimum height, logical points.</summary>
    public const int MinBottomHeightPt = 96;
    /// <summary>The bottom shelf's maximum height, logical points.</summary>
    public const int MaxBottomHeightPt = 320;

    /// <summary>The right strip's default width, logical points — mirrors
    /// <c>EditorChromeLayout.RightPanelWidth</c> so an untouched shell is byte-identical.</summary>
    public const int DefaultRightWidthPt = 280;
    /// <summary>The bottom shelf's default height, logical points — mirrors
    /// <c>EditorChromeLayout.BottomBarHeight</c> so an untouched shell is byte-identical.</summary>
    public const int DefaultBottomHeightPt = 168;

    private int _rightWidthPt = DefaultRightWidthPt;
    private int _bottomHeightPt = DefaultBottomHeightPt;

    /// <summary>The right strip width in logical points, always clamped to
    /// <c>[<see cref="MinRightWidthPt"/>, <see cref="MaxRightWidthPt"/>]</c>.</summary>
    public int RightWidthPt
    {
        get => _rightWidthPt;
        set => _rightWidthPt = Math.Clamp(value, MinRightWidthPt, MaxRightWidthPt);
    }

    /// <summary>The bottom shelf height in logical points, always clamped to
    /// <c>[<see cref="MinBottomHeightPt"/>, <see cref="MaxBottomHeightPt"/>]</c>.</summary>
    public int BottomHeightPt
    {
        get => _bottomHeightPt;
        set => _bottomHeightPt = Math.Clamp(value, MinBottomHeightPt, MaxBottomHeightPt);
    }

    /// <summary>The left strip width — reserved at 0 (marked terrain).</summary>
    public int LeftWidthPt => 0;

    /// <summary>The menu-bar strip height — reserved at 0 (marked terrain).</summary>
    public int MenuBarHeightPt => 0;

    // ── Active tabs (one per region) ─────────────────────────────────────────────────────────────
    /// <summary>The right strip's active tab (default <see cref="EditorRightTab.Scene"/>).</summary>
    public EditorRightTab ActiveRightTab = EditorRightTab.Scene;

    /// <summary>The bottom shelf's active tab (default — and only — <see cref="EditorBottomTab.Assets"/>).</summary>
    public EditorBottomTab ActiveBottomTab = EditorBottomTab.Assets;

    // ── Drag ownership ───────────────────────────────────────────────────────────────────────────
    /// <summary>Which drag currently owns the pointer (see <see cref="ShellDragKind"/>).</summary>
    public ShellDragKind ActiveDrag = ShellDragKind.None;

    /// <summary>The region size (pt) or scroll offset (lines/rows) captured when a drag was claimed —
    /// the drag computes an absolute result from this anchor so it never accumulates float drift.</summary>
    public float DragGrabValue;

    /// <summary>The cursor coordinate (device px) captured when a drag was claimed (x for the right
    /// splitter/scrollbar, y for the bottom splitter/scrollbar).</summary>
    public float DragGrabPixel;

    /// <summary>Whether any drag is in progress (a foreign drag makes every other chrome consumer
    /// stand down for the frame).</summary>
    public bool IsDragging => ActiveDrag != ShellDragKind.None;

    // ── Marked terrain: region → panels ──────────────────────────────────────────────────────────
    /// <summary>The region → hosted-panels assignment (marked terrain). A future drag-rearrange is a
    /// reassignment here, not new plumbing. <see cref="EditorRegion.MenuBar"/> / <see cref="EditorRegion.Left"/>
    /// are present-but-empty (reserved at size 0).</summary>
    public readonly IReadOnlyDictionary<EditorRegion, IReadOnlyList<EditorPanelKind>> RegionPanels =
        new Dictionary<EditorRegion, IReadOnlyList<EditorPanelKind>>
        {
            [EditorRegion.MenuBar] = Array.Empty<EditorPanelKind>(),
            [EditorRegion.Left] = Array.Empty<EditorPanelKind>(),
            [EditorRegion.Right] = new[] { EditorPanelKind.Scene, EditorPanelKind.Systems, EditorPanelKind.Project },
            [EditorRegion.Bottom] = new[] { EditorPanelKind.Assets },
        };
}
