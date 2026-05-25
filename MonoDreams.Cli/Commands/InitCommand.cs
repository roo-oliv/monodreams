using System.CommandLine;

namespace MonoDreams.Cli.Commands;

internal static class InitCommand
{
    public static Command Build(Option<string?> registryOption)
    {
        var cmd = new Command("init", "Scaffold a new MonoGame project and install the foundation block.");

        var nameArg = new Argument<string>(
            name: "name",
            description: "Project name (will create a directory of this name).");
        cmd.AddArgument(nameArg);

        var dirOption = new Option<string?>(
            name: "--dir",
            description: "Target directory. Defaults to ./<name>.");
        cmd.AddOption(dirOption);

        cmd.SetHandler(async (name, dir, registry) =>
        {
            await Runner.RunInitAsync(name, dir, registry);
        }, nameArg, dirOption, registryOption);

        return cmd;
    }
}
