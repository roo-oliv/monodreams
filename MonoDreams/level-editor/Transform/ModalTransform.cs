#nullable enable
using System;
using System.Globalization;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.Transform;

/// <summary>The modal transform family (UX3-F design §5): Blender's <c>G</c>/<c>S</c>/<c>R</c>
/// modal transforms.</summary>
public enum EditorModalMode
{
    /// <summary><c>G</c> — grab: translate by the world-space delta from the entry cursor.</summary>
    Grab,

    /// <summary><c>S</c> — scale: a distance-ratio factor about the pivot (camera entity ⇒ zoom).</summary>
    Scale,

    /// <summary><c>R</c> — rotate: the swept angle about the pivot.</summary>
    Rotate,
}

/// <summary>The modal transform's axis constraint. <see cref="None"/> = free; <see cref="X"/>/<see cref="Y"/>
/// lock the edit to that world axis (a second press of the same key clears it, a different key replaces).</summary>
public enum ModalAxis
{
    /// <summary>No constraint — the free two-axis edit.</summary>
    None,

    /// <summary>Constrained to world X (grab zeroes ΔY, scale edits only Scale.X).</summary>
    X,

    /// <summary>Constrained to world Y (grab zeroes ΔX, scale edits only Scale.Y).</summary>
    Y,
}

/// <summary>
/// The pure, world-free state machine + math behind the editor's Blender-style modal transforms
/// (UX3-F design §5) — the <see cref="GizmoTransform"/> / <c>CameraNav</c> pattern, so it is
/// unit-testable without a world, a cursor, or a GraphicsDevice. It carries the entry anchor + the
/// drag-start transform and turns the CURRENT cursor world point (moved WITHOUT a button held) into a
/// live transform result; a typed numeric buffer OVERRIDES the mouse.
///
/// <para><b>Per-mode geometry.</b> <see cref="EditorModalMode.Grab"/> = the world delta from the
/// entry cursor (axis-constrained by zeroing the other component);
/// <see cref="EditorModalMode.Scale"/> = the distance-ratio factor to the pivot (axis-constrained =
/// per-axis scale); <see cref="EditorModalMode.Rotate"/> = the swept angle about the pivot
/// (<see cref="GizmoTransform.RotationDelta"/>; axis keys ignored). The local <c>Origin</c> is
/// preserved unchanged, exactly like the gizmo.</para>
///
/// <para><b>Numeric entry (typed OVERRIDES the mouse).</b> Digits / <c>-</c> / <c>.</c> build
/// <see cref="NumericBuffer"/>; when it parses to a value it replaces the mouse-derived amount:
/// <b>grab</b> = units along the constrained axis (SIMPLIFY v1 — a typed grab REQUIRES an axis
/// constraint; the status hint prompts "press X or Y" otherwise), <b>scale</b> = a factor (uniform,
/// or per-axis when constrained), <b>rotate</b> = degrees. A typed value is <b>exact</b> — snap does
/// NOT re-quantize it (typing IS the exact affordance; the readout literally says "type = exact").</para>
///
/// <para><b>Snap composition.</b> With snap on, the MOUSE-driven result is quantized exactly like a
/// gizmo drag (<see cref="GizmoTransform.Snap(Microsoft.Xna.Framework.Vector2,float)"/> for the grab
/// position + the scale extent, the rotation step for rotate).</para>
/// </summary>
public struct ModalTransform
{
    /// <summary>Whether a modal session is in progress.</summary>
    public bool IsActive;

    /// <summary>The active mode (grab / scale / rotate).</summary>
    public EditorModalMode Mode;

    /// <summary>The current axis constraint (see <see cref="ModalAxis"/>).</summary>
    public ModalAxis Axis;

    /// <summary>The typed numeric buffer ("" = empty; a value here OVERRIDES the mouse).</summary>
    public string NumericBuffer;

    /// <summary>The cursor world position captured at entry — the grab/scale anchor.</summary>
    public Vector2 EntryCursorWorld;

    /// <summary>The rotate/scale pivot (the entity's world pivot at entry).</summary>
    public Vector2 WorldPivot;

    /// <summary>The drag-start transform fields (LOCAL, what <c>TransformComponent</c> stores).</summary>
    public Vector2 StartPosition;

    /// <summary>See <see cref="StartPosition"/>.</summary>
    public float StartRotation;

    /// <summary>See <see cref="StartPosition"/>.</summary>
    public Vector2 StartScale;

