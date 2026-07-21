#nullable enable
namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// The transform tool the gizmo currently exposes. The toolbar's tool-select buttons set
/// <see cref="GizmoStateComponent.Tool"/> to one of these; <c>GizmoSystem</c> draws and hit-tests
/// the matching handle and interprets a drag accordingly.
/// </summary>
public enum GizmoTool
{
    /// <summary>Drag the centre handle to translate the selected entity's <c>Position</c>.</summary>
    Move,

    /// <summary>Drag the rotate ring to spin the selected entity about its <c>Origin</c>.</summary>
    Rotate,

    /// <summary>Drag the scale handle to scale the selected entity about its <c>Origin</c>.</summary>
    Scale,
}

/// <summary>
/// The editor's coarse <b>tool modality</b> (island-authoring plan §3.1 / wave-repass §S1): which
/// tool FAMILY owns a press in the game viewport. Exactly one mode is active at a time on the
/// shared <see cref="GizmoStateComponent"/>; <c>SelectionSystem</c> and <c>GizmoSystem</c> process
/// viewport presses <b>only</b> in <see cref="SelectTransform"/> (they early-out otherwise —
/// activating a placement/brush tool visibly deactivates the transform gizmo, the Unity/Godot
/// convention), and the placement/brush systems act only in their own mode. This composes with the
/// finer <see cref="GizmoStateComponent.PressClaimed"/> click-ownership rule, which keeps resolving
/// handle-vs-scene <i>within</i> <see cref="SelectTransform"/>.
/// </summary>
public enum EditorToolMode
{
    /// <summary>The default: clicks select; the transform gizmo drags. The only mode in which
    /// <c>SelectionSystem</c>/<c>GizmoSystem</c> act on viewport presses.</summary>
    SelectTransform,

    /// <summary>Free placement (the palette armed an item OR a trigger type): a ghost preview
    /// follows the cursor and a click stamps a prop / trigger zone through the snapshotting create
    /// command. Escape / right-click / a transform-tool button disarms back to
    /// <see cref="SelectTransform"/>.</summary>
    Place,

    /// <summary>Freeform boundary authoring (island-authoring §5.2): clicks lay polyline vertices
    /// with a live preview line; Enter or double-click commits the whole lay as one undo step
    /// (creating the <c>BoundaryComponent</c> authoring entity), Escape / right-click cancels. Like
    /// <see cref="Place"/>, selection and the transform gizmo are dormant in this mode.</summary>
    Boundary,

    // Reserved mode names for the later brush waves (wave-repass §S1) — added here (not
    // implemented) so the modality contract is stable: Scatter (Wave D scatter brush),
    // GroundPaint (Wave E ground canvas), Road (Wave F spline tool).
}

/// <summary>
/// The gizmo's configuration — pure data on a single editor-owned state entity that the toolbar
/// mutates and <c>GizmoSystem</c> reads. ECS purity: this carries the persistent settings
/// (which tool is active, whether grid-snap is on, the grid step) plus the one piece of per-frame
/// gizmo state another system must observe — the <see cref="PressClaimed"/> click-ownership flag;
/// the rest of the transient per-drag accumulation lives in <c>GizmoSystem</c>'s private fields
/// (hot-path frame state no other system reads), and the visible handle/highlight overlays are
/// separate standalone entities.
///
/// <para>The toolbar drives this: a tool-select button sets <see cref="Tool"/>, the snap toggle
/// flips <see cref="SnapEnabled"/>. <c>GizmoSystem</c> never owns a second copy — it reads this one
/// instance so the toolbar and the gizmo agree.</para>
/// </summary>
public struct GizmoStateComponent
{
    /// <summary>
    /// Click-ownership claim (frame-scoped): true while the gizmo owns the cursor's left press —
    /// the press edge landed on the active tool's handle (a proxy target forces the Move handle),
    /// or a handle drag is in progress. <c>GizmoSystem</c> writes it on <b>every</b> Edit frame it
    /// runs (set or cleared, so it cannot go stale while the gizmo is running); <c>SelectionSystem</c>
    /// reads it the <b>same frame</b> and skips a claimed press entirely — no re-pick, no
    /// click-empty clear. Without the claim, pressing a rotate/scale handle that lies outside the
    /// selected sprite's bounds reads as a click on empty space and clears the selection in the
    /// same frame the drag began, killing the drag (and despawning gizmo overlays / collider
    /// proxies). The same-frame read is safe by pipeline ordering: the gizmo runs in the UPDATE
    /// pipeline, selection at the end of the DRAW pipeline.
    /// </summary>
    public bool PressClaimed;

    /// <summary>The active transform tool (move / rotate / scale).</summary>
    public GizmoTool Tool;

    /// <summary>The coarse tool modality (see <see cref="EditorToolMode"/>): which tool family owns
    /// a viewport press. Selection and the gizmo act only in
    /// <see cref="EditorToolMode.SelectTransform"/>; the palette's placement acts only in
    /// <see cref="EditorToolMode.Place"/>. The palette system arms/disarms it; the toolbar's
    /// transform-tool buttons reset it to <see cref="EditorToolMode.SelectTransform"/> (a radio
    /// over the modes).</summary>
    public EditorToolMode Mode;

    /// <summary>When true, a drag's world-space result is quantized to <see cref="GridStep"/>
    /// (translate snaps the position to the grid; rotate snaps to <see cref="RotationStepRadians"/>;
    /// scale snaps the scaled extent to the grid). When false the raw drag delta is applied.</summary>
    public bool SnapEnabled;

    /// <summary>The grid quantum in world units used when <see cref="SnapEnabled"/> is on. Must be
    /// &gt; 0 to snap; a non-positive value disables snapping even when <see cref="SnapEnabled"/>.</summary>
    public float GridStep;

    /// <summary>The rotation quantum in radians used when snapping a rotate drag. A non-positive
    /// value disables rotation snapping.</summary>
    public float RotationStepRadians;

    /// <summary>
    /// The minimum world-unit distance between successive stamps of a palette <b>hold-drag</b>
    /// (island-authoring Slice 4 multi-stamp — the embryo of the future scatter brush). While a
    /// palette item is armed in <see cref="EditorToolMode.Place"/>, holding the left button and
    /// dragging stamps the armed prop repeatedly, one stamp per <see cref="StampSpacing"/> of
    /// arc-length travelled (a plain, jitter-free spacing — no seed). A single click still places
    /// exactly one; a non-positive value disables the multi-stamp (a click still places one). This
    /// lives on the shared editor-state entity beside <see cref="GridStep"/> (the natural home for
    /// the persistent brush setting — the future BrushState fields graduate here), so the palette
    /// reads it the same way it reads snap.
    /// </summary>
    public float StampSpacing;

    /// <summary>A sensible default: select/transform modality, move tool, snap off, a 16-unit
    /// grid, 15° rotation step, 32-unit multi-stamp spacing.</summary>
    public static GizmoStateComponent Default => new()
    {
        Tool = GizmoTool.Move,
        Mode = EditorToolMode.SelectTransform,
        SnapEnabled = false,
        GridStep = 16f,
        RotationStepRadians = MathHelperPi / 12f, // 15 degrees
        StampSpacing = 32f,
    };

    // Local constant to avoid a Microsoft.Xna.Framework dependency in this pure-data file.
    private const float MathHelperPi = 3.14159265358979f;
}
