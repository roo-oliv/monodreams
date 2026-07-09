#nullable enable
using System;
using System.Collections.Generic;
using MonoDreams.LevelEditor.UI;

namespace MonoDreams.LevelEditor.Component;

/// <summary>A left-strip tab (region-agnostic by name so a future region can reuse it — UX2-B moved
/// the tab group from the right strip to the left). <see cref="Entities"/> = the entity tree;
/// <see cref="Systems"/> = the pipeline listing; <see cref="Scenes"/> = the scene catalog + project
/// info. The Inspector is no longer a tab — it is the dedicated right panel.</summary>
public enum EditorPanelTab
{
    /// <summary>The world's entities as a tree (was the "Scene" tab; the Inspector left it).</summary>
    Entities,

    /// <summary>The pipeline registrar listing (unchanged content).</summary>
    Systems,

    /// <summary>The scene catalog + project info (was the "Project" tab).</summary>
    Scenes,
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

    /// <summary>Resizing the left strip via its viewport-facing (right-edge) splitter.</summary>
    LeftSplitter,

    /// <summary>Resizing the right strip via its viewport-facing (left-edge) splitter.</summary>
    RightSplitter,

    /// <summary>Resizing the bottom shelf via its viewport-facing (top-edge) splitter.</summary>
    BottomSplitter,

    /// <summary>Dragging the left strip's scrollbar thumb.</summary>
    LeftScrollbar,

    /// <summary>Dragging the right strip's scrollbar thumb.</summary>
    RightScrollbar,

    /// <summary>Dragging the bottom shelf's scrollbar thumb.</summary>
    BottomScrollbar,
}

/// <summary>A shell region — the marked-terrain layout model. <see cref="Left"/> (UX2-B: the tab
/// group), <see cref="Right"/> (the Inspector) and <see cref="Bottom"/> (the Assets shelf) are built;
/// <see cref="MenuBar"/> is reserved at size 0 so a future menu bar is a state mutation, not a
/// rearchitect.</summary>
public enum EditorRegion
{
    /// <summary>A menu-bar strip above the toolbar (reserved at 0pt today).</summary>
    MenuBar,

    /// <summary>The left panel strip (Entities / Systems / Scenes tabs — UX2-B).</summary>
    Left,

    /// <summary>The right panel strip (the dedicated Inspector — UX2-B).</summary>
    Right,

    /// <summary>The bottom shelf (Assets tab).</summary>
    Bottom,

    /// <summary>The window status bar strip (UX3-F) — chrome flush with the window bottom, below the
    /// bottom shelf; part of the ONE viewport inset (see <c>EditorChromeLayout.StatusBar</c>).</summary>
    StatusBar,
}

/// <summary>A logical panel a region can host — the marked-terrain data model. A future
/// drag-rearrange is a reassignment in <see cref="EditorShellStateComponent.RegionPanels"/>, not new
/// code.</summary>
public enum EditorPanelKind
{
    /// <summary>The pipeline registrar listing.</summary>
    Systems,

    /// <summary>The world's entities as a tree.</summary>
    Entities,

    /// <summary>The selected entity's components + members.</summary>
    Inspector,

    /// <summary>The scene catalog + project info (root, levels dir, current scene id).</summary>
    Scenes,

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
/// data, and <see cref="EditorRegion.MenuBar"/> is reserved at size 0 — day-1 builds no menu bar, but
/// the architecture already permits a future menu bar / drag-docking as pure state mutations.</para>
///
/// <para>All state is <b>in-session</b> (no persistence). The clamp ranges keep a resized strip
/// usable — the defaults reproduce <c>EditorChromeLayout</c>'s constants
/// (<c>LeftPanelWidth</c> / <c>RightPanelWidth</c> / <c>BottomBarHeight</c>) byte-for-byte when
/// nothing touches them.</para>
/// </summary>
public sealed class EditorShellStateComponent
{
    // ── Region-size clamps (logical points) ─────────────────────────────────────────────────────
    /// <summary>The left strip's minimum width, logical points.</summary>
    public const int MinLeftWidthPt = 180;
    /// <summary>The left strip's maximum width, logical points.</summary>
    public const int MaxLeftWidthPt = 600;
    /// <summary>The right strip's minimum width, logical points.</summary>
    public const int MinRightWidthPt = 180;
    /// <summary>The right strip's maximum width, logical points.</summary>
    public const int MaxRightWidthPt = 600;
    /// <summary>The bottom shelf's minimum height, logical points.</summary>
    public const int MinBottomHeightPt = 96;
    /// <summary>The bottom shelf's maximum height, logical points.</summary>
    public const int MaxBottomHeightPt = 320;

