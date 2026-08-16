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

        // Same option name as `add` — `--project` is accepted as a hidden alias there and here (see DirOption).
        var (dirOption, deprecatedDirOption) = DirOption.AddTo(cmd, "Target directory. Defaults to ./<name>.");

        var platformOption = new Option<string?>(
            name: "--platform",
            description: "Target platform: 'desktop' (DesktopGL head, default), 'web' (BlazorGL/KNI head), or 'multi' (both heads sharing one Core library).");
        platformOption.FromAmong("desktop", "web", "multi");
        cmd.AddOption(platformOption);

        cmd.SetHandler(async (name, dir, deprecatedDir, platform, registry) =>
        {
            if (!DirOption.TryResolve(dir, deprecatedDir, out var targetDir)) return;
            await Runner.RunInitAsync(name, targetDir, platform, registry);
        }, nameArg, dirOption, deprecatedDirOption, platformOption, registryOption);

        return cmd;
    }
}