    /// <summary>See <see cref="StartPosition"/> — preserved unchanged through every edit.</summary>
    public Vector2 StartOrigin;

    /// <summary>Begins a modal session in <paramref name="mode"/> from the entry cursor + the
    /// drag-start transform. Axis free, buffer empty.</summary>
    public static ModalTransform Enter(
        EditorModalMode mode, Vector2 entryCursorWorld, Vector2 worldPivot,
        Vector2 startPosition, float startRotation, Vector2 startScale, Vector2 startOrigin) => new()
    {
        IsActive = true,
        Mode = mode,
        Axis = ModalAxis.None,
        NumericBuffer = string.Empty,
        EntryCursorWorld = entryCursorWorld,
        WorldPivot = worldPivot,
        StartPosition = startPosition,
        StartRotation = startRotation,
        StartScale = startScale,
        StartOrigin = startOrigin,
    };

    /// <summary>Toggles the axis lock: the same axis clears it, a different axis replaces it
    /// (<see cref="ModalAxis.None"/> is a no-op).</summary>
    public void ToggleAxis(ModalAxis axis)
    {
        if (axis == ModalAxis.None) return;
        Axis = Axis == axis ? ModalAxis.None : axis;
    }

    /// <summary>Appends one typed character to <see cref="NumericBuffer"/>: a digit appends, <c>.</c>
    /// appends once, <c>-</c> toggles the leading sign; anything else is ignored.</summary>
    public void TypeChar(char c)
    {
        NumericBuffer ??= string.Empty;
        if (c >= '0' && c <= '9') NumericBuffer += c;
        else if (c == '.') { if (!NumericBuffer.Contains('.')) NumericBuffer += c; }
        else if (c == '-') NumericBuffer = NumericBuffer.StartsWith("-") ? NumericBuffer.Substring(1) : "-" + NumericBuffer;
    }

    /// <summary>Removes the last character of <see cref="NumericBuffer"/> (a no-op when empty).</summary>
    public void Backspace()
    {
        if (!string.IsNullOrEmpty(NumericBuffer))
            NumericBuffer = NumericBuffer.Substring(0, NumericBuffer.Length - 1);
    }

