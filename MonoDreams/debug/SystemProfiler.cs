#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using MonoDreams.State;
using MonoDreams.System;

namespace MonoDreams.Debug;

/// <summary>
/// Per-system frame-time accounting — the answer to "which system is eating the frame", on any
/// platform. <b>Off unless <see cref="Enabled"/> is set</b> (the host flips it from
/// <c>MONODREAMS_PROFILE=1</c>): setting it installs <see cref="Record"/> as
/// <see cref="GatedSystem.TimingSink"/> — foundation defines that socket and never references this
/// module — and clearing it uninstalls the sink again, so the cost when off is one null check per
/// gated system per frame and no profiler is reachable at all.
///
/// <para>Every pipeline entry a screen registers is wrapped in a <c>GatedSystem</c>, so that is
/// where the stopwatch lives — one seam covers both pipelines of every screen, groups included
/// (a group's time is its own line, its children's lines nest under it by name). Call
/// <see cref="Report"/> periodically; it returns a sorted table and starts a fresh window.</para>
///
/// <para><b>Why this and not a real profiler:</b> the platform that matters here is a browser, where
/// attaching a native profiler to wasm tells you about the runtime, not about which system is
/// heavy. Timing at the ECS seam gives the same answer on desktop and on web, and it goes through
/// <c>Logger</c> — which on the web head reaches the browser console.</para>
/// </summary>
public static class SystemProfiler
{
    private static readonly Action<string, long> RecordSink = Record;
    private static bool _enabled;

    /// <summary>Whether timing is collected at all. The host sets this once at boot
    /// (from <c>MONODREAMS_PROFILE=1</c>). Setting it installs/uninstalls <see cref="Record"/>
    /// as <see cref="GatedSystem.TimingSink"/> — the plug into foundation's socket, so foundation
    /// never references this module. Disabling mid-run uninstalls the sink and stops recording.</summary>
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            GatedSystem.TimingSink = value ? RecordSink : null;
        }
    }

    /// <summary>Seconds between reports (the host's report loop reads this).</summary>
    public static float ReportInterval = 2f;

    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
    private static readonly object Lock = new();
    private static long _frames;

    private sealed class Entry
    {
        public long Ticks;
        public long Calls;
    }

    /// <summary>Folds one system's elapsed ticks into the current window.</summary>
    public static void Record(string name, long ticks)
    {
        lock (Lock)
        {
            if (!Entries.TryGetValue(name, out var entry)) Entries[name] = entry = new Entry();
            entry.Ticks += ticks;
            entry.Calls++;
        }
    }

    /// <summary>Counts a frame (the host calls this once per Update), so the report can give
    /// per-frame milliseconds rather than a meaningless total.</summary>
    public static void CountFrame() => _frames++;

    /// <summary>
    /// The window's table — systems by descending cost, in ms PER FRAME — then resets. Returns null
    /// when nothing was recorded (profiling off, or no frames yet).
    /// </summary>
    public static string? Report()
    {
        lock (Lock)
        {
            if (_frames == 0 || Entries.Count == 0) return null;

            var rows = new List<(string Name, double MsPerFrame, double Share)>();
            var total = 0L;
            foreach (var (_, entry) in Entries) total += entry.Ticks;

            foreach (var (name, entry) in Entries)
            {
                var ms = entry.Ticks * 1000.0 / Stopwatch.Frequency / _frames;
                rows.Add((name, ms, total == 0 ? 0 : entry.Ticks * 100.0 / total));
            }
            rows.Sort((a, b) => b.MsPerFrame.CompareTo(a.MsPerFrame));

            var totalMs = total * 1000.0 / Stopwatch.Frequency / _frames;
            var sb = new StringBuilder();
            sb.Append($"[perf] {_frames} frames, {totalMs:0.00}ms/frame in profiled systems:");
            var shown = 0;
            foreach (var row in rows)
            {
                if (row.MsPerFrame < 0.01 && shown >= 12) continue; // the tail is noise
                sb.Append($"\n         {row.Name,-28} {row.MsPerFrame,7:0.000}ms  {row.Share,5:0.0}%");
                shown++;
            }

            Entries.Clear();
            _frames = 0;
            return sb.ToString();
        }
    }

    /// <summary>Convenience for a host loop: reports every <see cref="ReportInterval"/> seconds of
    /// game time. Returns whether a report was logged.</summary>
    public static bool ReportPeriodically(GameState state, ref float timer)
    {
        if (!Enabled) return false;
        timer += state.Time;
        if (timer < ReportInterval) return false;
        timer = 0f;
        var report = Report();
        if (report != null) Logger.Info(report);
        return report != null;
    }
}
