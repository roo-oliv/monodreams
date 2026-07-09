#nullable enable
using System;
using System.IO;
using Microsoft.Xna.Framework;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.Platform;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// Resolves a prefab id to its validated <see cref="PrefabData"/> from the filesystem — the raw
/// <c>Func&lt;string, PrefabData?&gt;</c> the <c>PrefabExpander</c> / <c>SceneWriter</c> wrap in a
/// per-pass <see cref="PrefabCache"/>. It mirrors <see cref="NativeLevelLoader"/>'s scene resolution:
/// <list type="number">
///   <item><b>source-first</b> — when an editor <see cref="EditorProjectContext"/> is resolved and the
///   source <c>&lt;ProjectRoot&gt;/Prefabs/&lt;id&gt;.mdprefab</c> exists, read it through
///   <see cref="IPlatformServices"/> (the versioned source tree is authoritative the moment the editor
///   Saves — the bundled copy is stale until the next build, matching the scene source-first rule);</item>
///   <item><b>bundled</b> — else read the bundled <c>Content/Prefabs/&lt;id&gt;.mdprefab</c> through
///   <see cref="TitleContainer"/> (the console-portable content-stream primitive — a file read on
///   desktop, an HTTP fetch on web);</item>
///   <item><b>miss</b> — else <c>null</c> (the expander fails loud, the factory warns-and-drops).</item>
/// </list>
///
/// <para>A <b>null</b> project context (a shipped / console / web build) skips the source-first branch
/// entirely — bundled-only, byte-identical to the scene reader's shipped path. A malformed prefab
/// (found but not one-root) throws loud from <see cref="PrefabData.FromScene"/>.</para>
/// </summary>
public sealed class PrefabFileSource
{
    private readonly string _contentRoot;
    private readonly EditorProjectContext? _projectContext;
    private readonly Func<string, bool> _sourceExists;
    private readonly Func<string, string> _readSource;
    private readonly Func<string, string?> _readBundled;

    /// <param name="contentRoot"><c>ContentManager.RootDirectory</c> (e.g. <c>"Content"</c>).</param>
    /// <param name="projectContext">The resolved editor project context (enables source-first) or null.</param>
    /// <param name="sourceExists">Existence probe for the source-tree path (defaults to
    /// <c>IPlatformServices.FileExists</c>; injectable for tests).</param>
    /// <param name="readSource">Reader for the source-tree path (defaults to
    /// <c>IPlatformServices.ReadAllText</c>).</param>
    /// <param name="readBundled">Reader for the bundled content-stream path — returns the text or null if
    /// absent (defaults to a <c>TitleContainer.OpenStream</c> read; injectable for tests).</param>
    public PrefabFileSource(
        string contentRoot,
        EditorProjectContext? projectContext = null,
        Func<string, bool>? sourceExists = null,
        Func<string, string>? readSource = null,
        Func<string, string?>? readBundled = null)
    {
        _contentRoot = string.IsNullOrEmpty(contentRoot) ? "Content" : contentRoot;
        _projectContext = projectContext;
        _sourceExists = sourceExists ?? (p => PlatformServices.Current.FileExists(p));
        _readSource = readSource ?? (p => PlatformServices.Current.ReadAllText(p));
        _readBundled = readBundled ?? ReadBundledViaTitleContainer;
    }

    /// <summary>The content-relative prefab path for an id, e.g. <c>Prefabs/npc-boldo.mdprefab</c>.</summary>
    public static string ContentRelativePath(string prefabId) =>
        Path.Combine(MgcbLevelBundle.PrefabsDirectoryName, prefabId + PrefabWriter.PrefabFileExtension);

    /// <summary>Resolves <paramref name="prefabId"/> to its validated <see cref="PrefabData"/>, source-first
    /// then bundled, or <c>null</c> when no <c>.mdprefab</c> exists. Throws loud on a malformed prefab.</summary>
    public PrefabData? Resolve(string prefabId)
    {
        if (string.IsNullOrEmpty(prefabId)) return null;

        // Source-first (editor): the versioned source tree wins over any stale bundled copy.
        if (_projectContext is { Resolved: true, ProjectRoot: { } root })
        {
            var sourcePath = Path.Combine(root, MgcbLevelBundle.PrefabsDirectoryName,
                prefabId + PrefabWriter.PrefabFileExtension);
            if (_sourceExists(sourcePath))
            {
                Logger.Info($"[level-editor] Prefab '{prefabId}': source-first from '{sourcePath}'.");
                return Parse(prefabId, _readSource(sourcePath));
            }
        }

        // Bundled (console-portable TitleContainer).
        var bundled = _readBundled(Path.Combine(_contentRoot, ContentRelativePath(prefabId)));
        return bundled == null ? null : Parse(prefabId, bundled);
    }

    private static PrefabData? Parse(string prefabId, string json)
    {
        var scene = CanonicalJson.Deserialize<SceneData>(json);
        if (scene == null)
        {
            Logger.Warning($"[level-editor] Prefab '{prefabId}' deserialized to null; treating as absent.");
            return null;
        }
        return PrefabData.FromScene(prefabId, scene); // validates one-root (throws loud on malformed)
    }

    private static string? ReadBundledViaTitleContainer(string contentStreamPath)
    {
        try
        {
            using var stream = TitleContainer.OpenStream(contentStreamPath);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return null; // not bundled / not found
        }
    }
}
