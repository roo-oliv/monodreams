using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Debug;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.State;
using MonoDreams.System;
using Xunit;

// Folder/namespace note: this lives under `Profiling/`, not `Debug/`, because
// `.gitignore` ignores `/MonoDreams.Tests/[Dd]ebug/` as a build-output directory — a test file
// there would compile locally and never be committed.
namespace MonoDreams.Tests.Profiling;

/// <summary>
/// Protects the optional per-system frame-time profiler: it plugs itself into foundation's
/// <see cref="GatedSystem.TimingSink"/> socket (foundation never references it back — see
/// <c>GatedSystemTimingSinkTests.Foundation_SourcesNeverReferenceDebugModule</c>), accumulates a
/// window of ticks per profile name, and renders it as a table whose shape is a <b>grep contract</b>
/// (the report is read out of a log file, on desktop and in a browser console alike).
///
/// <para>Every gate a screen registers through <see cref="EditorPipelineRegistrar"/> carries the
/// entry's full hierarchical name, so the report's rows ARE the pipeline tree — that end-to-end path
/// is asserted here rather than only at the naming seam.</para>
///
/// <para><b>Static state.</b> <see cref="SystemProfiler"/>'s window and the timing socket are
/// process-wide, so this class shares the <c>SystemProfilerStatics</c> collection with
/// <c>GatedSystemTimingSinkTests</c> and fully resets both in its constructor and
/// <see cref="Dispose"/>. Profile names are test-local and assertions on the report are
/// containment-based, so a stray recording could never make a test here flake.</para>
/// </summary>
[Collection("SystemProfilerStatics")]
public class SystemProfilerTests : IDisposable
{
    /// <summary>The header's fixed shape: <c>[perf] {frames} frames, {ms}ms/frame in profiled
    /// systems:</c>. The decimal separator is matched as <c>[.,]</c> so the contract test is about
    /// the LAYOUT and not about the machine's locale.</summary>
    private static readonly Regex HeaderContract =
        new(@"^\[perf\] \d+ frames, \d+[.,]\d\dms/frame in profiled systems:$", RegexOptions.Compiled);

    /// <summary>A row's fixed shape: indented name, then ms/frame to three decimals suffixed
    /// <c>ms</c>, then the share to one decimal suffixed <c>%</c>. Anchored on those two suffixes so
    /// the fixed-width padding may change without breaking the test.</summary>
    private static readonly Regex RowContract =
        new(@"^\s+\S.*\s\d+[.,]\d{3}ms\s+\d+[.,]\d%$", RegexOptions.Compiled);

    /// <summary>The name the drain uses to force a report (and therefore a window reset); never
    /// asserted on, and gone by the time any test body runs.</summary>
    private const string DrainMarker = "__drain__";

    /// <summary>
    /// A child system that burns a measurable slice of wall clock, so the recorded tick span (and
    /// therefore the report's ms/frame and share columns) is never zero — a zero total would make
    /// the share column degenerate and the format assertions meaningless.
    /// </summary>
    private sealed class BusySystem : ISystem<GameState>
    {
        private readonly long _ticks;
        public int UpdateCount { get; private set; }
        public bool IsEnabled { get; set; } = true;

        public BusySystem(double milliseconds = 0.2)
            => _ticks = Math.Max(1L, (long)(Stopwatch.Frequency * milliseconds / 1000.0));

        public void Update(GameState state)
        {
            if (!IsEnabled) return;
            UpdateCount++;
            var start = Stopwatch.GetTimestamp();
            while (Stopwatch.GetTimestamp() - start < _ticks) { }
        }

        public void Dispose() { }
    }

    private static GameState NewState(RunMode mode) => new(new GameTime()) { RunMode = mode };

    private static GatedSystem NamedGate(string profileName, ISystem<GameState> child,
        EditTimeBehavior policy = EditTimeBehavior.RunNormally)
        => new(child, policy) { ProfileName = profileName };

    public SystemProfilerTests() => ResetStatics();

    public void Dispose() => ResetStatics();

    private static void ResetStatics()
    {
        SystemProfiler.Enabled = false;  // the setter also uninstalls the sink
        GatedSystem.TimingSink = null;   // belt and braces: the socket is what drives recording
        SystemProfiler.ReportInterval = 2f;
        DrainWindow();
    }

    /// <summary>
    /// Empties the window <b>deterministically</b>. <see cref="SystemProfiler.Report"/> only resets
    /// when it actually renders a table (it early-returns on an empty window), so the drain first
    /// guarantees both preconditions — one entry and one frame — and then discards the table.
    /// </summary>
    private static void DrainWindow()
    {
        SystemProfiler.Record(DrainMarker, 1);
        SystemProfiler.CountFrame();
        SystemProfiler.Report();
    }

