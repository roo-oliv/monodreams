using System.CommandLine;
using System.Globalization;

namespace MonoDreams.Cli.Commands;

/// <summary>
/// Rejects unrecognized <c>--options</c> BEFORE System.CommandLine binds anything.
///
/// <para>Without this pass an option-looking token that no command declares is quietly bound to the
/// nearest positional argument: <c>monodreams add rendering --dir x</c> used to fail with
/// "Module '--dir' not found" (the token became a module name), and <c>monodreams migrate --bogus p</c>
/// bound <c>--bogus</c> as the scene path. Both errors point at the wrong thing entirely — a human loses
/// thirty seconds, an agent loses a whole debugging spiral chasing the registry or the file system.</para>
///
/// <para>The check walks the raw argv against the command tree, so it is independent of how many
/// positional arguments a command declares: the first option-shaped token that no command in scope
/// recognizes is reported by name, with a did-you-mean hint when a close match exists. The same walk
/// catches the swallow one level down — a known option whose value slot holds another option
/// (<c>--preset --registry x</c>) — instead of letting it bind "--registry" as the preset name.</para>
/// </summary>
internal static class StrictOptions
{
    /// <summary>The tool's invocation name, used to build the "for `monodreams add`" part of the message.
    /// Hardcoded rather than read from the root command because the root command's name follows the host
    /// executable (it is <c>testhost</c> under the test runner).</summary>
    internal const string ToolName = "monodreams";

    /// <summary>
    /// Aliases the invocation pipeline provides rather than the command tree — they never appear in
    /// <see cref="Command.Options"/>, so they must be whitelisted explicitly or `--help` would be rejected.
    /// </summary>
    private static readonly HashSet<string> ParserProvidedAliases = new(StringComparer.Ordinal)
    {
        "--help", "-h", "-?", "--version",
    };

    /// <summary>
    /// Returns the message for the first usage error in <paramref name="args"/> — an unknown option, or a
    /// known option whose value slot holds another option — or <c>null</c> when every option token is
    /// recognized by the command those args select.
    /// </summary>
    internal static string? FindUsageError(Command root, IReadOnlyList<string> args)
    {
        var command = root;
        var commandPath = new List<string>();
        var known = new Dictionary<string, Option>(StringComparer.Ordinal);
        Absorb(root, known);

        var sawArgument = false;
        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];

            // POSIX end-of-options: everything after `--` is a literal value, never an option.
            if (token == "--") break;

            // Response files (@file) and directives ([parse]) are expanded by the parser itself — step
            // over them instead of counting one as this command's first positional argument.
            if (token.Length > 1 && (token[0] == '@' || (token[0] == '[' && token[^1] == ']'))) continue;

            if (IsOptionLike(token))
            {
                var name = OptionName(token);
                if (known.TryGetValue(name, out var option))
                {
                    // `--opt value` (not `--opt=value`, not a flag): the next token is this option's value.
                    if (name.Length == token.Length && TakesValue(option) && i + 1 < args.Count)
                    {
                        // The parser would happily bind the FOLLOWING OPTION as this one's value — that is
                        // the same swallowing bug one level down (`--preset --registry x` binds the preset
                        // to "--registry" and then complains about a missing registry). Reject it here.
                        if (IsOptionLike(args[i + 1]))
                            return MissingValueMessage(name, args[i + 1], commandPath);
                        i++;
                    }
                    continue;
                }

                if (ParserProvidedAliases.Contains(name)) continue;

                return UnknownOptionMessage(name, known, commandPath);
            }

            // A subcommand can only appear before this command's own positional arguments.
            if (!sawArgument && FindSubcommand(command, token) is { } subcommand)
            {
                command = subcommand;
                commandPath.Add(subcommand.Name);
                Absorb(subcommand, known);
                continue;
            }

            sawArgument = true;
        }

        return null;
    }

    // ---- token shapes -------------------------------------------------------------------------

    /// <summary>A token is option-shaped when it starts with a dash and is neither a bare <c>-</c> nor a
    /// negative number (no command takes a numeric positional today, but a number must never read as an
    /// option).</summary>
    private static bool IsOptionLike(string token) =>
        token.Length > 1
        && token[0] == '-'
        && !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    /// <summary>Strips the value from the <c>--opt=value</c> / <c>--opt:value</c> forms.</summary>
    private static string OptionName(string token)
    {
        var cut = token.IndexOfAny(new[] { '=', ':' });
        return cut < 0 ? token : token[..cut];
    }

    private static bool TakesValue(Option option) =>
        option.ValueType != typeof(bool) && option.Arity.MaximumNumberOfValues > 0;

    // ---- command tree -------------------------------------------------------------------------

    /// <summary>Adds a command's own options to the in-scope alias set. Ancestors' options stay in scope
    /// (global options live on the root), and a subcommand's alias shadows an ancestor's on collision.</summary>
    private static void Absorb(Command command, Dictionary<string, Option> known)
    {
        foreach (var option in command.Options)
            foreach (var alias in option.Aliases)
                known[alias] = option;
    }

    private static Command? FindSubcommand(Command command, string token) =>
        command.Subcommands.FirstOrDefault(c => c.Aliases.Contains(token));

    // ---- message ------------------------------------------------------------------------------

    private static string Invocation(List<string> commandPath) =>
        commandPath.Count == 0 ? ToolName : $"{ToolName} {string.Join(' ', commandPath)}";

    private static string MissingValueMessage(string name, string next, List<string> commandPath)
    {
        var invocation = Invocation(commandPath);
        return $"error: option '{name}' for `{invocation}` expects a value, but the next token is '{next}'." +
               Environment.NewLine +
               $"       Write `{name}=<value>` if the value itself starts with a dash.";
    }

    private static string UnknownOptionMessage(string name, Dictionary<string, Option> known, List<string> commandPath)
    {
        var invocation = Invocation(commandPath);
        var suggestion = Suggest(name, known);
        var hint = suggestion is null ? "" : $" Did you mean '{suggestion}'?";
        return $"error: unknown option '{name}' for `{invocation}`.{hint}" + Environment.NewLine +
               $"       Run `{invocation} --help` for the options it accepts.";
    }

    /// <summary>
    /// The closest visible alias to the typo, or <c>null</c> when nothing is close enough. Hidden options
    /// (deprecated aliases) are accepted but never suggested — the suggestion is what the user should learn.
    /// </summary>
    private static string? Suggest(string name, Dictionary<string, Option> known)
    {
        string? best = null;
        var bestDistance = int.MaxValue;

        foreach (var (alias, option) in known)
        {
            if (option.IsHidden) continue;
            // A short alias (`-v`) is never a plausible fix for a long typo, and vice versa.
            if (Math.Abs(alias.Length - name.Length) > 4) continue;

            var distance = Distance(alias, name);
            if (distance < bestDistance || (distance == bestDistance && string.CompareOrdinal(alias, best) < 0))
            {
                bestDistance = distance;
                best = alias;
            }
        }

        var tolerance = name.Length <= 4 ? 1 : name.Length <= 8 ? 2 : 3;
        return bestDistance <= tolerance ? best : null;
    }

    /// <summary>Levenshtein edit distance — small inputs, so the plain two-row DP is plenty.</summary>
    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), substitution);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
