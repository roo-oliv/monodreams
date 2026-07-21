using System.CommandLine;

namespace MonoDreams.Cli.Commands;

internal static class MigrateCommand
{
    public static Command Build()
    {
        var cmd = new Command("migrate",
            "Migrate native scenes/prefabs to the current format: applies every lift in order (v1→v2 " +
            "colliders, then v2→v3 camera-block→camera-entity). Supersedes migrate-colliders.");

        var pathArg = new Argument<string>(
            name: "path",
            description: "A .mdscene/.mdprefab file, or a directory to scan recursively.");
        cmd.AddArgument(pathArg);

        var dryRunOption = new Option<bool>(
            name: "--dry-run",
            description: "Report what would change without writing any file.");
        cmd.AddOption(dryRunOption);

        cmd.SetHandler((path, dryRun) => Runner.RunMigrate(path, dryRun), pathArg, dryRunOption);

        return cmd;
    }
}
