using Microsoft.Xna.Framework;
using MonoDreams.LevelEditor.Transform;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the UX3-F modal-transform math (design §5) on the pure <see cref="ModalTransform"/> state
/// machine — grab/scale/rotate live results, axis constraints, the typed-value override (incl. the
/// grab-requires-axis SIMPLIFY-v1 rule), snap composition (mouse-driven only; typed is exact), and the
/// numeric-buffer editing. No world, no cursor, no GraphicsDevice.
/// </summary>
public class ModalTransformTests
{
    private const float Tol = 1e-4f;

    private static ModalTransform Grab(Vector2 entry, Vector2 startPos) =>
        ModalTransform.Enter(EditorModalMode.Grab, entry, Vector2.Zero, startPos, 0f, Vector2.One, Vector2.Zero);

    private static ModalTransform Scale(Vector2 entry, Vector2 pivot, Vector2 startScale) =>
        ModalTransform.Enter(EditorModalMode.Scale, entry, pivot, Vector2.Zero, 0f, startScale, Vector2.Zero);

    private static ModalTransform Rotate(Vector2 entry, Vector2 pivot, float startRot) =>
        ModalTransform.Enter(EditorModalMode.Rotate, entry, pivot, Vector2.Zero, startRot, Vector2.One, Vector2.Zero);

    // ── Grab ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Grab_Free_AppliesTheWorldDeltaFromTheEntryCursor()
    {
        var m = Grab(new Vector2(50f, 50f), new Vector2(10f, 20f));
        var (pos, rot, scale, origin) = m.Result(new Vector2(62f, 46.5f), snapStep: 0f, rotationSnapStep: 0f);

        Assert.Equal(new Vector2(10f + 12f, 20f - 3.5f), pos);
        Assert.Equal(0f, rot);
        Assert.Equal(Vector2.One, scale);
        Assert.Equal(Vector2.Zero, origin); // Origin preserved
    }

    [Fact]
    public void Grab_AxisLock_ZeroesTheOtherComponent()
    {
        var start = new Vector2(10f, 20f);
        var m = Grab(new Vector2(50f, 50f), start);

        m.ToggleAxis(ModalAxis.X);
        Assert.Equal(new Vector2(10f + 12f, 20f), m.Result(new Vector2(62f, 46.5f), 0f, 0f).position);

        m.ToggleAxis(ModalAxis.Y); // a different axis replaces
        Assert.Equal(new Vector2(10f, 20f - 3.5f), m.Result(new Vector2(62f, 46.5f), 0f, 0f).position);
    }

    [Fact]
    public void Grab_Typed_RequiresAnAxis_AppliesAlongIt_ExactlyIgnoringSnap()
    {
        var start = new Vector2(10f, 20f);
        var m = Grab(new Vector2(50f, 50f), start);
        m.TypeChar('2'); m.TypeChar('4'); // buffer "24"

        // No axis → the typed value does NOT apply; the mouse still drives (SIMPLIFY v1).
        Assert.Equal(new Vector2(10f + 12f, 20f - 3.5f),
            m.Result(new Vector2(62f, 46.5f), snapStep: 0f, rotationSnapStep: 0f).position);

        // X locked → +24 along X, EXACT even with snap on (typing is the exact affordance).
        m.ToggleAxis(ModalAxis.X);
        Assert.Equal(new Vector2(10f + 24f, 20f), m.Result(new Vector2(62f, 46.5f), snapStep: 16f, rotationSnapStep: 0f).position);
    }

    [Fact]
    public void Grab_MouseDriven_SnapsThePositionToTheGridStep()
    {
        var m = Grab(Vector2.Zero, new Vector2(10f, 20f));
        // (10+13, 20-27) = (23, -7) → nearest multiple of 16 → (16, 0).
        var pos = m.Result(new Vector2(13f, -27f), snapStep: 16f, rotationSnapStep: 0f).position;
        Assert.Equal(16f, pos.X, Tol);
        Assert.Equal(0f, pos.Y, Tol);
    }

    // ── Scale ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Scale_Free_IsTheDistanceRatioToThePivot()
    {
        var pivot = new Vector2(0f, 0f);
        var m = Scale(new Vector2(10f, 0f), pivot, new Vector2(2f, 3f)); // entry 10 units out
        // Cursor now 20 units out → ratio 2 → scale (4, 6).
        Assert.Equal(new Vector2(4f, 6f), m.Result(new Vector2(20f, 0f), 0f, 0f).scale);
        // Uniform factor exposed for the rig path.
        Assert.Equal(2f, m.UniformScaleFactor(new Vector2(20f, 0f)), Tol);
    }

    [Fact]
    public void Scale_AxisLock_ScalesOnlyThatAxis()
    {
        var m = Scale(new Vector2(10f, 0f), Vector2.Zero, new Vector2(2f, 3f));
        m.ToggleAxis(ModalAxis.X);
        Assert.Equal(new Vector2(4f, 3f), m.Result(new Vector2(20f, 0f), 0f, 0f).scale);
        m.ToggleAxis(ModalAxis.Y);
        Assert.Equal(new Vector2(2f, 6f), m.Result(new Vector2(20f, 0f), 0f, 0f).scale);
    }

