using System.CommandLine;
using MonoDreams.Cli.Commands;

namespace MonoDreams.Cli;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
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

        return await root.InvokeAsync(args);
    }
}
