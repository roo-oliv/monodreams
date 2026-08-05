using System;
using System.IO;
using System.Runtime.CompilerServices;
using MonoDreams.Platform;

namespace MonoDreams.State;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public static class Logger
{
    private static readonly object Lock = new();
    private static TextWriter _writer;
    private static float _gameTime = -1f;
    private static bool _initialized;

    /// <summary>
    /// The live threshold. Read on the hot path WITHOUT taking <see cref="Lock"/> — it changes
    /// exactly once, in <see cref="Initialize"/>, before any system exists to log; paying a monitor
    /// per discarded <c>Logger.Debug</c> would cost more than the message it is refusing to build.
    /// </summary>
    public static LogLevel MinimumLevel { get; private set; } = LogLevel.Debug;

    /// <summary>Whether a line at <paramref name="level"/> would survive the threshold. One static
    /// field read; the interpolated-message handlers below branch on it before they allocate.</summary>
    public static bool IsEnabled(LogLevel level) => level >= MinimumLevel;

    public static void Initialize(string outputDirectory, LogLevel minimumLevel = LogLevel.Debug)
    {
        lock (Lock)
        {
            if (_initialized) return;

            MinimumLevel = minimumLevel;

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"monodreams_{timestamp}.log";
            // Routed through IPlatformServices: the desktop sink is a file StreamWriter
            // under outputDirectory; a web head supplies a Console/in-memory writer.
            _writer = PlatformServices.Current.OpenLogWriter(outputDirectory, fileName);
            _initialized = true;

            Info($"Logger initialized. Writing to {PlatformServices.Current.CombinePath(outputDirectory, fileName)}");
        }
    }

    public static void UpdateGameTime(float totalTime)
    {
        _gameTime = totalTime;
    }

    // TWO overloads per level, and the pair is the whole design.
    //
    // `Logger.Debug($"cell {x},{y} -> {flavor}")` binds to the HANDLER overload: C# prefers an
    // applicable interpolated-string-handler parameter over `string` when the argument is an
    // interpolated string literal. The handler's ctor reports `shouldAppend: false` at a level the
    // threshold discards, and the compiler then SKIPS the holes entirely — no ToString, no boxing,
    // no StringBuilder, no line. That is the fix: before this, every interpolated call site (300+
    // across the engine and its reference games) formatted its message in full and handed the
    // finished string to a method whose first act was to throw it away — and the per-entity ones
    // in level loading, culling and collision paid it every frame, per entity.
    //
    // `Logger.Info(alreadyBuiltString)` — a variable, a concatenation, a method result — has no
    // interpolation to defer, so it binds to the plain `string` overload and behaves exactly as it
    // always did. Both forms exist across the codebase; neither call site changed.

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Debug(ref Message<AtDebug> message) { if (message.Enabled) Write(LogLevel.Debug, message.Consume()); }

    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Info(ref Message<AtInfo> message) { if (message.Enabled) Write(LogLevel.Info, message.Consume()); }

    public static void Warning(string message) => Write(LogLevel.Warning, message);
    public static void Warning(ref Message<AtWarning> message) { if (message.Enabled) Write(LogLevel.Warning, message.Consume()); }

    public static void Error(string message) => Write(LogLevel.Error, message);
    public static void Error(ref Message<AtError> message) { if (message.Enabled) Write(LogLevel.Error, message.Consume()); }

    public static void Shutdown()
    {
        lock (Lock)
        {
            if (!_initialized) return;

            Info("Logger shutting down.");
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
            _initialized = false;
            _gameTime = -1f;
        }
    }

    private static void Write(LogLevel level, string message)
    {
        // FIRST statement, and it must stay first: everything below — the wall clock most of all,
        // DateTime.Now carries a timezone conversion — is per-line cost that a discarded level
        // must never pay. The handler overloads have usually decided this already; the `string`
        // overload has not.
        if (level < MinimumLevel) return;

        var wallClock = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var gameTimeStr = _gameTime < 0 ? "  N/A " : $"{_gameTime,6:F2}";
        var levelStr = level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => " INFO",
            LogLevel.Warning => " WARN",
            LogLevel.Error => "ERROR",
            _ => "?????",
        };