    [Fact]
    public void Scale_Typed_IsTheExactFactor_UniformOrPerAxis()
    {
        var m = Scale(new Vector2(10f, 0f), Vector2.Zero, new Vector2(2f, 3f));
        m.TypeChar('2'); // factor 2
        Assert.Equal(new Vector2(4f, 6f), m.Result(new Vector2(999f, 0f), 0f, 0f).scale); // mouse ignored
        Assert.Equal(2f, m.UniformScaleFactor(new Vector2(999f, 0f)), Tol);
        m.ToggleAxis(ModalAxis.Y);
        Assert.Equal(new Vector2(2f, 6f), m.Result(new Vector2(999f, 0f), 0f, 0f).scale);
    }

    [Fact]
    public void Scale_MouseDriven_SnapsTheExtent()
    {
        var m = Scale(new Vector2(10f, 0f), Vector2.Zero, new Vector2(2f, 3f));
        // ratio 1.6 → (3.2, 4.8) → snap step 1 → (3, 5).
        var scale = m.Result(new Vector2(16f, 0f), snapStep: 1f, rotationSnapStep: 0f).scale;
        Assert.Equal(3f, scale.X, Tol);
        Assert.Equal(5f, scale.Y, Tol);
    }

    // ── Rotate ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Rotate_Free_IsTheSweptAngleAboutThePivot_AxisIgnored()
    {
        var pivot = new Vector2(0f, 0f);
        var m = Rotate(new Vector2(40f, 0f), pivot, startRot: 0f); // entry ray +X
        m.ToggleAxis(ModalAxis.X); // rotate ignores axis keys
        // current ray +Y → +90°.
        Assert.Equal(MathHelper.PiOver2, m.Result(new Vector2(0f, 40f), 0f, 0f).rotation, 3);
    }

    [Fact]
    public void Rotate_Typed_IsExactDegrees()
    {
        var m = Rotate(new Vector2(40f, 0f), Vector2.Zero, startRot: 0.25f);
        m.TypeChar('9'); m.TypeChar('0'); // 90 degrees
        Assert.Equal(0.25f + MathHelper.PiOver2, m.Result(new Vector2(0f, 40f), 0f, 0f).rotation, 3);
    }

    [Fact]
    public void Rotate_MouseDriven_SnapsToTheRotationStep()
    {
        var step = MathHelper.ToRadians(15f);
        var m = Rotate(new Vector2(40f, 0f), Vector2.Zero, startRot: 0f);
        // ~40° sweep → nearest multiple of 15° = 45°.
        var rot = m.Result(new Vector2(40f * 0.766f, 40f * 0.643f), snapStep: 0f, rotationSnapStep: step).rotation;
        Assert.Equal(MathHelper.ToRadians(45f), rot, 3);
    }

    // ── Buffer + axis editing ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Buffer_TypeCharBackspaceAndSignAndDot()
    {
        var m = Grab(Vector2.Zero, Vector2.Zero);
        Assert.False(m.TryTypedValue(out _)); // empty

        m.TypeChar('1'); m.TypeChar('2'); m.TypeChar('.'); m.TypeChar('5');
        Assert.True(m.TryTypedValue(out var v));
        Assert.Equal(12.5f, v, Tol);

        m.TypeChar('.'); // a second dot is ignored
        Assert.True(m.TryTypedValue(out v));
        Assert.Equal(12.5f, v, Tol);

        m.TypeChar('-'); // toggle sign
        Assert.True(m.TryTypedValue(out v));
        Assert.Equal(-12.5f, v, Tol);
        m.TypeChar('-'); // toggle back
        Assert.True(m.TryTypedValue(out v));
        Assert.Equal(12.5f, v, Tol);

        m.Backspace(); // "12.5" → "12."
        Assert.True(m.TryTypedValue(out v));
        Assert.Equal(12f, v, Tol);
    }

    [Fact]
    public void Buffer_LoneSignOrDot_IsNotAValue()
    {
        var m = Grab(Vector2.Zero, Vector2.Zero);
        m.TypeChar('-');
        Assert.False(m.TryTypedValue(out _)); // "-" alone → mouse drives
        m.Backspace();
        m.TypeChar('.');
        Assert.False(m.TryTypedValue(out _)); // "." alone → mouse drives
    }

    [Fact]
    public void Axis_SamePressClears_DifferentReplaces()
    {
        var m = Grab(Vector2.Zero, Vector2.Zero);
        Assert.Equal(ModalAxis.None, m.Axis);
        m.ToggleAxis(ModalAxis.X);
        Assert.Equal(ModalAxis.X, m.Axis);
        m.ToggleAxis(ModalAxis.X); // same → clears
        Assert.Equal(ModalAxis.None, m.Axis);
        m.ToggleAxis(ModalAxis.X);
        m.ToggleAxis(ModalAxis.Y); // different → replaces
        Assert.Equal(ModalAxis.Y, m.Axis);
    }
}
