using MonoDreams.Cli.Installer;
using MonoDreams.Cli.Manifest;
using MonoDreams.Cli.Resolver;

namespace MonoDreams.Cli.Commands;

internal static class Runner
{
    public static Task RunInitAsync(string name, string? dir, string? platformOption, string? registryPath)
    {
        if (!IsValidProjectName(name))
        {
            Console.Error.WriteLine($"error: '{name}' is not a valid project name (use letters, digits, underscore, and dash; must start with a letter).");
            Environment.ExitCode = 2;
            return Task.CompletedTask;
        }

        IReadOnlyList<Platform> platforms;
        try { platforms = ParsePlatformOption(platformOption); }
        catch (Exception ex) { Console.Error.WriteLine($"error: {ex.Message}"); Environment.ExitCode = 2; return Task.CompletedTask; }

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

        var platformTokens = platforms.Select(Platforms.ToToken).ToList();
        Console.WriteLine($"-> scaffolding {name} ({string.Join(" + ", platformTokens)}) at {projectDir}");
        var coreDir = ProjectScaffolder.Scaffold(projectDir, name, platforms);

        Console.WriteLine($"-> installing foundation module");
        // Module source + shared NuGet packages install into the Core library, not the project root.
        var installer = new Installer.Installer(registry, coreDir, dryRun: false, platforms);
        var foundation = registry.GetModule("foundation");
        installer.Apply(foundation);

        // State lives at the project root (alongside the .sln), the dir the user runs `add` from.
        var state = StateFile.LoadOrCreate(projectDir);
        state.Platforms = platformTokens;
        state.Modules.Add("foundation");
        state.Save(projectDir);

        var copiedCount = Directory.GetFiles(Path.Combine(coreDir, "MonoDreams", "foundation"), "*", SearchOption.AllDirectories).Length;
        Console.WriteLine();
        Console.WriteLine($"Created project '{name}' ({string.Join(" + ", platformTokens)}). {copiedCount} engine files copied.");
        if (!string.IsNullOrEmpty(foundation.PostInstallNotes))
        {
            Console.WriteLine();
            Console.WriteLine(foundation.PostInstallNotes);
        }
        Console.WriteLine();
        Console.WriteLine($"Next steps:");
        Console.WriteLine($"  cd {Path.GetRelativePath(Directory.GetCurrentDirectory(), projectDir)}");
        Console.WriteLine($"  monodreams add rendering camera  # or whichever modules you need");
        if (platforms.Contains(Platform.Web))
            Console.WriteLine($"  dotnet build {name}.Web -p:MonoDreamsPlatform=web   # the web head (needs the wasm-tools workload)");
        if (platforms.Contains(Platform.Desktop))
            Console.WriteLine($"  dotnet run --project {name}.Desktop");

        return Task.CompletedTask;
    }

    public static Task RunAddAsync(string[] modules, string? presetName, string? projectPath, bool dryRun, string? registryPath)
    {
        var projectDir = Path.GetFullPath(projectPath ?? Directory.GetCurrentDirectory());

        Registry registry;
        try { registry = Registry.Load(registryPath); }
        catch (Exception ex) { Console.Error.WriteLine($"error: {ex.Message}"); Environment.ExitCode = 2; return Task.CompletedTask; }

        // A hand-edited / corrupted monodreams.json can fail to deserialize (malformed JSON) or carry an
        // unknown platform token (TargetPlatforms calls Platforms.Parse, which throws on a bad token).
        // Route both to the CLI's standard error path (exit code 2) instead of an uncaught crash.
        StateFile state;
        IReadOnlyList<Platform> targetPlatforms;
        try
        {
            state = StateFile.LoadOrCreate(projectDir);
            targetPlatforms = state.TargetPlatforms;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: could not read project state from '{Path.Combine(projectDir, StateFile.FileName)}': {ex.Message}");
            Environment.ExitCode = 2;
            return Task.CompletedTask;
        }

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

        // Resolve once per target platform. A module unsupported on a platform throws there; we treat
        // that as an unsupported combo to warn about, not a hard failure, as long as the module is
        // supported on at least one of the project's platforms. A module supported on none is an error.
        var resolvedPerPlatform = new Dictionary<Platform, List<string>>();
        var unsupported = new List<(string Module, Platform Platform)>();
        foreach (var platform in targetPlatforms)
        {
            try
            {
                resolvedPerPlatform[platform] = DependencyResolver.Resolve(registry, requested, state.Modules, platform);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("does not support platform"))
            {
                // Identify which requested modules are the unsupported ones for this platform.
                foreach (var m in requested.Distinct())
                {
                    ModuleManifest manifest;
                    try { manifest = registry.GetModule(m); }
                    catch { continue; }
                    if (!manifest.SupportsPlatform(platform)) unsupported.Add((m, platform));
                }
                // Re-resolve excluding the modules unsupported on this platform so the rest still install.
                var supportedHere = requested.Where(m =>
                {
                    try { return registry.GetModule(m).SupportsPlatform(platform); }
                    catch { return true; }
                }).ToList();
                resolvedPerPlatform[platform] = supportedHere.Count == 0
                    ? new List<string>()
                    : DependencyResolver.Resolve(registry, supportedHere, state.Modules, platform);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"error: {ex.Message}");
                Environment.ExitCode = 2;
                return Task.CompletedTask;
            }
        }