        // The format is a CONTRACT, not a preference: the input-replay/verification workflow,
        // GameTestRunner's log assertions and the tooling greps all parse these lines. Do not
        // reflow it.
        var line = $"[{wallClock}] [GT {gameTimeStr}] [{levelStr}] {message}";

        lock (Lock)
        {
            // Both sinks, deliberately. On WEB the console IS the only sink (OpenLogWriter returns
            // null there), so dropping it to spare the console.log cost would mute the browser build
            // outright; the cost is answered instead by shipping at Warning, where the volume is a
            // handful of lines per session rather than a few hundred per level load.
            PlatformServices.Current.WriteLineToConsole(line);
            _writer?.WriteLine(line);
        }
    }

    /// <summary>
    /// A level carried in the TYPE system, so <see cref="Message{TLevel}"/> can decide whether to
    /// build anything from inside its own constructor — where it has no argument to read a level
    /// from, only its generic parameter. One handler type per level would be the alternative and is
    /// four copies of the same twelve methods.
    /// </summary>
    public interface ILogLevelTag
    {
        static abstract LogLevel Value { get; }
    }

    public readonly struct AtDebug : ILogLevelTag { public static LogLevel Value => LogLevel.Debug; }
    public readonly struct AtInfo : ILogLevelTag { public static LogLevel Value => LogLevel.Info; }
    public readonly struct AtWarning : ILogLevelTag { public static LogLevel Value => LogLevel.Warning; }
    public readonly struct AtError : ILogLevelTag { public static LogLevel Value => LogLevel.Error; }

    /// <summary>
    /// The zero-cost-when-disabled seam. The compiler lowers <c>Logger.Debug($"a {b} c")</c> into
    /// "construct the handler, and if it said yes, append each piece" — so a `false` out of the
    /// constructor means the holes are never evaluated at all. <c>TLevel.Value</c> is a constant per
    /// instantiation, which leaves the disabled path as one static field read and a compare.
    /// </summary>
    [InterpolatedStringHandler]
    public ref struct Message<TLevel> where TLevel : ILogLevelTag
    {
        private DefaultInterpolatedStringHandler _text;

        /// <summary>Whether this level survived the threshold; false means <see cref="_text"/> is
        /// a default struct that must never be consumed.</summary>
        public readonly bool Enabled;

        public Message(int literalLength, int formattedCount, out bool shouldAppend)
        {
            if (TLevel.Value >= MinimumLevel)
            {
                _text = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
                Enabled = shouldAppend = true;
            }
            else
            {
                _text = default;
                Enabled = shouldAppend = false;
            }
        }

        // The append surface the compiler binds interpolation holes to. Delegating to the BCL's own
        // handler is what keeps `{x,6:F2}`-style alignment/format specifiers behaving identically to
        // a plain interpolated string — the call sites are full of them.
        //
        // The `string`/`ReadOnlySpan<char>` parameters below are nullable-oblivious on purpose: this
        // assembly does not enable nullable reference types, so the BCL's `string?` annotations are
        // dropped rather than reproduced (same runtime behaviour, no CS8632).
        public void AppendLiteral(string value) => _text.AppendLiteral(value);
        public void AppendFormatted<T>(T value) => _text.AppendFormatted(value);
        public void AppendFormatted<T>(T value, string format) => _text.AppendFormatted(value, format);
        public void AppendFormatted<T>(T value, int alignment) => _text.AppendFormatted(value, alignment);
        public void AppendFormatted<T>(T value, int alignment, string format) => _text.AppendFormatted(value, alignment, format);
        public void AppendFormatted(string value) => _text.AppendFormatted(value);
        public void AppendFormatted(string value, int alignment = 0, string format = null) => _text.AppendFormatted(value, alignment, format);
        public void AppendFormatted(ReadOnlySpan<char> value) => _text.AppendFormatted(value);
        public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string format = null) => _text.AppendFormatted(value, alignment, format);

        internal string Consume() => _text.ToStringAndClear();
    }
}