    /// <summary>The left strip's default width, logical points — mirrors
    /// <c>EditorChromeLayout.LeftPanelWidth</c> so an untouched shell is byte-identical.</summary>
    public const int DefaultLeftWidthPt = 240;
    /// <summary>The right strip's default width, logical points — mirrors
    /// <c>EditorChromeLayout.RightPanelWidth</c> so an untouched shell is byte-identical.</summary>
    public const int DefaultRightWidthPt = 280;
    /// <summary>The bottom shelf's default height, logical points — mirrors
    /// <c>EditorChromeLayout.BottomBarHeight</c> so an untouched shell is byte-identical.</summary>
    public const int DefaultBottomHeightPt = 168;

    private int _leftWidthPt = DefaultLeftWidthPt;
    private int _rightWidthPt = DefaultRightWidthPt;
    private int _bottomHeightPt = DefaultBottomHeightPt;

    /// <summary>The left strip width in logical points, always clamped to
    /// <c>[<see cref="MinLeftWidthPt"/>, <see cref="MaxLeftWidthPt"/>]</c> (UX2-B activated the left
    /// region UX-B reserved at 0).</summary>
    public int LeftWidthPt
    {
        get => _leftWidthPt;
        set => _leftWidthPt = Math.Clamp(value, MinLeftWidthPt, MaxLeftWidthPt);
    }

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

    /// <summary>The menu-bar strip height — reserved at 0 (marked terrain).</summary>
    public int MenuBarHeightPt => 0;

    /// <summary>The window status bar height (UX3-F) — a fixed thin strip (not resizable), mirroring
    /// <c>EditorChromeLayout.StatusBarHeight</c>. Part of the ONE viewport-inset bottom margin.</summary>
    public int StatusBarHeightPt => EditorChromeLayout.StatusBarHeight;

    // ── Active tabs (one per region) ─────────────────────────────────────────────────────────────
    /// <summary>The left strip's active tab (default <see cref="EditorPanelTab.Entities"/>).</summary>
    public EditorPanelTab ActiveLeftTab = EditorPanelTab.Entities;

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
    /// reassignment here, not new plumbing. <see cref="EditorRegion.MenuBar"/> is present-but-empty
    /// (reserved at size 0). UX2-B: the tab group lives in <see cref="EditorRegion.Left"/> and the
    /// Inspector is the dedicated <see cref="EditorRegion.Right"/> panel.</summary>
    public readonly IReadOnlyDictionary<EditorRegion, IReadOnlyList<EditorPanelKind>> RegionPanels =
        new Dictionary<EditorRegion, IReadOnlyList<EditorPanelKind>>
        {
            [EditorRegion.MenuBar] = Array.Empty<EditorPanelKind>(),
            [EditorRegion.Left] = new[] { EditorPanelKind.Entities, EditorPanelKind.Systems, EditorPanelKind.Scenes },
            [EditorRegion.Right] = new[] { EditorPanelKind.Inspector },
            [EditorRegion.Bottom] = new[] { EditorPanelKind.Assets },
            [EditorRegion.StatusBar] = Array.Empty<EditorPanelKind>(), // UX3-F: chrome-only strip (no panel)
        };
}
