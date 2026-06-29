using System;
using System.IO;
using System.Threading.Tasks;

namespace MonoDreams.Platform;

/// <summary>
/// The default <see cref="IPlatformServices"/>: every member maps to the real
/// filesystem / process environment, reproducing MonoDreams' historical desktop
/// behaviour. This is the implementation <see cref="PlatformServices.Current"/>
/// starts with, so a desktop head needs no setup. A web head replaces it at startup.
/// </summary>
public sealed class DesktopPlatformServices : IPlatformServices
{
    public string BaseDirectory => AppDomain.CurrentDomain.BaseDirectory;

    public string GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    public string CombinePath(params string[] paths) => Path.Combine(paths);

    public bool FileExists(string path) => File.Exists(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);

    public void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);

    public string ExportScene(string suggestedFileName, string contents)
    {
        // Desktop: write the scene next to the executable so the user can find it on disk.
        var path = Path.Combine(BaseDirectory, suggestedFileName);
        File.WriteAllText(path, contents);
        return path;
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public TextWriter OpenLogWriter(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var logPath = Path.Combine(directory, fileName);
        return new StreamWriter(logPath, append: false) { AutoFlush = true };
    }

    public void WriteLineToConsole(string line) => Console.WriteLine(line);

    public void RunBackground(Action work) => Task.Run(work);
}
