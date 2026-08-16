using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoDreams.Renderer;

/// <summary>
/// How the presented image reached the screen — the step of the presentation chain that won for
/// the current window (see <see cref="PresentationPolicy"/>).
/// </summary>
public enum PresentationMode
{
    /// <summary>The destination was GROWN past the aspect-fit rectangle to land on a clean scale:
    /// the frame is slightly larger than the window, so its edges leave the screen. The compensating
    /// move is to render slightly MORE world — see
    /// <see cref="PresentationPolicy.ResolveRenderSize"/>.</summary>
    Overscan,

    /// <summary>The aspect-fit rectangle at a clean scale — the letter/pillarbox step. Bars appear
    /// where the window is wider or taller than the frame; when the window's aspect matches the
    /// render resolution's and the scale is already clean, there are none.</summary>
    Letterbox,

    /// <summary>The exact aspect-fit rectangle at whatever (usually fractional) scale the window
    /// implies — the historical behaviour, and the only step where the per-layer sampler question
    /// really arises.</summary>
    Stretch,
}

/// <summary>Granularity of the clean-scale ladder a <see cref="PresentationPolicy"/> snaps to.</summary>
public enum CleanScaleSteps
{
    /// <summary>Whole steps only: …, 1/3, 1/2, 1, 2, 3, … — one source pixel is a whole number of
    /// screen pixels (or the other way round). The strictest, and what pixel art wants.</summary>
    Integer,

    /// <summary>Whole and half steps: …, 1/2.5, 1/2, 1/1.5, 1, 1.5, 2, 2.5, … A half step repeats a
    /// FIXED 1-2-1-2 pixel pattern instead of an arbitrary one, so it does not crawl the way a
    /// fractional scale does, and it lands much closer to the window than the whole step below
    /// it.</summary>
    Half,
}

/// <summary>
/// Per-layer sampling choice for the final composite (<c>RenderLayer.Sampler</c>).
/// </summary>
public enum SamplerPolicy
{
    /// <summary>Point at an integer scale (1×, 2×, 3× — where nearest-neighbour is exact), linear
    /// otherwise. The default: pixel art stays crisp where crispness is achievable and UI text stops
    /// shimmering where it is not.</summary>
    Auto,

    /// <summary>Always nearest-neighbour, whatever the scale — for a layer that is opinionated
    /// about it (a chunky pixel-art layer that wants point even at 0.8×).</summary>
    Point,

    /// <summary>Always bilinear, whatever the scale.</summary>
    Linear,
}

/// <summary>The resolved presentation for one window: which step of the chain won, the uniform
/// scale it presents the render target at, and the resulting destination SIZE (the
/// <c>ViewportManager</c> centers it in the available area).</summary>
public readonly record struct PresentationResolution(PresentationMode Mode, float Scale, int Width, int Height);

/// <summary>
/// The declared answer to "the window is not the render resolution — now what?", in preference
/// order. Pure data + pure math: <see cref="Resolve"/> takes a window and a render resolution and
/// returns the destination the compositor should draw to; <c>ViewportManager</c> owns the placement
/// and the mouse inversion of that same rectangle.
///
/// <para>The chain, in the order it is tried:</para>
/// <list type="number">
/// <item><b>Overscan to a clean scale</b> (<see cref="AllowOverscan"/>) — spend up to
/// <see cref="OverscanTolerance"/> of extra scale to reach the clean step ABOVE the aspect-fit
/// scale. The frame then overflows the window and its edges leave the screen, which is why the
/// tolerance is a GAMEPLAY dial, not a cosmetic one: the honest way to spend it is to render that
/// much more world (<see cref="ResolveRenderSize"/> sizes the render targets so the overflow is
/// zero and the extra scale becomes extra view).</item>
/// <item><b>Letterbox / pillarbox at a clean scale</b> (<see cref="AllowLetterbox"/>) — drop to the
/// clean step BELOW the aspect-fit scale and pad with bars, as long as the drop costs no more than
/// <see cref="LetterboxTolerance"/>.</item>
/// <item><b>Stretch</b> (<see cref="AllowStretch"/>) — the exact aspect-fit rectangle at a
/// fractional scale (the historical behaviour). Only here is the picture resampled at an arbitrary
/// ratio, which is what the per-layer <see cref="SamplerPolicy"/> answers.</item>
/// </list>
///
/// <para>The chain always terminates: with <see cref="AllowStretch"/> off, the last step is the
/// clean scale below the fit regardless of <see cref="LetterboxTolerance"/> — bars, never a
/// fractional resample.</para>
/// </summary>
public sealed record PresentationPolicy
{
    // Absolute slack applied to the ladder comparisons, on numbers of order 1-10. Float division of
    // window/render sizes lands a hair under a step often enough to matter (1279/1280 * 2 = 1.9984
    // is a fit scale of 1, not of 0.5).
    private const float Epsilon = 1e-4f;

