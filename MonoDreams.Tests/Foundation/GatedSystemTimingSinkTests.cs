using System;
using System.Collections.Generic;
using System.IO;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.State;
using MonoDreams.System;
using Xunit;

namespace MonoDreams.Tests.Foundation;

/// <summary>
/// Protects the profiling <b>socket</b> <c>foundation</c> owns: <see cref="GatedSystem.TimingSink"/>
/// plus <see cref="GatedSystem.ProfileName"/>. Every pipeline entry passes through a gate, so this is
/// the one seam that can time a whole screen's pipelines — and the dependency direction is
/// deliberate: foundation defines the socket and NEVER references the profiler that plugs into it
/// (the optional <c>debug</c> module installs itself from the outside). The last test in this file
/// pins exactly that with a source scan.
///
/// Pure logic — no rendering or world needed; the fake child system is the same counting
/// <see cref="ISystem{GameState}"/> pattern <c>RunStateGatingTest</c> uses.
///
/// <para><b>Static state.</b> <see cref="GatedSystem.TimingSink"/> is process-wide, so this class
/// shares the <c>SystemProfilerStatics</c> collection with <c>SystemProfilerTests</c> (which drives
/// the same socket from the debug side) and resets the socket in both its constructor and
/// <see cref="Dispose"/>. It deliberately does NOT touch <c>SystemProfiler</c> itself: clearing the
/// sink is sufficient to isolate these tests, and a foundation test that reached into the debug
/// module would sit oddly next to the dependency-direction assertion below.</para>
/// </summary>
[Collection("SystemProfilerStatics")]
public class GatedSystemTimingSinkTests : IDisposable
{
    /// <summary>A minimal child system that records its run count and respects IsEnabled.</summary>
    private sealed class CountingSystem : ISystem<GameState>
    {
        public int UpdateCount { get; private set; }
        public bool IsEnabled { get; set; } = true;

        public void Update(GameState state)
        {
            // Honour the ISystem contract: a disabled system does no work.
            if (!IsEnabled) return;
            UpdateCount++;
        }

        public void Dispose() { }
    }

    private static GameState NewState(RunMode mode) => new(new GameTime()) { RunMode = mode };

    public GatedSystemTimingSinkTests() => GatedSystem.TimingSink = null;

    public void Dispose() => GatedSystem.TimingSink = null;

    // ---- The socket is empty by default, and an unplugged socket changes nothing ----

    [Fact]
    public void TimingSink_DefaultsToNull_AndGateForwardsWithoutIt()
    {
        // Nothing is installed: profiling costs one null check per gated Update and no profiler is
        // reachable in the object graph at all.
        Assert.Null(GatedSystem.TimingSink);

        // A NAMED gate with no sink behaves exactly like an unnamed one — it just forwards.
        var child = new CountingSystem();
        var gate = new GatedSystem(child, EditTimeBehavior.RunNormally) { ProfileName = "defaults_gate" };

        gate.Update(NewState(RunMode.Play));
        gate.Update(NewState(RunMode.Edit));

        Assert.Equal(2, child.UpdateCount);
    }

    // ---- Sink + name: the gate times the child and reports once per admitted Update ----

    [Fact]
    public void Update_WithSinkAndProfileName_RecordsElapsedTicks()
    {
        const string name = "records_gate";
        var recorded = new List<(string Name, long Ticks)>();
        GatedSystem.TimingSink = (n, t) => recorded.Add((n, t));

        var child = new CountingSystem();
        var gate = new GatedSystem(child, EditTimeBehavior.RunNormally) { ProfileName = name };

        gate.Update(NewState(RunMode.Play));

        Assert.Equal(1, child.UpdateCount);
        Assert.Single(recorded); // exactly one recording per admitted Update
        Assert.Equal(name, recorded[0].Name);
        Assert.True(recorded[0].Ticks >= 0,
            $"expected a non-negative Stopwatch tick span, got {recorded[0].Ticks}");
    }

    // ---- An unnamed gate is never timed, even with a sink installed ----

    [Fact]
    public void Update_WithSinkButNullProfileName_DoesNotRecord()
    {
        var recorded = new List<(string Name, long Ticks)>();
        GatedSystem.TimingSink = (n, t) => recorded.Add((n, t));

        var child = new CountingSystem();
        var gate = new GatedSystem(child, EditTimeBehavior.RunNormally); // ProfileName left null

        gate.Update(NewState(RunMode.Play));

        Assert.Equal(1, child.UpdateCount); // the child still runs...
        Assert.Empty(recorded);             // ...but an unnamed gate contributes no row
    }

    // ---- A frame the gate or the policy skipped is not a timed frame ----

