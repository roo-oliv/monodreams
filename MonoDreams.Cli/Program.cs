using System.CommandLine;
using MonoDreams.Cli.Commands;

namespace MonoDreams.Cli;

internal static class Program
{
    internal static Task<int> Main(string[] args) => RunAsync(args);

    /// <summary>
    /// The real entry point (<see cref="Main"/> only forwards to it, so tests can drive the CLI exactly the
    /// way the shell does). Unknown options are rejected before parsing — see <see cref="StrictOptions"/>.
    /// </summary>
    internal static async Task<int> RunAsync(string[] args)
    {
        // The process-wide exit code is the fallback channel commands report user errors on (below); start
        // every run from a clean slate so nothing leaks in from an earlier in-process run.
        Environment.ExitCode = 0;

        var root = BuildRootCommand();

        var usageError = StrictOptions.FindUsageError(root, args);
        if (usageError is not null)
        {
            Console.Error.WriteLine(usageError);
            return 2;
        }

        var exitCode = await root.InvokeAsync(args);

        // Commands report user errors by writing to stderr and setting Environment.ExitCode, then returning
        // normally. The value RETURNED from Main wins over Environment.ExitCode, so without this fold a
        // failed `monodreams add` would print `error: ...` and still exit 0 — invisible to any script or
        // agent that checks the exit status.
        return exitCode != 0 ? exitCode : Environment.ExitCode;
    }

    internal static RootCommand BuildRootCommand()
    {
        var root = new RootCommand(
            "MonoDreams CLI — copies engine source modules into your project.");

        var registryOption = new Option<string?>(
            name: "--registry",
            description: "Path to a local registry directory (default: ./registry relative to cwd, or the registry shipped with this tool).");
        root.AddGlobalOption(registryOption);

        root.AddCommand(InitCommand.Build(registryOption));
        root.AddCommand(AddCommand.Build(registryOption));
        root.AddCommand(ListCommand.Build(registryOption));
        root.AddCommand(MigrateCommand.Build());
        root.AddCommand(MigrateCollidersCommand.Build());

        return root;
    }
}
