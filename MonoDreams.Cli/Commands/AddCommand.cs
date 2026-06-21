using System.CommandLine;

namespace MonoDreams.Cli.Commands;

internal static class AddCommand
{
    public static Command Build(Option<string?> registryOption)
    {
        var cmd = new Command("add", "Install one or more modules into the current project.");

        var modulesArg = new Argument<string[]>(
            name: "modules",
            description: "Module names to install (e.g. `monodreams add rendering camera collision`).")
        {
            Arity = ArgumentArity.ZeroOrMore
        };
        cmd.AddArgument(modulesArg);

        var presetOption = new Option<string?>(
            name: "--preset",
            description: "Install all modules in a preset (see `monodreams list` for available presets).");
        cmd.AddOption(presetOption);

        var projectOption = new Option<string?>(
            name: "--project",
            description: "Path to the project directory. Defaults to cwd.");
        cmd.AddOption(projectOption);

        var dryRunOption = new Option<bool>(
            name: "--dry-run",
            description: "Resolve and print the install plan without copying any files.");
        cmd.AddOption(dryRunOption);

        cmd.SetHandler(async (modules, preset, project, dryRun, registry) =>
        {
            await Runner.RunAddAsync(modules, preset, project, dryRun, registry);
        }, modulesArg, presetOption, projectOption, dryRunOption, registryOption);

        return cmd;
    }
}
