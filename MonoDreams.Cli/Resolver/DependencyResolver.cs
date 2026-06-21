using MonoDreams.Cli.Manifest;

namespace MonoDreams.Cli.Resolver;

internal static class DependencyResolver
{
    /// <summary>
    /// Resolves the requested modules and their transitive dependencies in install order, for all platforms.
    /// </summary>
    public static List<string> Resolve(Registry registry, IEnumerable<string> requested, IEnumerable<string> alreadyInstalled)
        => Resolve(registry, requested, alreadyInstalled, targetPlatform: null);

    /// <summary>
    /// Resolves the requested modules and their transitive dependencies in install order. When
    /// <paramref name="targetPlatform"/> is set, a module whose <c>platforms</c> tag excludes that platform is
    /// rejected (if requested directly) or skipped (if it is only an optional/transitive dependency that the
    /// platform cannot support). Untagged modules support every platform.
    /// </summary>
    public static List<string> Resolve(Registry registry, IEnumerable<string> requested, IEnumerable<string> alreadyInstalled, Platform? targetPlatform)
    {
        var seen = new HashSet<string>(alreadyInstalled);
        var visiting = new HashSet<string>();
        var ordered = new List<string>();

        foreach (var name in requested)
            Visit(name, registry, seen, visiting, ordered, targetPlatform, path: new Stack<string>());
        return ordered;
    }

    private static void Visit(string name, Registry registry, HashSet<string> seen, HashSet<string> visiting, List<string> ordered, Platform? targetPlatform, Stack<string> path)
    {
        if (seen.Contains(name)) return;
        if (!visiting.Add(name))
        {
            var cycle = string.Join(" -> ", path.Reverse().Concat(new[] { name }));
            throw new InvalidOperationException($"Cyclic dependency: {cycle}");
        }

        path.Push(name);
        var manifest = registry.GetModule(name);

        if (targetPlatform is { } platform && !manifest.SupportsPlatform(platform))
            throw new InvalidOperationException(
                $"Module '{name}' does not support platform '{Platforms.ToToken(platform)}' (supports: {string.Join(", ", manifest.SupportedPlatforms.Select(Platforms.ToToken))}).");

        foreach (var dep in manifest.Dependencies)
            Visit(dep, registry, seen, visiting, ordered, targetPlatform, path);
        path.Pop();

        visiting.Remove(name);
        seen.Add(name);
        ordered.Add(name);
    }
}
