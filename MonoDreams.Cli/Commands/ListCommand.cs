using System.CommandLine;

namespace MonoDreams.Cli.Commands;

internal static class ListCommand
{
    public static Command Build(Option<string?> registryOption)
    {
        var cmd = new Command("list", "List available blocks and presets from the registry.");

        var verboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Include block dependencies, NuGet refs, and premises references.");
        cmd.AddOption(verboseOption);

        cmd.SetHandler((verbose, registry) =>
        {
            Runner.RunList(verbose, registry);
        }, verboseOption, registryOption);

        return cmd;
    }
}
