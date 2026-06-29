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
/// The gizmo's configuration — pure data on a single editor-owned state entity that the toolbar
/// mutates and <c>GizmoSystem</c> reads. ECS purity: this carries only the persistent settings
/// (which tool is active, whether grid-snap is on, the grid step); the transient per-drag
/// accumulation lives in <c>GizmoSystem</c>'s private fields (it is hot-path frame state, not data
/// other systems read), and the visible handle/highlight overlays are separate standalone entities.
///
/// <para>The toolbar drives this: a tool-select button sets <see cref="Tool"/>, the snap toggle
/// flips <see cref="SnapEnabled"/>. <c>GizmoSystem</c> never owns a second copy — it reads this one
/// instance so the toolbar and the gizmo agree.</para>
/// </summary>
public struct GizmoStateComponent
{
    /// <summary>The active transform tool (move / rotate / scale).</summary>
    public GizmoTool Tool;

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

    /// <summary>A sensible default: move tool, snap off, a 16-unit grid, 15° rotation step.</summary>
    public static GizmoStateComponent Default => new()
    {
        Tool = GizmoTool.Move,
        SnapEnabled = false,
        GridStep = 16f,
        RotationStepRadians = MathHelperPi / 12f, // 15 degrees
    };

    // Local constant to avoid a Microsoft.Xna.Framework dependency in this pure-data file.
    private const float MathHelperPi = 3.14159265358979f;
}