    [Fact]
    public void Update_SkippedByPolicyOrDisabled_DoesNotRecord()
    {
        var recorded = new List<(string Name, long Ticks)>();
        GatedSystem.TimingSink = (n, t) => recorded.Add((n, t));

        // (a) The gate's own master switch is off: the child never runs, so there is nothing to time.
        var disabledChild = new CountingSystem();
        var disabledGate = new GatedSystem(disabledChild, EditTimeBehavior.RunNormally)
        {
            ProfileName = "skipped_disabled",
            IsEnabled = false,
        };
        disabledGate.Update(NewState(RunMode.Play));
        Assert.Equal(0, disabledChild.UpdateCount);
        Assert.Empty(recorded);

        // (b) The policy refuses the frame (Freeze in Edit) — likewise no run, no row.
        var frozenChild = new CountingSystem();
        var frozenGate = new GatedSystem(frozenChild, EditTimeBehavior.Freeze) { ProfileName = "skipped_frozen" };
        frozenGate.Update(NewState(RunMode.Edit));
        Assert.Equal(0, frozenChild.UpdateCount);
        Assert.Empty(recorded);

        // Sanity: the same frozen gate DOES record once the mode admits it, so the assertions above
        // are about the skip and not about a sink that was never wired.
        frozenGate.Update(NewState(RunMode.Play));
        Assert.Equal(1, frozenChild.UpdateCount);
        Assert.Single(recorded);
    }

    // ---- Uninstalling the sink stops recording without touching the pipeline ----

    [Fact]
    public void Update_SinkRemoved_StopsRecording()
    {
        var recorded = new List<(string Name, long Ticks)>();
        GatedSystem.TimingSink = (n, t) => recorded.Add((n, t));

        var child = new CountingSystem();
        var gate = new GatedSystem(child, EditTimeBehavior.RunNormally) { ProfileName = "removed_gate" };

        gate.Update(NewState(RunMode.Play));
        Assert.Single(recorded);
        Assert.Equal(1, child.UpdateCount);

        GatedSystem.TimingSink = null; // the profiler was turned off mid-run
        gate.Update(NewState(RunMode.Play));
        gate.Update(NewState(RunMode.Play));

        Assert.Single(recorded);            // no further rows
        Assert.Equal(3, child.UpdateCount); // ...and the pipeline is otherwise unchanged
    }

    // ---- Dependency direction: foundation owns the socket, never the plug ----

    /// <summary>
    /// Source-scan architecture test (the <c>EditorThemeLintTests</c> / ship-lint pattern): no
    /// <c>.cs</c> file under <c>MonoDreams/foundation/</c> may name the debug module's namespace
    /// (<c>MonoDreams.Debug</c>) or its profiler (<c>SystemProfiler</c>). This is what makes the
    /// profiler genuinely optional — with nothing installed, no profiling code is reachable from a
    /// build that only composes foundation.
    ///
    /// <para>Line comments (<c>//</c>, which covers <c>///</c> doc comments) are stripped before
    /// scanning, exactly as the palette lint does: the invariant being pinned is a <b>code</b>
    /// dependency, and prose that explains the socket by naming its intended plug is not one.</para>
    /// </summary>
    [Fact]
    public void Foundation_SourcesNeverReferenceDebugModule()
    {
        var foundationRoot = Path.Combine(RepoRoot(), "MonoDreams", "foundation");
        Assert.True(Directory.Exists(foundationRoot), $"expected the foundation module at {foundationRoot}");

        var sources = Directory.GetFiles(foundationRoot, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(sources); // the module has source, so a green result is not vacuous

        var offenders = new List<string>();
        foreach (var file in sources)
        {
            var lineNumber = 0;
            foreach (var raw in File.ReadLines(file))
            {
                lineNumber++;
                var code = StripLineComment(raw);
                if (code.Length == 0) continue;

                if (code.Contains("MonoDreams.Debug", StringComparison.Ordinal) ||
                    code.Contains("SystemProfiler", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{lineNumber}: {code.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "foundation must own the profiling socket (GatedSystem.TimingSink) and never reference the " +
            "debug module that plugs into it. Offenders:\n" + string.Join("\n", offenders));
    }

    /// <summary>Removes a line comment (<c>//</c> … EOL, which also covers <c>///</c> doc comments),
    /// returning the code portion of the line.</summary>
    private static string StripLineComment(string line)
    {
        var idx = line.IndexOf("//", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }

    /// <summary>Walks up from the test base dir to the repo root — the directory holding both
    /// <c>monodreams.sln</c> and the <c>MonoDreams</c> module tree — mirroring the walk-up idiom in
    /// <c>EditorThemeLintTests.RepoRoot</c> / <c>GameTestRunner.FindRepoRoot</c>.</summary>
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null &&
               !(File.Exists(Path.Combine(dir, "monodreams.sln")) &&
                 Directory.Exists(Path.Combine(dir, "MonoDreams"))))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        Assert.NotNull(dir);
        return dir!;
    }
}
