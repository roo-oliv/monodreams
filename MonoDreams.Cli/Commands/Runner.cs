using MonoDreams.Cli.Installer;
using MonoDreams.Cli.Manifest;
using MonoDreams.Cli.Resolver;

namespace MonoDreams.Cli.Commands;

internal static class Runner
{
    public static Task RunInitAsync(string name, string? dir, string? registryPath)
    {
        if (!IsValidProjectName(name))
        {
            Console.Error.WriteLine($"error: '{name}' is not a valid project name (use letters, digits, underscore, and dash; must start with a letter).");
            Environment.ExitCode = 2;
            return Task.CompletedTask;
        }

        var projectDir = Path.GetFullPath(dir ?? name);
        if (Directory.Exists(projectDir) && Directory.EnumerateFileSystemEntries(projectDir).Any())
        {
            Console.Error.WriteLine($"error: '{projectDir}' exists and is not empty.");
            Environment.ExitCode = 2;
            return Task.CompletedTask;
        }

        Registry registry;
        try { registry = Registry.Load(registryPath); }
        catch (Exception ex) { Console.Error.WriteLine($"error: {ex.Message}"); Environment.ExitCode = 2; return Task.CompletedTask; }

        Console.WriteLine($"-> scaffolding {name} at {projectDir}");
        ProjectScaffolder.Scaffold(projectDir, name);

        Console.WriteLine($"-> installing foundation module");
        var installer = new Installer.Installer(registry, projectDir, dryRun: false);
        var foundation = registry.GetModule("foundation");
        installer.Apply(foundation);

        var state = StateFile.LoadOrCreate(projectDir);
        state.Modules.Add("foundation");
        state.Save(projectDir);

        var copiedCount = Directory.GetFiles(Path.Combine(projectDir, "MonoDreams", "foundation"), "*", SearchOption.AllDirectories).Length;
        Console.WriteLine();
        Console.WriteLine($"Created project '{name}'. {copiedCount} engine files copied.");
        if (!string.IsNullOrEmpty(foundation.PostInstallNotes))
        {
            Console.WriteLine();
            Console.WriteLine(foundation.PostInstallNotes);
        }
        Console.WriteLine();
        Console.WriteLine($"Next steps:");
        Console.WriteLine($"  cd {Path.GetRelativePath(Directory.GetCurrentDirectory(), projectDir)}");
        Console.WriteLine($"  monodreams add rendering camera  # or whichever modules you need");
        Console.WriteLine($"  dotnet run");

        return Task.CompletedTask;
    }

    public static Task RunAddAsync(string[] modules, string? presetName, string? projectPath, bool dryRun, string? registryPath)
    {
        var projectDir = Path.GetFullPath(projectPath ?? Directory.GetCurrentDirectory());

        Registry registry;
        try { registry = Registry.Load(registryPath); }
        catch (Exception ex) { Console.Error.WriteLine($"error: {ex.Message}"); Environment.ExitCode = 2; return Task.CompletedTask; }

        var state = StateFile.LoadOrCreate(projectDir);

        var requested = new List<string>(modules);
        if (!string.IsNullOrEmpty(presetName))
        {
            var preset = registry.GetPreset(presetName);
            if (preset is null)
            {
                Console.Error.WriteLine($"error: preset '{presetName}' not found. Run `monodreams list` to see available presets.");
                Environment.ExitCode = 2;
                return Task.CompletedTask;
            }
            requested.AddRange(preset.Modules);
        }

        if (requested.Count == 0)
        {
            Console.Error.WriteLine("error: nothing to add. Pass module names or `--preset <name>`.");
            Environment.ExitCode = 2;
            return Task.CompletedTask;
        }

        List<string> resolved;
        try
        {
            resolved = DependencyResolver.Resolve(registry, requested, state.Modules);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Environment.ExitCode = 2;
            return Task.CompletedTask;
        }

        if (resolved.Count == 0)
        {
            Console.WriteLine("All requested modules (and their dependencies) are already installed. Nothing to do.");
            return Task.CompletedTask;
        }

        Console.WriteLine($"Plan ({(dryRun ? "dry-run, no changes" : "applying")}):");
        Console.WriteLine($"  project: {projectDir}");
        Console.WriteLine($"  already installed: {(state.Modules.Count == 0 ? "<none>" : string.Join(", ", state.Modules))}");
        Console.WriteLine($"  to install: {string.Join(", ", resolved)}");
        Console.WriteLine();

        var installer = new Installer.Installer(registry, projectDir, dryRun);
        var notes = new List<(string Module, string Notes)>();

        try
        {
            foreach (var name in resolved)
            {
                var manifest = registry.GetModule(name);
                installer.Apply(manifest);
                if (!string.IsNullOrWhiteSpace(manifest.PostInstallNotes))
                    notes.Add((name, manifest.PostInstallNotes));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error during install: {ex.Message}");
            Environment.ExitCode = 1;
            return Task.CompletedTask;
        }

        if (!dryRun)
        {
            foreach (var name in resolved) state.Modules.Add(name);
            state.Save(projectDir);
        }

        if (notes.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Post-install notes:");
            Console.WriteLine();
            foreach (var (module, body) in notes)
            {
                Console.WriteLine($"==== {module} ====");
                Console.WriteLine(body);
                Console.WriteLine();
            }
        }

        return Task.CompletedTask;
    }

    public static void RunList(bool verbose, string? registryPath)
    {
        Registry registry;
        try { registry = Registry.Load(registryPath); }
        catch (Exception ex) { Console.Error.WriteLine($"error: {ex.Message}"); Environment.ExitCode = 2; return; }

        Console.WriteLine($"Available modules ({registry.Index.Modules.Count}):");
        var nameWidth = registry.Index.Modules.Max(b => b.Name.Length);
        foreach (var entry in registry.Index.Modules)
        {
            Console.WriteLine($"  {entry.Name.PadRight(nameWidth)}  {entry.Description}");
            if (verbose)
            {
                var manifest = registry.GetModule(entry.Name);
                if (manifest.Dependencies.Count > 0)
                    Console.WriteLine($"  {new string(' ', nameWidth)}  deps: {string.Join(", ", manifest.Dependencies)}");
                if (manifest.NugetDependencies.Count > 0)
                    Console.WriteLine($"  {new string(' ', nameWidth)}  nuget: {string.Join(", ", manifest.NugetDependencies.Select(n => $"{n.Id} {n.Version}"))}");
                if (!string.IsNullOrEmpty(manifest.PremisesRef))
                    Console.WriteLine($"  {new string(' ', nameWidth)}  premises: {manifest.PremisesRef}");
            }
        }

        if (registry.Index.Presets.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Presets ({registry.Index.Presets.Count}, install via `monodreams add --preset <name>`):");
            var presetWidth = registry.Index.Presets.Max(p => p.Name.Length);
            foreach (var preset in registry.Index.Presets)
            {
                Console.WriteLine($"  {preset.Name.PadRight(presetWidth)}  {preset.Description}");
                if (verbose)
                    Console.WriteLine($"  {new string(' ', presetWidth)}  modules: {string.Join(", ", preset.Modules)}");
            }
        }
    }

    private static bool IsValidProjectName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!char.IsLetter(name[0])) return false;
        return name.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');
    }
}
