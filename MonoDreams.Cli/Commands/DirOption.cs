using System.CommandLine;

namespace MonoDreams.Cli.Commands;

/// <summary>
/// The one canonical "where is the project" option, shared by <c>init</c> and <c>add</c>: <c>--dir</c>.
///
/// <para><c>add</c> used to spell the same concept <c>--project</c>, so a user who learned one command
/// reached for the wrong name in the other. <c>--project</c> stays accepted as a HIDDEN alias so existing
/// scripts keep working, but it is absent from <c>--help</c>: there is exactly one documented spelling,
/// and using the alias prints a deprecation note pointing at it.</para>
/// </summary>
internal static class DirOption
{
    internal const string Canonical = "--dir";
    internal const string DeprecatedAlias = "--project";

    /// <summary>
    /// Adds the canonical option plus its hidden alias to <paramref name="command"/>. Both must be bound
    /// by the handler and collapsed through <see cref="TryResolve"/>.
    /// </summary>
    internal static (Option<string?> Dir, Option<string?> Deprecated) AddTo(Command command, string description)
    {
        var dir = new Option<string?>(name: Canonical, description: description);
        command.AddOption(dir);

        var deprecated = new Option<string?>(
            name: DeprecatedAlias,
            description: $"Deprecated alias for {Canonical}.")
        {
            IsHidden = true,
        };
        command.AddOption(deprecated);

        return (dir, deprecated);
    }

    /// <summary>
    /// Collapses the canonical option and its deprecated alias into the single value the command runs with.
    /// Returns <c>false</c> — after printing the error and setting exit code 2 — when both were given with
    /// different values, since there is no sane way to pick one.
    /// </summary>
    internal static bool TryResolve(string? dir, string? deprecated, out string? value)
    {
        var hasDir = !string.IsNullOrEmpty(dir);
        var hasDeprecated = !string.IsNullOrEmpty(deprecated);

        if (hasDir && hasDeprecated && !string.Equals(dir, deprecated, StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                $"error: {Canonical} '{dir}' and {DeprecatedAlias} '{deprecated}' disagree — " +
                $"{DeprecatedAlias} is a deprecated alias for {Canonical}. Pass only {Canonical}.");
            Environment.ExitCode = 2;
            value = null;
            return false;
        }

        if (!hasDir && hasDeprecated)
            Console.Error.WriteLine($"note: {DeprecatedAlias} is a deprecated alias for {Canonical}; prefer {Canonical}.");

        value = hasDir ? dir : deprecated;
        return true;
    }
}
