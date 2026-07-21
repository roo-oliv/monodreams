#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using MonoDreams.LevelEditor.Assets;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// Ship-readiness analysis of a native scene (project-persistence plan §7). A scene is
/// <b>"ship-ready / fully portable"</b> iff it carries <b>zero <c>file:</c> AssetKeys</b> — every
/// asset reference has graduated from the editor's drop-folder <c>file:</c> scheme (loaded at
/// runtime from the gitignored asset folder, desktop-editor-first) to an MGCB <b>content key</b>
/// (processed, shipped, web-ready, portable). A scene with a <c>file:</c> key loads a magenta
/// placeholder on a fresh checkout / on web (where there is no directory scan), so "zero
/// <c>file:</c> keys" is the checkable invariant that says a committed level is portable.
///
/// <para>This is a pure analyzer (no IO): it walks the serialized component bodies of a
/// <see cref="SceneData"/> for any JSON string using the <see cref="FileAssetKey.Prefix"/> scheme,
/// so it catches today's <c>SpriteInfoComponent.AssetKey</c> and any future file-scheme reference
/// (e.g. a font key) without enumerating component types. Exposed as a unit-testable function
/// (<see cref="FindFileAssetKeys"/> / <see cref="IsShipReady"/>), asserted over the committed
/// <c>Content/Levels/**</c> scenes by a test, and surfaced as a loud warning on Save (the editor
/// warns when it writes a scene that still has <c>file:</c> keys).</para>
/// </summary>
public static class SceneLint
{
    /// <summary>A single <c>file:</c>-scheme asset reference found in a scene: which entity
    /// (index into <see cref="SceneData.Entities"/>) and which registered component key carries it,
    /// plus the offending key value.</summary>
    public readonly record struct FileAssetKeyFinding(int EntityIndex, string ComponentKey, string AssetKey);

    /// <summary>Whether every asset reference in <paramref name="scene"/> has graduated to a content
    /// key (i.e. there are <b>zero</b> <c>file:</c> keys). Pure — no IO.</summary>
    public static bool IsShipReady(SceneData? scene) => FindFileAssetKeys(scene).Count == 0;

    /// <summary>
    /// Returns every <c>file:</c> AssetKey in <paramref name="scene"/>, each tagged with the entity
    /// index + component key that carries it. A ship-ready scene returns an empty list. Pure — it
    /// recursively walks each entity's serialized component bodies for any JSON string using the
    /// <see cref="FileAssetKey"/> scheme.
    /// </summary>
    public static IReadOnlyList<FileAssetKeyFinding> FindFileAssetKeys(SceneData? scene)
    {
        var findings = new List<FileAssetKeyFinding>();
        if (scene?.Entities == null) return findings;

        for (var i = 0; i < scene.Entities.Count; i++)
        {
            var entity = scene.Entities[i];
            if (entity?.Components == null) continue;
            foreach (var (componentKey, body) in entity.Components)
                CollectFileKeys(body, i, componentKey, findings);
        }
        return findings;
    }

    /// <summary>Recursively collects <c>file:</c>-scheme string values from a serialized component
    /// body (an object, array, or scalar).</summary>
    private static void CollectFileKeys(JsonElement element, int entityIndex, string componentKey, List<FileAssetKeyFinding> findings)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var value = element.GetString();
                if (FileAssetKey.IsFileKey(value))
                    findings.Add(new FileAssetKeyFinding(entityIndex, componentKey, value!));
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    CollectFileKeys(property.Value, entityIndex, componentKey, findings);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectFileKeys(item, entityIndex, componentKey, findings);
                break;
        }
    }
}
