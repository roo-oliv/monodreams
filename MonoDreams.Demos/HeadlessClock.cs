using System;
using Microsoft.Xna.Framework;

namespace MonoDreams.Demos;

/// <summary>
/// The deterministic fixed-step clock a <c>--headless</c> Demos run advances instead of the host's
/// wallclock-derived <see cref="GameTime"/>.
///
/// <para><b>Why the host clock cannot be used.</b> Headless sets
/// <c>IsFixedTimeStep = false</c> and <c>SynchronizeWithVerticalRetrace = false</c> on purpose (the
/// max-speed contract): MonoGame then hands <c>Update</c> a <see cref="GameTime"/> whose
/// <c>ElapsedGameTime</c> is the measured wall time of the previous frame. Every value derived from
/// it — <c>GameState.Time</c>, <c>GameState.TotalTime</c>, the <c>[GT …]</c> stamp on every log line,
/// the <c>gt=</c> field of a screenshot name, and any simulation that integrates over dt — then
/// differs between two runs of the same scene on the same machine. That makes the observe-and-
/// self-verify path unable to answer "did my change alter the output?", because the output was never
/// the same twice to begin with.</para>
///
/// <para><b>What this guarantees.</b> Every headless frame advances by exactly one
/// <see cref="Step"/>, and <c>TotalGameTime</c> is recomputed from the frame COUNT
/// (<c>Step.Ticks * frames</c>, integer arithmetic) rather than accumulated, so it carries no
/// rounding drift and depends on nothing but how many frames have run. Two runs of the same demo for
/// the same number of frames therefore see an identical time series.</para>
///
/// <para><b>Scope.</b> Headless only, and it lives in the Demos host rather than in
/// <c>foundation</c>: <c>GameState</c> merely consumes whatever <see cref="GameTime"/> it is handed,
/// so injecting a synthetic one at the single host→<c>ScreenController</c> seam covers the engine
/// without touching it. The windowed path never constructs this type and keeps MonoGame's own
/// fixed-step clock byte for byte.</para>
/// </summary>
internal sealed class HeadlessClock
{
    private readonly TimeSpan _step;

    /// <param name="step">The constant per-frame delta. The host passes <c>Game.TargetElapsedTime</c>
    /// — the very step the WINDOWED path runs at (1/60 s) — so headless simulates the same rate a
    /// player sees, just as fast as the machine can produce it.</param>
    public HeadlessClock(TimeSpan step)
    {
        if (step <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(step), step, "The headless step must be positive.");

        _step = step;
        // Frame 0 is the pre-run state: no frame has been advanced, so total and elapsed are both
        // zero. Nothing reads this (Draw always follows an Update within one MonoGame tick); it
        // exists so Current is never null.
        Current = new GameTime(TimeSpan.Zero, TimeSpan.Zero);
    }

    /// <summary>The constant per-frame delta every headless frame advances by.</summary>
    public TimeSpan Step => _step;

    /// <summary>How many frames <see cref="Advance"/> has produced.</summary>
    public long Frames { get; private set; }

    /// <summary>
    /// The <see cref="GameTime"/> of the frame currently being processed — what <c>Draw</c> reads so
    /// it sees the same instant as the <c>Update</c> it follows, instead of advancing the clock twice
    /// per frame.
    /// </summary>
    public GameTime Current { get; private set; }

    /// <summary>
    /// Advances one frame and returns the new <see cref="Current"/>. Called exactly once per
    /// <c>Update</c>.
    /// </summary>
    public GameTime Advance()
    {
        Frames++;
        // Recomputed from the frame count, never `total += step`: integer tick multiplication is
        // exact, so frame N always reports the same instant no matter how the run got there.
        Current = new GameTime(TimeSpan.FromTicks(_step.Ticks * Frames), _step);
        return Current;
    }
}
