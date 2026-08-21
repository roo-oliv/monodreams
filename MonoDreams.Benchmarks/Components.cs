using System.Numerics;

namespace MonoDreams.Benchmarks;

/// <summary>
/// The struct-component case: two small, blittable structs, the shape MonoDreams uses for the
/// per-frame numeric payload a system reads and writes every tick (physics integration, culling
/// bounds). Deliberately tiny so the measurement reports the ECS's own storage and dispatch cost
/// rather than the component's.
/// </summary>
public struct BenchPosition
{
    public float X;
    public float Y;
}

/// <summary>Second struct component, so every benchmark reads one and writes another.</summary>
public struct BenchVelocity
{
    public float X;
    public float Y;
}

/// <summary>
/// The zero-sized tag the structural-churn benchmark adds and removes. Mirrors
/// <c>MonoDreams.Component.Draw.VisibleComponent</c> exactly — including having no fields — which is
/// the empty tag <c>CullingSystem</c> adds to and removes from entities every single frame: the
/// engine's real structural churn, and the one that costs an archetype move under an archetype
/// backend but only a bitmask flip under a sparse-set one.
/// <para>
/// See <see cref="BenchTagByte"/> for why the same churn is also measured with a one-byte tag.
/// </para>
/// </summary>
public struct BenchVisible
{
    // Tag component — no data, exactly like VisibleComponent.
}

/// <summary>
/// A tag carrying one byte of payload — same role as <see cref="BenchVisible"/>, but NOT zero-sized.
/// <para>
/// It exists because DefaultEcs 0.18.0-beta01 treats a zero-sized component specially, and its
/// <c>Remove</c> for that special case degrades with the number of entities currently carrying the
/// component (measured on this machine: ~1.1 µs per removal with 1k tagged entities, ~12.8 µs with
/// 100k, ~26 µs with 200k — quadratic over a full sweep, while a one-byte tag removes in ~5 ns).
/// Benchmarking only the zero-sized shape would report that one pathology instead of the
/// sparse-set-vs-archetype difference the migration is actually choosing between, so the churn
/// family measures both shapes and <c>RESULTS.md</c> reads them apart.
/// </para>
/// </summary>
public struct BenchTagByte
{
    public byte Value;
}

/// <summary>Value stand-in for <c>Microsoft.Xna.Framework.Rectangle</c> (a sprite's source rect).</summary>
public struct BenchRectangle
{
    public int X;
    public int Y;
    public int Width;
    public int Height;
}

/// <summary>
/// The managed-component case: a class shaped like <c>MonoDreams.Component.Draw.DrawComponent</c> —
/// the engine's unified render component, which is a CLASS carrying ~12 fields of mixed value and
/// reference data, mutated in place by the prep systems without republishing.
/// <para>
/// MonoGame types are mirrored, not referenced (Vector2 → <see cref="System.Numerics.Vector2"/>,
/// Color → packed <see cref="uint"/>, Texture2D/BitmapFont → <see cref="object"/>), so the benchmark
/// project stays free of the engine and its content pipeline. What is being measured is what a
/// backend does with a REFERENCE-typed component — an object array slot plus a pointer chase per
/// access — and that cost does not depend on which struct the value fields came from.
/// </para>
/// </summary>
public sealed class BenchDrawComponent
{
    // Draw element type + render target (DrawElementType / RenderTargetID enums in the engine).
    public int Type;
    public int Target;

    // Transform fields, present for every draw type.
    public Vector2 Position;
    public float Rotation;
    public Vector2 Origin;
    public Vector2 Scale = Vector2.One;
    public uint Color = 0xFFFFFFFF;
    public float LayerDepth;

    // Sprite-specific.
    public object? Texture;
    public BenchRectangle? SourceRectangle;
    public Vector2 Size;
    public bool FlipHorizontally;
    public bool FlipVertically;

    // Text-specific.
    public object? Font;
    public string? Text;
}
