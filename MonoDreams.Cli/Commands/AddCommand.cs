using System.CommandLine;

namespace MonoDreams.Cli.Commands;

internal static class AddCommand
{
    public static Command Build(Option<string?> registryOption)
    {
        var cmd = new Command("add", "Install one or more blocks into the current project.");

        var blocksArg = new Argument<string[]>(
            name: "blocks",
            description: "Block names to install (e.g. `monodreams add rendering camera collision`).")
        {
            Arity = ArgumentArity.ZeroOrMore
        };
        cmd.AddArgument(blocksArg);

        var presetOption = new Option<string?>(
            name: "--preset",
            description: "Install all blocks in a preset (see `monodreams list` for available presets).");
        cmd.AddOption(presetOption);

        var projectOption = new Option<string?>(
            name: "--project",
            description: "Path to the project directory. Defaults to cwd.");
        cmd.AddOption(projectOption);

        var dryRunOption = new Option<bool>(
            name: "--dry-run",
            description: "Resolve and print the install plan without copying any files.");
        cmd.AddOption(dryRunOption);

        cmd.SetHandler(async (blocks, preset, project, dryRun, registry) =>
        {
            await Runner.RunAddAsync(blocks, preset, project, dryRun, registry);
        }, blocksArg, presetOption, projectOption, dryRunOption, registryOption);

        return cmd;
    }
}