    private readonly float _overscanTolerance = 0.05f;
    private readonly float _letterboxTolerance = 0.25f;

    /// <summary>Whether step 1 (overscan to the clean step above the fit scale) may run. Off by
    /// default: growing the frame past the window trades authored edges for crispness, and that is
    /// a decision a game makes, not one it inherits.</summary>
    public bool AllowOverscan { get; init; }

    /// <summary>
    /// How much extra scale overscan may spend to reach a clean step, as a fraction (0.05 = 5%).
    /// It bounds how much of the frame leaves the window — the tightest axis loses about this
    /// fraction of its pixels — or, when the head sized its render targets with
    /// <see cref="ResolveRenderSize"/>, how much EXTRA WORLD the camera reveals instead.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Negative.</exception>
    public float OverscanTolerance
    {
        get => _overscanTolerance;
        init => _overscanTolerance = value >= 0f
            ? value
            : throw new ArgumentOutOfRangeException(nameof(OverscanTolerance),
                $"Overscan tolerance must be non-negative (got {value}).");
    }

    /// <summary>Whether step 2 (drop to the clean step below the fit scale and pad with bars) may
    /// run. Off by default, so an unconfigured manager presents exactly as it always did.</summary>
    public bool AllowLetterbox { get; init; }

    /// <summary>
    /// How much scale the letterbox step may give up to reach a clean step, as a fraction of the
    /// aspect-fit scale (0.25 = "a quarter smaller, at most"). It bounds the bars: a window just
    /// under 1× of the render resolution would otherwise drop to 1/1.5 and frame a two-thirds-size
    /// image in a wide black border.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Outside [0, 1).</exception>
    public float LetterboxTolerance
    {
        get => _letterboxTolerance;
        init => _letterboxTolerance = value >= 0f && value < 1f
            ? value
            : throw new ArgumentOutOfRangeException(nameof(LetterboxTolerance),
                $"Letterbox tolerance must be in [0, 1) (got {value}).");
    }

    /// <summary>Whether the chain may end in a fractional aspect-fit present. On by default (it is
    /// the historical behaviour); turning it off makes the last resort a clean scale with bars,
    /// however large.</summary>
    public bool AllowStretch { get; init; } = true;

    /// <summary>Which scales count as clean. <see cref="CleanScaleSteps.Half"/> by default — whole
    /// steps alone are so far apart below 1× that the chain almost always falls through to
    /// stretch.</summary>
    public CleanScaleSteps Steps { get; init; } = CleanScaleSteps.Half;

    /// <summary>
    /// The ENGINE default: aspect-fit at whatever scale the window implies, exactly as every
    /// MonoDreams game presented before the policy existed. Byte-identical framing — the only thing
    /// that changed under it is that layers sample through <see cref="SamplerPolicy.Auto"/>, so a
    /// fractional present filters instead of shimmering.
    /// </summary>
    public static readonly PresentationPolicy Stretch = new();

    /// <summary>
    /// The recommended default for a NEW game (what a scaffolded game should declare): overscan
    /// within 5% → letterbox within 25% → stretch. It reaches for a clean scale twice, cheaply, and
    /// still refuses to leave the player with a two-thirds-size picture in a fat border.
    /// </summary>
    public static readonly PresentationPolicy Default = new()
    {
        AllowOverscan = true,
        AllowLetterbox = true,
    };

    /// <summary>Never resample at a fractional ratio: overscan, else a clean scale with bars —
    /// however large the bars get. The pixel-art purist's policy, in half steps.</summary>
    public static readonly PresentationPolicy Crisp = new()
    {
        AllowOverscan = true,
        AllowLetterbox = true,
        LetterboxTolerance = 0.999f,
        AllowStretch = false,
    };

