namespace MonoDreams.Cli.Tests;

internal static class CliTestSupport
{
    /// <summary>
    /// Walks up from the test assembly to the repo root (the directory containing
    /// <c>MonoDreams/module.schema.json</c>) — the registry path the CLI reads engine manifests + source from.
    /// </summary>
    public static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "MonoDreams", "module.schema.json")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root (directory containing MonoDreams/module.schema.json).");
    }

    public static string NewTempDir(string tag)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"md-{tag}-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
