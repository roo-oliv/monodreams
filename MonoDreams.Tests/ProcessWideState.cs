using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Debug;
using MonoDreams.Platform;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Debug;
using MonoDreams.System.Draw;
using MonoDreams.UI;

namespace MonoDreams.Tests;

/// <summary>
/// Every piece of <b>process-wide</b> engine state a test can touch, in one place: the sockets
/// (`Logger.LineSink`, `GatedSystem.TimingSink`, `MasterRenderSystem.RenderedTargetSink`), the
/// static switches (`SystemProfiler`, the four debug-overlay flags, `FinalDrawSystem`'s colours) and
/// the two global singletons (`PlatformServices.Current`, the `Logger` session itself).
///
/// <para>The engine's own contract for each of these is "an owner installs on construction and
/// restores the default on dispose" (foundation premises "`Logger.LineSink` is a single-owner tap",
/// "`GatedSystem`'s timing sink keeps the profiler out of foundation"; rendering premise "A render
/// pass publishes its destination through a null-by-default socket"). A <b>test</b> is an owner like
/// any other, and one that installs a socket and dies without restoring it hands the leak to
/// whichever test xUnit happens to run next — a different one on every run, because .NET randomises
/// the string hashing that decides class order. That is the shape issue #114 arrived in: a suite
/// that fails 32 tests once and is green three runs later, with every named failure passing in
/// isolation.</para>
///
/// <para><see cref="Reset"/> makes that structurally impossible: xUnit calls it after every single
/// test through <see cref="ProcessWideStateGuardAttribute"/>, declared once at assembly scope, so a
/// leak can never outlive the test that caused it. <see cref="Dirty"/> names what is currently
/// installed — the failure message of the hygiene test, and the payload of
/// <c>MONODREAMS_TEST_REPORT_LEAKS=1</c> when hunting the next one.</para>
///
/// <para><b>One entry is not the engine's.</b> <see cref="Reset"/> also empties DefaultEcs's static
/// query-filter memo cache — see <c>ResetEcsQueryFilterCache</c> for the upstream key collision that
/// makes a shared cache able to hand one test's predicate to another test's query. It is listed here
/// for the same reason as the rest: it is process-wide, mutable, and survives a <c>World</c>.</para>
///
/// <para><b>Adding a new static to the engine?</b> Add it here in the same PR. A process-wide mutable
/// that this file does not know about is a flake waiting for the right shuffle.</para>
/// </summary>
public static class ProcessWideState
{
    private static IPlatformServices _defaultPlatform = PlatformServices.Current;
    private static Color _defaultClearColor = FinalDrawSystem.ClearColor;
    private static Color _defaultLetterboxColor = FinalDrawSystem.LetterboxColor;
    private static float _defaultReportInterval = SystemProfiler.ReportInterval;

    /// <summary>
    /// DefaultEcs's own process-wide mutable: the <c>static</c> memo cache in
    /// <c>DefaultEcs.Internal.EntityQueryFilterFactory</c> that maps a query's filter to the
    /// <c>Predicate&lt;ComponentEnum&gt;</c> an <see cref="EntitySet"/> is built from. It is
    /// <c>internal</c>, so reflection is the only handle; the field is resolved once and the value is
    /// read per call because the library assigns the dictionary lazily.
    /// </summary>
    private static readonly FieldInfo? EcsQueryFilterCacheField = typeof(World).Assembly
        .GetType("DefaultEcs.Internal.EntityQueryFilterFactory")
        ?.GetField("_filters", BindingFlags.Static | BindingFlags.NonPublic);

    /// <summary>False once a DefaultEcs upgrade renames or moves the cache — at which point
    /// <see cref="Reset"/> silently stops clearing it. <c>EcsQueryFilterCacheTests</c> owns the
    /// loud failure so the other ~1500 tests do not have to carry the check.</summary>
    public static bool EcsQueryFilterCacheIsReachable => EcsQueryFilterCacheField != null;

    /// <summary>How many predicates the DefaultEcs filter cache currently holds, or -1 when the cache
    /// is unreachable. Only a test asserts on this.</summary>
    public static int EcsQueryFilterCacheCount =>
        EcsQueryFilterCacheField?.GetValue(null) is IDictionary filters ? filters.Count : -1;