    /// <summary>
    /// Integer scales only, no overscan, no stretch — the largest whole step of the render
    /// resolution that fits, centered, with bars around it. This is the retired
    /// <c>ViewportManager.ScalingMode.PixelPerfect</c> expressed as a policy, and it matches that
    /// mode exactly for every window at least as large as the render resolution in both axes
    /// (where both are <c>floor(fit)</c>).
    ///
    /// <para><b>Below 1× the two DIVERGE, deliberately.</b> The old mode clamped its integer scale
    /// to a floor of 1, so a smaller window got the frame at 1× with its edges cropped off-screen
    /// (1920×1080 in a 1600×900 window: 1920×1080 at (-160, -90)). A no-overscan policy may not
    /// crop, so the ladder keeps descending through the reciprocal steps instead: 1/2, 1/3, …
    /// (the same case resolves to 960×540 centered, with bars). Half the picture with bars beats
    /// silently eating the authored edges — but a game that was relying on the old crop will frame
    /// differently on any window under the render resolution.</para>
    /// </summary>
    public static readonly PresentationPolicy PixelPerfect = new()
    {
        AllowLetterbox = true,
        LetterboxTolerance = 0.999f,
        AllowStretch = false,
        Steps = CleanScaleSteps.Integer,
    };

    /// <summary>
    /// Runs the chain for one window. <paramref name="availableWidth"/>/<paramref name="availableHeight"/>
    /// are the area the game viewport may use (the window minus any chrome inset), and
    /// <paramref name="renderWidth"/>/<paramref name="renderHeight"/> are the render targets' pixel
    /// size. Returns the winning step, its uniform scale, and the destination SIZE — the caller
    /// centers it in the available area, which is what makes an overscan destination stick out on
    /// all four sides and a letterboxed one sit inside bars.
    /// </summary>
    /// <param name="availableWidth">Width of the area the game viewport may use.</param>
    /// <param name="availableHeight">Height of the area the game viewport may use.</param>
    /// <param name="renderWidth">Render-target width in pixels.</param>
    /// <param name="renderHeight">Render-target height in pixels.</param>
    /// <param name="allowOverscan">Lets the caller veto step 1 for this window regardless of
    /// <see cref="AllowOverscan"/> — the editor shell does, because a frame that overflows the game
    /// viewport would paint over the chrome around it.</param>
    public PresentationResolution Resolve(int availableWidth, int availableHeight,
        int renderWidth, int renderHeight, bool allowOverscan = true)
    {
        if (renderWidth <= 0 || renderHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(renderWidth),
                $"Render resolution must be positive (got {renderWidth}x{renderHeight}).");

        float availWidth = Math.Max(1, availableWidth);
        float availHeight = Math.Max(1, availableHeight);
        var fit = Math.Min(availWidth / renderWidth, availHeight / renderHeight);

        // An already-clean fit needs no trade at all: present the aspect-fit rectangle, which is
        // both the letterbox step (bars only from an aspect mismatch) and the zero-overflow case of
        // the overscan step. Reported as Letterbox because nothing left the window.
        if (!IsClean(fit))
        {
            if (AllowOverscan && allowOverscan)
            {
                var up = CleanScaleAtOrAbove(fit);
                if (up / fit - 1f <= OverscanTolerance + Epsilon)
                    return Scaled(PresentationMode.Overscan, up, renderWidth, renderHeight);
            }

            if (AllowLetterbox)
            {
                var down = CleanScaleAtOrBelow(fit);
                if (1f - down / fit <= LetterboxTolerance + Epsilon)
                    return Scaled(PresentationMode.Letterbox, down, renderWidth, renderHeight);
            }

            if (!AllowStretch)
                return Scaled(PresentationMode.Letterbox, CleanScaleAtOrBelow(fit), renderWidth, renderHeight);

            // The historical aspect-fit arithmetic, kept verbatim (including its rounding) so a
            // stretched present is byte-identical to the pre-policy engine.
            var targetAspectRatio = renderWidth / (float)renderHeight;
            var screenAspectRatio = availWidth / availHeight;
            int destWidth, destHeight;
            if (screenAspectRatio > targetAspectRatio) // wider than the frame: letterbox
            {
                destHeight = (int)availHeight;
                destWidth = (int)(destHeight * targetAspectRatio + 0.5f);
            }
            else // taller than the frame (or equal): pillarbox
            {
                destWidth = (int)availWidth;
                destHeight = (int)(destWidth / targetAspectRatio + 0.5f);
            }
            return new PresentationResolution(PresentationMode.Stretch, fit, destWidth, destHeight);
        }

        return Scaled(PresentationMode.Letterbox, fit, renderWidth, renderHeight);
    }

