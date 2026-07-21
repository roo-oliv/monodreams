using System.CommandLine;

namespace MonoDreams.Cli.Commands;

internal static class MigrateCollidersCommand
{
    public static Command Build()
    {
        var cmd = new Command("migrate-colliders",
            "Migrate legacy version-1 native scenes/prefabs (embedded colliders) to version 2 (colliders as entities).");

        var pathArg = new Argument<string>(
            name: "path",
            description: "A .mdscene/.mdprefab file, or a directory to scan recursively.");
        cmd.AddArgument(pathArg);

        var dryRunOption = new Option<bool>(
            name: "--dry-run",
            description: "Report what would change without writing any file.");
        cmd.AddOption(dryRunOption);

        cmd.SetHandler((path, dryRun) => Runner.RunMigrateColliders(path, dryRun), pathArg, dryRunOption);

        return cmd;
    }
}
