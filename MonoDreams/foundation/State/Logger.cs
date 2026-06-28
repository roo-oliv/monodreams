using System;
using System.IO;
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
    private static LogLevel _minimumLevel = LogLevel.Debug;
    private static float _gameTime = -1f;
    private static bool _initialized;

    public static void Initialize(string outputDirectory, LogLevel minimumLevel = LogLevel.Debug)
    {
        lock (Lock)
        {
            if (_initialized) return;

            _minimumLevel = minimumLevel;

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
            PlatformServices.Current.WriteLineToConsole(line);
            _writer?.WriteLine(line);
        }
    }
}