    /// <summary>
    /// The render resolution to allocate so that this window presents at a clean scale with NOTHING
    /// leaving the screen — the "reveal a sliver more world instead of cropping the frame" half of
    /// the overscan step. Returns the design resolution unchanged when overscan is disabled or the
    /// extra view would exceed <see cref="OverscanTolerance"/>, and never returns less than the
    /// design resolution in either axis (the policy adds view, it never takes it away).
    ///
    /// <para>A head calls this BEFORE its screens allocate render targets — the targets, the
    /// per-pass cameras and the back buffer all follow <c>ViewportManager.VirtualWidth/Height</c>,
    /// so the camera's virtual resolution is exactly the dial being turned:</para>
    /// <code>
    /// var size = policy.ResolveRenderSize(design.X, design.Y, window.Width, window.Height);
    /// viewport.SetResolution(size.X, size.Y,
    ///     (int)MathF.Round(size.X / renderScale), (int)MathF.Round(size.Y / renderScale));
    /// </code>
    /// <para>It is a boot-time (or rebuild-time) decision, not a per-frame one: a render resolution
    /// that moved without its render targets moving with it would composite a stale target through
    /// a rectangle computed for the new size.</para>
    /// </summary>
    public Point ResolveRenderSize(int designWidth, int designHeight, int availableWidth, int availableHeight)
    {
        if (designWidth <= 0 || designHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(designWidth),
                $"Design resolution must be positive (got {designWidth}x{designHeight}).");

        if (!AllowOverscan) return new Point(designWidth, designHeight);

        float availWidth = Math.Max(1, availableWidth);
        float availHeight = Math.Max(1, availableHeight);
        var fit = Math.Min(availWidth / designWidth, availHeight / designHeight);
        var clean = CleanScaleAtOrBelow(fit);

        var extraWidth = availWidth / (clean * designWidth);
        var extraHeight = availHeight / (clean * designHeight);
        if (Math.Max(extraWidth, extraHeight) - 1f > OverscanTolerance + Epsilon)
            return new Point(designWidth, designHeight);

        return new Point(
            Math.Max(designWidth, (int)MathF.Round(availWidth / clean)),
            Math.Max(designHeight, (int)MathF.Round(availHeight / clean)));
    }

    /// <summary>The largest clean scale that is at most <paramref name="scale"/> (never below the
    /// ladder's floor of one screen pixel per render pixel row, i.e. it keeps shrinking through
    /// 1/2, 1/3, …).</summary>
    public float CleanScaleAtOrBelow(float scale)
    {
        var m = Steps == CleanScaleSteps.Half ? 2f : 1f;
        if (scale >= 1f) return MathF.Max(1f, MathF.Floor(scale * m + Epsilon) / m);
        return 1f / (MathF.Ceiling(m / scale - Epsilon) / m);
    }

    /// <summary>The smallest clean scale that is at least <paramref name="scale"/>.</summary>
    public float CleanScaleAtOrAbove(float scale)
    {
        var m = Steps == CleanScaleSteps.Half ? 2f : 1f;
        if (scale >= 1f) return MathF.Ceiling(scale * m - Epsilon) / m;
        return 1f / MathF.Max(1f, MathF.Floor(m / scale + Epsilon) / m);
    }

    /// <summary>Whether <paramref name="scale"/> is already on the ladder (within float slack).</summary>
    public bool IsClean(float scale) => MathF.Abs(CleanScaleAtOrBelow(scale) - scale) <= Epsilon * MathF.Max(1f, scale);

    private static PresentationResolution Scaled(PresentationMode mode, float scale, int renderWidth, int renderHeight) =>
        new(mode, scale,
            Math.Max(1, (int)MathF.Round(renderWidth * scale)),
            Math.Max(1, (int)MathF.Round(renderHeight * scale)));
}

/// <summary>Resolves a <see cref="SamplerPolicy"/> against the scale a layer is actually being
/// presented at.</summary>
public static class SamplerPolicyExtensions
{
    // Half a screen pixel over a 4K-wide frame is still well under this, so a rectangle that
    // rounded by a pixel still reads as its integer scale.
    private const float IntegerSlack = 1e-3f;

    /// <summary>
    /// The <see cref="SamplerState"/> this policy wants at <paramref name="scale"/> destination
    /// pixels per source pixel. <see cref="SamplerPolicy.Auto"/> is point at an integer scale of 1×
    /// or more — where nearest-neighbour maps each source pixel onto a whole block and pixel art
    /// stays crisp — and linear everywhere else, including every downscale, where nearest-neighbour
    /// drops rows and columns and makes 1-px text stems crawl as things move.
    /// </summary>
    public static SamplerState Resolve(this SamplerPolicy policy, float scale) => policy switch
    {
        SamplerPolicy.Point => SamplerState.PointClamp,
        SamplerPolicy.Linear => SamplerState.LinearClamp,
        _ => scale >= 1f - IntegerSlack && MathF.Abs(scale - MathF.Round(scale)) <= IntegerSlack
            ? SamplerState.PointClamp
            : SamplerState.LinearClamp,
    };
}