    /// <summary>Whether <see cref="NumericBuffer"/> parses to a finite value (a lone <c>-</c>/<c>.</c>
    /// or empty buffer does not — the mouse then drives).</summary>
    public readonly bool TryTypedValue(out float value)
    {
        value = 0f;
        if (string.IsNullOrEmpty(NumericBuffer)) return false;
        return float.TryParse(NumericBuffer, NumberStyles.Float | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// The uniform scale factor a scale session produces at <paramref name="currentCursorWorld"/>: the
    /// typed value when the buffer parses, else the distance ratio |cursor − pivot| / |entry − pivot|
    /// (1 when the entry cursor sat on the pivot). Exposed so the camera-entity zoom path can divide by the
    /// SAME factor (a bigger frustum ⇒ a lower zoom) without duplicating the mapping.
    /// </summary>
    public readonly float UniformScaleFactor(Vector2 currentCursorWorld)
    {
        if (TryTypedValue(out var typed)) return typed;
        var entryDist = (EntryCursorWorld - WorldPivot).Length();
        if (entryDist < 1e-4f) return 1f;
        return (currentCursorWorld - WorldPivot).Length() / entryDist;
    }

    /// <summary>
    /// The live transform result for the current cursor, applying the axis constraint, the typed
    /// override, and (mouse-driven only) grid snap. The result is LOCAL transform fields, ready to hand
    /// to <c>TransformEditCommand</c>. <paramref name="snapStep"/>/<paramref name="rotationSnapStep"/>
    /// &gt; 0 quantize the mouse-driven grab position / scale extent / rotation.
    /// </summary>
    public readonly (Vector2 position, float rotation, Vector2 scale, Vector2 origin) Result(
        Vector2 currentCursorWorld, float snapStep, float rotationSnapStep)
    {
        switch (Mode)
        {
            case EditorModalMode.Grab:
            {
                var typed = Axis != ModalAxis.None && TryTypedValue(out _);
                Vector2 delta;
                if (typed)
                {
                    TryTypedValue(out var v);
                    delta = Axis == ModalAxis.X ? new Vector2(v, 0f) : new Vector2(0f, v);
                }
                else
                {
                    delta = currentCursorWorld - EntryCursorWorld;
                    if (Axis == ModalAxis.X) delta.Y = 0f;
                    else if (Axis == ModalAxis.Y) delta.X = 0f;
                }
                var position = StartPosition + delta;
                if (!typed && snapStep > 0f) position = GizmoTransform.Snap(position, snapStep);
                return (position, StartRotation, StartScale, StartOrigin);
            }
            case EditorModalMode.Scale:
            {
                var typed = TryTypedValue(out _);
                var factor = UniformScaleFactor(currentCursorWorld);
                var scale = Axis switch
                {
                    ModalAxis.X => new Vector2(StartScale.X * factor, StartScale.Y),
                    ModalAxis.Y => new Vector2(StartScale.X, StartScale.Y * factor),
                    _ => StartScale * factor,
                };
                if (!typed && snapStep > 0f) scale = GizmoTransform.Snap(scale, snapStep);
                return (StartPosition, StartRotation, scale, StartOrigin);
            }
            case EditorModalMode.Rotate:
            {
                var typed = TryTypedValue(out var deg);
                var angle = typed
                    ? MathHelper.ToRadians(deg)
                    : GizmoTransform.RotationDelta(WorldPivot, EntryCursorWorld, currentCursorWorld);
                var rotation = StartRotation + angle;
                if (!typed && rotationSnapStep > 0f) rotation = GizmoTransform.Snap(rotation, rotationSnapStep);
                return (StartPosition, rotation, StartScale, StartOrigin);
            }
            default:
                return (StartPosition, StartRotation, StartScale, StartOrigin);
        }
    }

    /// <summary>The status-bar readout for the current cursor: the applied ΔX/ΔY (grab), per-axis
    /// factors (scale), or degrees (rotate), plus the axis tag + buffer. <paramref name="isCameraZoom"/>
    /// flips the scale label to "Zoom" (the camera entity's Scale edits <c>CameraComponent.Zoom</c>, not
    /// <c>Transform.Scale</c>).</summary>
    public readonly ModalReadout Readout(Vector2 currentCursorWorld, float snapStep, float rotationSnapStep, bool isCameraZoom)
    {
        var (position, rotation, scale, _) = Result(currentCursorWorld, snapStep, rotationSnapStep);
        var dx = position.X - StartPosition.X;
        var dy = position.Y - StartPosition.Y;
        var fx = MathF.Abs(StartScale.X) > 1e-6f ? scale.X / StartScale.X : 1f;
        var fy = MathF.Abs(StartScale.Y) > 1e-6f ? scale.Y / StartScale.Y : 1f;
        var degrees = MathHelper.ToDegrees(GizmoTransform.WrapAngle(rotation - StartRotation));
        return new ModalReadout(Mode, isCameraZoom, Axis, NumericBuffer ?? string.Empty, dx, dy, fx, fy, degrees);
    }
}

/// <summary>
/// The pure, live readout of a modal transform session — the values <see cref="MonoDreams.LevelEditor.UI.StatusBarModel"/>
/// formats for the status bar's left side. Which fields are meaningful depends on <see cref="Mode"/>:
/// grab → <see cref="DX"/>/<see cref="DY"/>; scale → <see cref="FactorX"/>/<see cref="FactorY"/>;
/// rotate → <see cref="Degrees"/>.
/// </summary>
public readonly struct ModalReadout
{
    public ModalReadout(EditorModalMode mode, bool isCameraZoom, ModalAxis axis, string buffer,
        float dx, float dy, float factorX, float factorY, float degrees)
    {
        Mode = mode;
        IsCameraZoom = isCameraZoom;
        Axis = axis;
        Buffer = buffer;
        DX = dx;
        DY = dy;
        FactorX = factorX;
        FactorY = factorY;
        Degrees = degrees;
    }

    /// <summary>The session mode.</summary>
    public EditorModalMode Mode { get; }

    /// <summary>Whether the target is the camera entity (scale ⇒ "Zoom" rather than "Scale").</summary>
    public bool IsCameraZoom { get; }

    /// <summary>The current axis constraint.</summary>
    public ModalAxis Axis { get; }

    /// <summary>The current numeric buffer ("" = empty).</summary>
    public string Buffer { get; }

    /// <summary>Grab: the applied world ΔX / ΔY.</summary>
    public float DX { get; }

    /// <summary>See <see cref="DX"/>.</summary>
    public float DY { get; }

    /// <summary>Scale: the applied per-axis factor (both equal when unconstrained).</summary>
    public float FactorX { get; }

    /// <summary>See <see cref="FactorX"/>.</summary>
    public float FactorY { get; }

    /// <summary>Rotate: the applied angle in degrees.</summary>
    public float Degrees { get; }
}