    /// <summary>
    /// Captures the shipped defaults BEFORE any test runs. A module initializer is the only hook
    /// early enough: a static constructor on this class would first run whenever the first test
    /// happens to touch it, which may already be after another test replaced
    /// <see cref="PlatformServices.Current"/>.
    /// </summary>
    [ModuleInitializer]
    internal static void CaptureDefaults()
    {
        _defaultPlatform = PlatformServices.Current;
        _defaultClearColor = FinalDrawSystem.ClearColor;
        _defaultLetterboxColor = FinalDrawSystem.LetterboxColor;
        _defaultReportInterval = SystemProfiler.ReportInterval;
    }

    /// <summary>The process-wide state that is NOT at its default right now, named for a failure
    /// message. Empty is the healthy answer, and the answer after every <see cref="Reset"/>.</summary>
    public static IReadOnlyList<string> Dirty()
    {
        var dirty = new List<string>();

        if (Logger.LineSink != null) dirty.Add("Logger.LineSink");
        if (Logger.MinimumLevel != LogLevel.Debug) dirty.Add($"Logger.MinimumLevel ({Logger.MinimumLevel})");
        if (!ReferenceEquals(PlatformServices.Current, _defaultPlatform))
            dirty.Add($"PlatformServices.Current ({PlatformServices.Current.GetType().Name})");
        if (GatedSystem.TimingSink != null) dirty.Add("GatedSystem.TimingSink");
        if (SystemProfiler.Enabled) dirty.Add("SystemProfiler.Enabled");
        if (SystemProfiler.ReportInterval != _defaultReportInterval)
            dirty.Add($"SystemProfiler.ReportInterval ({SystemProfiler.ReportInterval})");
        if (MasterRenderSystem.RenderedTargetSink != null) dirty.Add("MasterRenderSystem.RenderedTargetSink");
        if (ColliderDebugSystem.Enabled) dirty.Add("ColliderDebugSystem.Enabled");
        if (SpriteDebugSystem.Enabled) dirty.Add("SpriteDebugSystem.Enabled");
        if (CullingSystem.DebugEnabled) dirty.Add("CullingSystem.DebugEnabled");
        if (LayoutDebugSystem.Enabled) dirty.Add("LayoutDebugSystem.Enabled");
        if (FinalDrawSystem.ClearColor != _defaultClearColor) dirty.Add("FinalDrawSystem.ClearColor");
        if (FinalDrawSystem.LetterboxColor != _defaultLetterboxColor) dirty.Add("FinalDrawSystem.LetterboxColor");

        return dirty;
    }

    /// <summary>
    /// Returns every process-wide engine static to the value it has in a fresh process. Cheap enough
    /// to run after each of the assembly's ~1500 tests: a handful of field writes plus one
    /// open/close of a throwaway in-memory log session.
    /// </summary>
    public static void Reset()
    {
        Logger.LineSink = null; // first: nothing below should reach a stale tap
        GatedSystem.TimingSink = null;
        SystemProfiler.Enabled = false; // the setter also uninstalls the timing sink
        SystemProfiler.ReportInterval = _defaultReportInterval;
        MasterRenderSystem.RenderedTargetSink = null;
        ColliderDebugSystem.Enabled = false;
        SpriteDebugSystem.Enabled = false;
        CullingSystem.DebugEnabled = false;
        LayoutDebugSystem.Enabled = false;
        FinalDrawSystem.ClearColor = _defaultClearColor;
        FinalDrawSystem.LetterboxColor = _defaultLetterboxColor;

        ResetEcsQueryFilterCache();

        ResetLogger();
        PlatformServices.Current = _defaultPlatform; // last: ResetLogger borrows the socket
    }

