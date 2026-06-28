using System.CommandLine;

namespace MonoDreams.Cli.Commands;

internal static class InitCommand
{
    public static Command Build(Option<string?> registryOption)
    {
        var cmd = new Command("init", "Scaffold a new MonoGame project and install the foundation module.");

        var nameArg = new Argument<string>(
            name: "name",
            description: "Project name (will create a directory of this name).");
        cmd.AddArgument(nameArg);

        var dirOption = new Option<string?>(
            name: "--dir",
            description: "Target directory. Defaults to ./<name>.");
        cmd.AddOption(dirOption);

        var platformOption = new Option<string?>(
            name: "--platform",
            description: "Target platform: 'desktop' (DesktopGL head, default), 'web' (BlazorGL/KNI head), or 'multi' (both heads sharing one Core library).");
        platformOption.FromAmong("desktop", "web", "multi");
        cmd.AddOption(platformOption);

        cmd.SetHandler(async (name, dir, platform, registry) =>
        {
            await Runner.RunInitAsync(name, dir, platform, registry);
        }, nameArg, dirOption, platformOption, registryOption);

        return cmd;
    }
}
