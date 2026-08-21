using System.Reflection;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

namespace MonoDreams.Benchmarks;

/// <summary>
/// Entry point for the DefaultEcs-vs-Arch baseline (issue #119, wave 0).
/// <para>
/// Run every benchmark:
/// <code>dotnet run --project MonoDreams.Benchmarks -c Release -- --filter '*'</code>
/// One family:
/// <code>dotnet run --project MonoDreams.Benchmarks -c Release -- --filter '*Iteration*'</code>
/// </para>
/// Reports land under <c>MonoDreams.Benchmarks/bin/Release/net8.0/artifacts/results/</c> (inside
/// <c>bin/</c>, so they are git-ignored); the committed summary is <c>RESULTS.md</c>.
/// </summary>
public static class Program
{
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
}

/// <summary>Settings every benchmark family shares.</summary>
internal static class BenchmarkConfigDefaults
{
    /// <summary>
    /// Keep BenchmarkDotNet's own output inside the project's <c>bin/</c>, which the repo's
    /// <c>.gitignore</c> already covers — the default (<c>./BenchmarkDotNet.Artifacts</c> next to
    /// the working directory) is not ignored and would show up as untracked noise at the repo root.
    /// </summary>
    internal static string ArtifactsPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "artifacts");

    internal static void ApplyShared(ManualConfig config)
    {
        // Loggers, columns and the default exporters — without this a hand-built ManualConfig
        // reports nothing.
        config.Add(DefaultConfig.Instance);
        // Allocation-per-op is half the answer for the managed (DrawComponent-shaped) case.
        config.AddDiagnoser(MemoryDiagnoser.Default);
        // GitHub-flavoured markdown is what gets pasted into RESULTS.md and the PR body.
        config.AddExporter(MarkdownExporter.GitHub);
        config.WithArtifactsPath(ArtifactsPath);
    }
}

/// <summary>
/// For benchmarks whose operation leaves the world exactly as it found it (iteration; add-then-remove
/// churn). BenchmarkDotNet is free to auto-tune the invocation count per iteration, which is what
/// gives a 10k pass — a few microseconds of work — a meaningful signal-to-noise ratio.
/// </summary>
public sealed class SelfRestoringConfig : ManualConfig
{
    public SelfRestoringConfig()
    {
        AddJob(Job.Default
            .WithWarmupCount(3)
            .WithIterationCount(7)
            .WithId("self-restoring"));
        BenchmarkConfigDefaults.ApplyShared(this);
    }
}

/// <summary>
/// For the structural-churn family, whose slowest case is three orders of magnitude slower than its
/// fastest: a full add/remove sweep of a zero-sized tag over 1M entities takes ~2.4 minutes under
/// DefaultEcs 0.18.0-beta01 (see <see cref="BenchTagByte"/>), against ~50 ms for the same sweep
/// under Arch.
/// <para>
/// Auto-tuning would spend an hour on that single cell, so the invocation count is pinned to 1 and
/// the iteration count cut to 3 (one warmup): a deliberately reduced budget, which is affordable
/// precisely because the differences this family reports are orders of magnitude, not percentages.
/// Every method in the family runs under these identical settings, so the side-by-side stays fair.
/// </para>
/// </summary>
public sealed class HeavyChurnConfig : ManualConfig
{
    public HeavyChurnConfig()
    {
        AddJob(Job.Default
            .WithWarmupCount(1)
            .WithIterationCount(3)
            .WithInvocationCount(1)
            .WithUnrollFactor(1)
            .WithId("heavy-churn"));
        BenchmarkConfigDefaults.ApplyShared(this);
    }
}

/// <summary>
/// For benchmarks whose operation is NOT self-restoring: entity creation ends with a world holding
/// up to a million entities, which has to be torn down before the next invocation.
/// <para>
/// The invocation count is pinned to 1 (unroll factor 1) so BenchmarkDotNet's per-iteration
/// cleanup hook runs after every single measured operation. Auto-tuning would batch several
/// creations of a 1M-entity world into one iteration and hold every one of them alive at the same
/// time — hundreds of megabytes for the managed case, and a GC pause landing inside the
/// measurement. The trade is resolution at 10k, bought back with a longer iteration count.
/// </para>
/// </summary>
public sealed class PerInvocationConfig : ManualConfig
{
    public PerInvocationConfig()
    {
        AddJob(Job.Default
            .WithWarmupCount(3)
            .WithIterationCount(10)
            .WithInvocationCount(1)
            .WithUnrollFactor(1)
            .WithId("per-invocation"));
        BenchmarkConfigDefaults.ApplyShared(this);
    }
}