    /// <summary>
    /// Empties DefaultEcs's static query-filter memo cache, so a predicate built by one test can never
    /// be handed to a query in another.
    ///
    /// <para><b>Why a cache needs resetting at all.</b> The cache is keyed by a string built as
    /// <c>$"{withFilter} {withoutFilter} {either} {either}"</c>, and <c>ComponentEnum.ToString()</c>
    /// reinterprets the raw <c>uint[]</c> bitset as UTF-16 — so a component whose flag lands on bit 5
    /// or 21 of a word renders as literally <c>' '</c>, the same character as the separator. Two
    /// different rules can therefore flatten to the same key, and the second one silently runs on the
    /// first one's predicate. Measured collision: <c>With&lt;A&gt;().With&lt;B&gt;()</c> and
    /// <c>With&lt;A&gt;().Without&lt;C&gt;()</c> both key to <c>[7,0,32,0,32,32,32]</c> when A is bit 2,
    /// C is bit 21 and B is bit 37 — whichever query is built first wins, and the loser matches
    /// nothing. This is an upstream defect (present in 0.17.2, 0.18.0-beta01 and upstream master); the
    /// real fixes are an impossible separator or a non-string key, both of which live in the library.
    /// </para>
    ///
    /// <para><b>Why that makes it this file's problem.</b> Which pair collides depends on the global
    /// <c>ComponentFlag</c> assignment order, which depends on the order the assembly's classes run
    /// in — the exact "fails under one shuffle, green under the next" signature of issue #114, whose
    /// residual failure the foundation premise already records as living "one layer down, in a
    /// corrupted DefaultEcs <c>EntitySet</c>". Clearing between tests does not fix the library, but it
    /// confines a poisoned key to the single test that built it, which is precisely the guarantee the
    /// rest of this class provides for the engine's own statics.</para>
    ///
    /// <para>Safe as a plain <c>Clear</c>: the dictionary is a pure memo — every entry is
    /// reconstructible from the query that asked for it — and <c>xunit.runner.json</c> disables
    /// collection parallelism, so no query is being built while this runs.</para>
    /// </summary>
    private static void ResetEcsQueryFilterCache()
    {
        if (EcsQueryFilterCacheField?.GetValue(null) is not IDictionary filters) return;
        lock (filters) filters.Clear();
    }

    /// <summary>
    /// Closes whatever log session a test left open and puts <see cref="Logger.MinimumLevel"/> back at
    /// <see cref="LogLevel.Debug"/>. Only a fresh <c>Initialize</c> can move the threshold and
    /// <c>Initialize</c> is a no-op while a session is open, so the reset is the three-step
    /// shutdown/open/shutdown cycle — run against a silent in-memory platform so it touches no disk
    /// and prints nothing.
    /// </summary>
    private static void ResetLogger()
    {
        PlatformServices.Current = SilentPlatformServices.Instance;
        Logger.Shutdown();
        Logger.Initialize("scratch");
        Logger.Shutdown();
    }

    /// <summary>An <see cref="IPlatformServices"/> that discards everything — no disk, no console.
    /// Used to make the logger reset invisible, and available to a test that needs to open a log
    /// session without leaving a directory behind.</summary>
    public static IPlatformServices Silent => SilentPlatformServices.Instance;

    /// <summary>The quietest possible <see cref="IPlatformServices"/>: every sink discards. Used only
    /// to make the logger reset invisible.</summary>
    private sealed class SilentPlatformServices : IPlatformServices
    {
        public static readonly SilentPlatformServices Instance = new();

        public string BaseDirectory => "/scratch/";

        // IPlatformServices is nullable-oblivious (the engine does not enable NRT); `null!` keeps
        // this nullable-enabled test assembly quiet without changing the contract.
        public string GetEnvironmentVariable(string name) => null!;
        public string CombinePath(params string[] paths) => string.Join("/", paths);
        public bool FileExists(string path) => false;
        public string ReadAllText(string path) => throw new FileNotFoundException(path);
        public void WriteAllText(string path, string contents) { }
        public void WriteAllBytes(string path, byte[] bytes) { }
        public string ExportScene(string suggestedFileName, string contents) => suggestedFileName;
        public void CreateDirectory(string path) { }
        public TextWriter OpenLogWriter(string directory, string fileName) => TextWriter.Null;
        public void WriteLineToConsole(string line) { }
        public void RunBackground(Action work) => work();
    }
}
