using System;
using System.IO;

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
    private static StreamWriter _writer;
    private static LogLevel _minimumLevel = LogLevel.Debug;
    private static float _gameTime = -1f;
    private static bool _initialized;

    public static void Initialize(string outputDirectory, LogLevel minimumLevel = LogLevel.Debug)
    {
        lock (Lock)
        {
            if (_initialized) return;

            _minimumLevel = minimumLevel;

            Directory.CreateDirectory(outputDirectory);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var logPath = Path.Combine(outputDirectory, $"monodreams_{timestamp}.log");
            _writer = new StreamWriter(logPath, append: false) { AutoFlush = true };
            _initialized = true;

            Info($"Logger initialized. Writing to {logPath}");
        }
    }

    public static void UpdateGameTime(float totalTime)
    {
        _gameTime = totalTime;
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warning(string message) => Write(LogLevel.Warning, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

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
        if (level < _minimumLevel) return;

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

        var line = $"[{wallClock}] [GT {gameTimeStr}] [{levelStr}] {message}";

        lock (Lock)
        {
            Console.WriteLine(line);
            _writer?.WriteLine(line);
        }
    }
}
