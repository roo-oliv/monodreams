using MonoDreams.Cli.Manifest;

namespace MonoDreams.Cli.Resolver;

internal static class DependencyResolver
{
    public static List<string> Resolve(Registry registry, IEnumerable<string> requested, IEnumerable<string> alreadyInstalled)
    {
        var seen = new HashSet<string>(alreadyInstalled);
        var visiting = new HashSet<string>();
        var ordered = new List<string>();

        foreach (var name in requested) Visit(name, registry, seen, visiting, ordered, path: new Stack<string>());
        return ordered;
    }

    private static void Visit(string name, Registry registry, HashSet<string> seen, HashSet<string> visiting, List<string> ordered, Stack<string> path)
    {
        if (seen.Contains(name)) return;
        if (!visiting.Add(name))
        {
            var cycle = string.Join(" -> ", path.Reverse().Concat(new[] { name }));
            throw new InvalidOperationException($"Cyclic dependency: {cycle}");
        }

        path.Push(name);
        var manifest = registry.GetBlock(name);
        foreach (var dep in manifest.Dependencies)
            Visit(dep, registry, seen, visiting, ordered, path);
        path.Pop();

        visiting.Remove(name);
        seen.Add(name);
        ordered.Add(name);
    }
}
