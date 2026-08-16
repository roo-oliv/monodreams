using Microsoft.Xna.Framework;
using MonoDreams.Debug;
using MonoDreams.Platform;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Debug;
using MonoDreams.System.Draw;
using MonoDreams.UI;
using Xunit;

namespace MonoDreams.Tests.Foundation;

/// <summary>
/// The regression guard for issue #114: <b>state one test leaves behind must not reach the next
/// one</b>. The engine's sockets are process-wide by design (foundation premises "`Logger.LineSink`
/// is a single-owner tap" and "`GatedSystem`'s timing sink keeps the profiler out of foundation";
/// rendering premise "A render pass publishes its destination through a null-by-default socket"),
/// and a test that installs one and dies without restoring it hands the leak to whichever test xUnit
/// runs next — a different victim on every run, because .NET randomises the class ordering.
///
/// <para>These two tests are a deliberate SEQUENCE (hence the alphabetical orderer, and the <c>A_</c>
/// / <c>B_</c> prefixes): the first leaks every process-wide static this assembly knows about and
/// cleans up nothing; the second asserts it sees a pristine process anyway. Without
/// <see cref="ProcessWideStateGuardAttribute"/> the second test fails and names exactly what leaked,
/// which is what the flake looked like from the far end.</para>
/// </summary>
[TestCaseOrderer("MonoDreams.Tests.AlphabeticalTestCaseOrderer", "MonoDreams.Tests")]
public class ProcessWideStateHygieneTests
{
    /// <summary>Proof that the leaking test actually ran before the checking one — otherwise a broken
    /// orderer would let the check pass vacuously.</summary>
    private static bool _leaked;

    /// <summary>Installs junk in every socket and switch, and restores nothing — a test class that
    /// forgot its <c>finally</c>, which is all it takes.</summary>
    [Fact]
    public void A_LeaksEveryProcessWideSocket()
    {
        Logger.LineSink = (_, _) => { };
        GatedSystem.TimingSink = (_, _) => { };
        SystemProfiler.Enabled = true;
        SystemProfiler.ReportInterval = 99f;
        MasterRenderSystem.RenderedTargetSink = (_, _) => { };
        ColliderDebugSystem.Enabled = true;
        SpriteDebugSystem.Enabled = true;
        CullingSystem.DebugEnabled = true;
        LayoutDebugSystem.Enabled = true;
        FinalDrawSystem.ClearColor = Color.HotPink;
        FinalDrawSystem.LetterboxColor = Color.HotPink;
        // The platform singleton and the logger half: an open session at a raised threshold silences
        // every later test's lines. Opened against the discard-everything platform so the leak costs
        // no disk — the replaced singleton is itself part of what must be restored.
        PlatformServices.Current = ProcessWideState.Silent;
        Logger.Shutdown();
        Logger.Initialize("scratch", LogLevel.Error);

        _leaked = true;

        // Assert the leak IS present while the test that caused it is still running: the guard resets
        // after the test, not during it.
        Assert.NotEmpty(ProcessWideState.Dirty());
    }

    /// <summary>The contract: whatever the previous test did, this one starts from the shipped
    /// defaults.</summary>
    [Fact]
    public void B_NextTestStartsFromTheShippedDefaults()
    {
        Assert.True(_leaked,
            "A_LeaksEveryProcessWideSocket must run first: these two are one sequence, so run the " +
            "CLASS (they cannot be filtered apart) and check AlphabeticalTestCaseOrderer still applies.");

        Assert.Empty(ProcessWideState.Dirty());

        // Spelled out for the sockets whose leak is silent rather than loud: a stale tap, a stale
        // profiler and a raised log threshold all change a later test's behaviour without any
        // exception to trace back.
        Assert.Null(Logger.LineSink);
        Assert.Equal(LogLevel.Debug, Logger.MinimumLevel);
        Assert.Null(GatedSystem.TimingSink);
        Assert.Null(MasterRenderSystem.RenderedTargetSink);
        Assert.False(SystemProfiler.Enabled);
        Assert.IsType<DesktopPlatformServices>(PlatformServices.Current);
    }
}