        foreach (var (module, platform) in unsupported.Distinct())
            Console.Error.WriteLine($"  warning: module '{module}' does not support platform '{Platforms.ToToken(platform)}' — skipping it for that platform (this project targets {string.Join(" + ", targetPlatforms.Select(Platforms.ToToken))}).");

        // A module that is requested directly but supported on NONE of the target platforms is a hard error.
        var supportedOnNone = requested.Distinct().Where(m =>
        {
            ModuleManifest manifest;
            try { manifest = registry.GetModule(m); } catch { return false; }
            return !targetPlatforms.Any(manifest.SupportsPlatform);
        }).ToList();
        if (supportedOnNone.Count > 0)
        {
            Console.Error.WriteLine($"error: module(s) {string.Join(", ", supportedOnNone)} support none of this project's target platform(s) ({string.Join(" + ", targetPlatforms.Select(Platforms.ToToken))}). Nothing to install.");
            Environment.ExitCode = 2;
            return Task.CompletedTask;
        }

        // Install order: union of all per-platform resolves, preserving the first platform's ordering.
        var resolved = new List<string>();
        var seen = new HashSet<string>();
        foreach (var platform in targetPlatforms)
            foreach (var m in resolvedPerPlatform[platform])
                if (seen.Add(m)) resolved.Add(m);

        if (resolved.Count == 0)
        {
            Console.WriteLine("All requested modules (and their dependencies) are already installed. Nothing to do.");
            return Task.CompletedTask;
        }

        Console.WriteLine($"Plan ({(dryRun ? "dry-run, no changes" : "applying")}):");
        Console.WriteLine($"  project: {projectDir}");
        Console.WriteLine($"  platforms: {string.Join(" + ", targetPlatforms.Select(Platforms.ToToken))}");
        Console.WriteLine($"  already installed: {(state.Modules.Count == 0 ? "<none>" : string.Join(", ", state.Modules))}");
        Console.WriteLine($"  to install: {string.Join(", ", resolved)}");
        Console.WriteLine();

        // Module source + packages go into the Core library (where the engine source compiles), located
        // either at the project root (legacy single-csproj layout) or a <Name>.Core/ subdirectory.
        var installRoot = FindCoreDir(projectDir);
        var installer = new Installer.Installer(registry, installRoot, dryRun, targetPlatforms);
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

    /// <summary>
    /// <c>monodreams migrate-colliders &lt;path&gt; [--dry-run]</c>: rewrites legacy version-1 native
    /// scenes/prefabs (embedded colliders) into the version-2 colliders-as-entities shape via the shared
    /// <see cref="MonoDreams.LevelEditor.Serialization.ColliderMigration"/> core (source-linked so the output
    /// is byte-canonical). Prints a per-file summary; a missing path or unparseable file exits with code 2.
    /// </summary>
    public static void RunMigrateColliders(string path, bool dryRun)
    {
        IReadOnlyList<MonoDreams.LevelEditor.Serialization.ColliderMigration.FileReport> reports;
        try
        {
            reports = MonoDreams.LevelEditor.Serialization.ColliderMigration.MigratePath(path, dryRun);
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Environment.ExitCode = 2;
            return;
        }
        catch (Exception ex)
        {
            // Unparseable input (or any migration failure): fail loud, write nothing, exit 2.
            Console.Error.WriteLine($"error: {ex.Message}");
            Environment.ExitCode = 2;
            return;
        }

        Console.WriteLine($"migrate-colliders ({(dryRun ? "dry-run, no changes" : "applying")}): {path}");
        Console.WriteLine();

        var changed = 0;
        var alreadyCurrent = 0;
        var boxesInPlace = 0;
        var childrenAdded = 0;
        foreach (var report in reports)
        {
            var r = report.Result;
            if (r.AlreadyCurrent)
            {
                alreadyCurrent++;
                Console.WriteLine($"  {report.Path}: already version 2 (no change)");
                continue;
            }
            if (!r.Changed)
            {
                Console.WriteLine($"  {report.Path}: no change");
                continue;
            }
            changed++;
            boxesInPlace += r.BoxesReshapedInPlace;
            childrenAdded += r.ChildEntitiesAdded;
            var verb = dryRun ? "would migrate" : "migrated";
            Console.WriteLine(
                $"  {report.Path}: {verb} → version 2 " +
                $"({r.BoxesReshapedInPlace} box(es) reshaped in place, {r.ChildEntitiesAdded} collider child entity(ies) added)");
        }

        Console.WriteLine();
        Console.WriteLine(
            $"{(dryRun ? "Would migrate" : "Migrated")} {changed} file(s), {alreadyCurrent} already current, " +
            $"{reports.Count} scanned. {boxesInPlace} box(es) reshaped in place, {childrenAdded} collider child entity(ies) added.");
    }

