#nullable enable
using System;
using System.Collections.Generic;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// A <b>per-load-pass cache</b> over a prefab source (<c>id → <see cref="PrefabData"/></c>): resolves
/// each prefab id at most once per operation and memoizes the (possibly <c>null</c>) result, so an
/// expansion pass that instantiates several instances of the same prefab — or a scene save that
/// compacts several instances against the same prefab root — reads and validates the <c>.mdprefab</c>
/// once. The underlying source is injected (source-first via the editor project context in-editor,
/// else <c>TitleContainer</c> in a shipped game, or an in-memory dictionary in tests); this wrapper
/// owns only the per-pass memo, so the raw source can do real file I/O without being hit repeatedly.
/// </summary>
public sealed class PrefabCache
{
    private readonly Func<string, PrefabData?> _source;
    private readonly Dictionary<string, PrefabData?> _resolved = new();

    /// <param name="source">The raw resolver: a prefab id → its validated <see cref="PrefabData"/>, or
    /// <c>null</c> when no <c>.mdprefab</c> exists for that id. May throw loud on a malformed prefab.</param>
    public PrefabCache(Func<string, PrefabData?> source) =>
        _source = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>Resolves <paramref name="prefabId"/> through the source, memoized for this pass. Returns
    /// <c>null</c> when the prefab does not exist (the caller decides whether that is fail-loud — the
    /// reader aborts — or warn-and-drop — the factory).</summary>
    public PrefabData? Resolve(string prefabId)
    {
        if (_resolved.TryGetValue(prefabId, out var cached)) return cached;
        var data = _source(prefabId);
        _resolved[prefabId] = data;
        return data;
    }
}
