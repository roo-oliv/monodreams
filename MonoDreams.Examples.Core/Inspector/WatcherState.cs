#if DEBUG
namespace MonoDreams.Examples.Inspector;

public class WatcherState
{
    private readonly HashSet<string> _checkedPaths = new();

    public bool IsChecked(string leafPath) => _checkedPaths.Contains(leafPath);

    public void ToggleLeaf(string leafPath)
    {
        if (!_checkedPaths.Remove(leafPath))
            _checkedPaths.Add(leafPath);
    }

    /// <summary>
    /// Toggle all leaves under a prefix. If any are checked, uncheck all; else check all.
    /// </summary>
    public void TogglePrefix(string prefix, List<string> allLeafPaths)
    {
        var anyChecked = allLeafPaths.Any(p => _checkedPaths.Contains(p));
        if (anyChecked)
        {
            foreach (var path in allLeafPaths)
                _checkedPaths.Remove(path);
        }
        else
        {
            foreach (var path in allLeafPaths)
                _checkedPaths.Add(path);
        }
    }

    /// <summary>
    /// Returns tri-state: true = all checked, false = none checked, null = some checked (intermediate).
    /// </summary>
    public bool? GetPrefixState(string prefix, List<string> allLeafPaths)
    {
        if (allLeafPaths.Count == 0) return false;

        var checkedCount = allLeafPaths.Count(p => _checkedPaths.Contains(p));
        if (checkedCount == 0) return false;
        if (checkedCount == allLeafPaths.Count) return true;
        return null; // intermediate
    }

    public bool HasAnyChecked() => _checkedPaths.Count > 0;

    public bool HasAnyCheckedUnder(string prefix, List<string> leafPaths)
    {
        return leafPaths.Any(p => _checkedPaths.Contains(p));
    }

    public void Clear() => _checkedPaths.Clear();
}
#endif