    /// <summary>The profile names of a report's rows, in the order the report lists them (the
    /// header is skipped). A row's name is its first whitespace-delimited token.</summary>
    private static List<string> RowNames(string report) =>
        report.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(line => line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
            .ToList();

    // ---- Enabled is the install/uninstall switch for foundation's socket ----

    [Fact]
    public void Enabled_InstallsAndUninstallsTimingSink()
    {
        Assert.Null(GatedSystem.TimingSink); // the constructor's reset: nothing plugged in

        SystemProfiler.Enabled = true;
        Assert.True(SystemProfiler.Enabled);
        Assert.NotNull(GatedSystem.TimingSink);

        SystemProfiler.Enabled = false;
        Assert.False(SystemProfiler.Enabled);
        Assert.Null(GatedSystem.TimingSink);
    }

    // ---- The rows are named by the registrar's hierarchical entry names ----

    [Fact]
    public void Report_NamesRowsPerRegistrarRegistration()
    {
        const string leaf = "rowsreg_alpha";
        const string group = "rowsreg_grp";

        var registrar = new EditorPipelineRegistrar();
        registrar.Add(leaf, new BusySystem(), EditTimeBehavior.RunNormally);
        registrar.AddGroup(group, EditTimeBehavior.RunNormally, g => g.Add("child", new BusySystem()));
        var pipeline = registrar.Build();

        SystemProfiler.Enabled = true;
        var state = NewState(RunMode.Play);
        for (var i = 0; i < 3; i++)
        {
            pipeline.Update(state);
            SystemProfiler.CountFrame();
        }

        var report = SystemProfiler.Report();
        Assert.NotNull(report);
        Assert.StartsWith("[perf] ", report);

        // Exact row names (not substrings): the group's OWN gate is a row, and its child appears
        // under the full hierarchical name — so the table reads as the pipeline tree.
        var names = RowNames(report!);
        Assert.Contains(leaf, names);
        Assert.Contains(group, names);
        Assert.Contains($"{group}.child", names);
    }

    // ---- The table's shape is a parsing contract ----

    [Fact]
    public void Report_FormatMatchesGrepContract()
    {
        var gate = NamedGate("fmt_gate", new BusySystem());

        SystemProfiler.Enabled = true;
        var state = NewState(RunMode.Play);
        for (var i = 0; i < 3; i++)
        {
            gate.Update(state);
            SystemProfiler.CountFrame();
        }

        var report = SystemProfiler.Report();
        Assert.NotNull(report);

        var lines = report!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Matches(HeaderContract, lines[0]);

        var rows = lines.Skip(1).ToList();
        Assert.NotEmpty(rows);
        foreach (var row in rows) Assert.Matches(RowContract, row);
    }

    // ---- A report needs BOTH a counted frame and a recording ----

    [Fact]
    public void Report_ReturnsNull_WithNoFramesOrNoRecordings()
    {
        DrainWindow(); // 0 frames, 0 entries

        Assert.Null(SystemProfiler.Report()); // neither frames nor entries

        SystemProfiler.Record("nofr_entry_only", 100);
        Assert.Null(SystemProfiler.Report()); // an entry, but no frame was ever counted

        DrainWindow();               // that entry is still pending (the null path does not reset)
        SystemProfiler.CountFrame(); // a frame, but nothing recorded during it
        Assert.Null(SystemProfiler.Report());

        DrainWindow();
    }

    // ---- Turning profiling off mid-run stops recording immediately ----

    [Fact]
    public void Disabled_MidRun_StopsRecording()
    {
        const string whileOn = "mid_on";
        const string whileOff = "mid_off";

        var onGate = NamedGate(whileOn, new BusySystem());
        var offChild = new BusySystem();
        var offGate = NamedGate(whileOff, offChild);
        var state = NewState(RunMode.Play);

        SystemProfiler.Enabled = true;
        onGate.Update(state);
        SystemProfiler.CountFrame();

        SystemProfiler.Enabled = false; // uninstalls the sink mid-run
        for (var i = 0; i < 3; i++) offGate.Update(state);

        var report = SystemProfiler.Report();
        Assert.NotNull(report);
        var names = RowNames(report!);
        Assert.Contains(whileOn, names);     // the enabled phase's recording is there...
        Assert.DoesNotContain(whileOff, names); // ...and nothing from the disabled phase is
        Assert.Equal(3, offChild.UpdateCount);  // though the gate kept forwarding to its child

        // The window (reset by that Report) stays empty while profiling is off: a counted frame
        // alone can never produce a table.
        for (var i = 0; i < 3; i++) offGate.Update(state);
        SystemProfiler.CountFrame();
        Assert.Null(SystemProfiler.Report());
    }

    // ---- Reporting starts a fresh window ----

    [Fact]
    public void Report_ResetsWindow()
    {
        var gate = NamedGate("reset_gate", new BusySystem());

        SystemProfiler.Enabled = true;
        gate.Update(NewState(RunMode.Play));
        SystemProfiler.CountFrame();

        Assert.NotNull(SystemProfiler.Report());
        Assert.Null(SystemProfiler.Report()); // the window was reset, not accumulated
    }

    // ---- The host-loop convenience: interval in GAME time, and only while enabled ----

    [Fact]
    public void ReportPeriodically_HonoursInterval()
    {
        // A state whose Time (current ElapsedGameTime) is exactly one second per call.
        var oneSecond = new GameState(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        var timer = 0f;

        // Off: it reports nothing and does not even accumulate the timer.
        SystemProfiler.Enabled = false;
        Assert.False(SystemProfiler.ReportPeriodically(oneSecond, ref timer));
        Assert.Equal(0f, timer);

        SystemProfiler.Enabled = true;
        SystemProfiler.ReportInterval = 2f;

        var gate = NamedGate("periodic_gate", new BusySystem());
        gate.Update(oneSecond);
        SystemProfiler.CountFrame();

        Assert.False(SystemProfiler.ReportPeriodically(oneSecond, ref timer)); // 1s of 2s
        Assert.Equal(1f, timer);
        Assert.True(SystemProfiler.ReportPeriodically(oneSecond, ref timer));  // 2s: a table was logged
        Assert.Equal(0f, timer);                                               // and the window restarts

        // The interval elapsing again with an empty window logs nothing (Report returned null).
        Assert.False(SystemProfiler.ReportPeriodically(oneSecond, ref timer));
        Assert.False(SystemProfiler.ReportPeriodically(oneSecond, ref timer));
    }
}