    /// <summary>
    /// <c>monodreams migrate &lt;path&gt; [--dry-run]</c>: the umbrella migrator — applies every known lift in
    /// version order (v1→v2 colliders, then v2→v3 camera-block→camera-entity) to each native scene/prefab via
    /// the shared <see cref="MonoDreams.LevelEditor.Serialization.SceneMigration"/> core (source-linked so the
    /// output is byte-canonical). Prints a per-file summary of which lifts ran; a missing path or unparseable
    /// file exits with code 2. Supersedes <c>migrate-colliders</c> (which runs only the collider lift).
    /// </summary>
    public static void RunMigrate(string path, bool dryRun)
    {
        IReadOnlyList<MonoDreams.LevelEditor.Serialization.SceneMigration.FileReport> reports;
        try
        {
            reports = MonoDreams.LevelEditor.Serialization.SceneMigration.MigratePath(path, dryRun);
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            Environment.ExitCode = 2;
            return;
        }
        catch (Exception ex)
        {
            // Unparseable input (or any migration failure): fail loud, write nothing, exit 2.
            Console.Error.WriteLine($"error: {ex.Message}");
            Environment.ExitCode = 2;
            return;
        }

        Console.WriteLine($"migrate ({(dryRun ? "dry-run, no changes" : "applying")}): {path}");
        Console.WriteLine();

        var changed = 0;
        var alreadyCurrent = 0;
        foreach (var report in reports)
        {
            var r = report.Result;
            if (r.AlreadyCurrent)
            {
                alreadyCurrent++;
                Console.WriteLine($"  {report.Path}: already current (version 3, no change)");
                continue;
            }
            if (!r.Changed)
            {
                Console.WriteLine($"  {report.Path}: no change");
                continue;
            }
            changed++;
            var verb = dryRun ? "would migrate" : "migrated";
            Console.WriteLine($"  {report.Path}: {verb} → version 3");
            foreach (var lift in r.LiftsApplied)
                Console.WriteLine($"      - {lift}");
        }

        Console.WriteLine();
        Console.WriteLine(
            $"{(dryRun ? "Would migrate" : "Migrated")} {changed} file(s), {alreadyCurrent} already current, " +
            $"{reports.Count} scanned.");
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

    /// <summary>
    /// Parses the <c>--platform</c> value into the target platform set. <c>desktop</c> / <c>web</c> select
    /// one backend; <c>multi</c> selects both. Null/empty defaults to desktop (the historical behavior).
    /// </summary>
    internal static IReadOnlyList<Platform> ParsePlatformOption(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return new[] { Platform.Desktop };
        return platform.Trim().ToLowerInvariant() switch
        {
            "desktop" => new[] { Platform.Desktop },
            "web" => new[] { Platform.Web },
            "multi" => Platforms.All.ToArray(),
            _ => throw new InvalidDataException($"Unknown platform '{platform}'. Expected 'desktop', 'web', or 'multi'."),
        };
    }

    /// <summary>
    /// Locates the shared game library directory where module source + packages install. Prefers a
    /// <c>&lt;Name&gt;.Core/</c> subdirectory (the multi-project scaffold layout); falls back to the project
    /// root for the legacy single-csproj layout.
    /// </summary>
    private static string FindCoreDir(string projectDir)
    {
        var coreSubdir = Directory.GetDirectories(projectDir, "*.Core")
            .FirstOrDefault(d => Directory.EnumerateFiles(d, "*.csproj", SearchOption.TopDirectoryOnly).Any());
        return coreSubdir ?? projectDir;
    }
}
