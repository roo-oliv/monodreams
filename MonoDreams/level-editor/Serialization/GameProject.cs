#nullable enable
using System;
using System.Text.Json.Serialization;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// The <b>project manifest</b> that makes "a MonoDreams game" a first-class, versionable unit —
/// the on-disk shape of <c>game.mdproj</c>. It is small, human-authored/edited data: the entry
/// scene the game boots, where its native <c>.mdscene</c> levels live, and which
/// <c>Content/</c> subfolders hold the assets the scenes reference.
///
/// <para><b>Read by both the editor and the game.</b> The editor reads it to anchor the project
/// root (see <see cref="MonoDreams.LevelEditor.Composition.EditorProjectContext"/>), to know the
/// levels directory, and (later) to list levels for an open/switch UI; the shipped game reads it at
/// boot to resolve <see cref="StartScene"/> into a level to load. Both read/write it through
/// <see cref="CanonicalJson"/> — the SAME canonical policy scenes use — so the manifest is
/// byte-stable and diffable too (a git diff of a project setting is one line).</para>
///
/// <para><b>Where it lives.</b> This type is colocated with <see cref="CanonicalJson"/> and
/// <see cref="SceneData"/> in the <c>level-editor</c> module (namespace
/// <c>MonoDreams.LevelEditor.Serialization</c>) because it is serialized through that same canonical
/// policy; every module compiles into <c>MonoDreams.dll</c>, so the shipped game boot path (which
/// reads the manifest later, PS4) reaches it without a new cross-module dependency. The committed
/// example manifest for the reference game is
/// <c>MonoDreams.Examples.Core/Content/game.mdproj</c> (under <c>Content/</c> so MGCB bundles it and
/// the shipped game can read it via <c>TitleContainer</c>, exactly like the bundled <c>.mdscene</c> levels).</para>
///
/// <para><b>Scope (v1).</b> The four fields below only. Engine-version pinning, build settings, and
/// per-scene metadata are deferred (add fields here when needed — the canonical serializer keeps
/// the file stable as fields grow).</para>
/// </summary>
public sealed class GameProject
{
    /// <summary>The default manifest file name (<c>game.mdproj</c>).</summary>
    public const string FileName = "game.mdproj";

    /// <summary>The default levels directory (relative to the project root) when the manifest omits
    /// <see cref="LevelsDir"/>.</summary>
    public const string DefaultLevelsDir = "Levels";

    /// <summary>Manifest schema version. Bump on any breaking change to the manifest shape.</summary>
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; } = 1;

    /// <summary>The id of the scene the game boots (resolved to a level to load at startup — PS4).
    /// A scene-local level id (no extension / directory), e.g. <c>"island"</c>.</summary>
    [JsonPropertyName("startScene")]
    public string StartScene { get; set; } = "";

    /// <summary>The directory (relative to the project root, i.e. the folder holding this manifest)
    /// where native <c>.mdscene</c> level files live. Defaults to <see cref="DefaultLevelsDir"/>.</summary>
    [JsonPropertyName("levelsDir")]
    public string LevelsDir { get; set; } = DefaultLevelsDir;

    /// <summary>The <c>Content/</c> subfolders that hold the assets scenes reference — the palette's
    /// catalog scan roots and the versioning boundary's declared asset list. <b>Authored order is
    /// preserved</b> on write (a plain array, not a sorted map), so the manifest diff stays minimal.</summary>
    [JsonPropertyName("assetRoots")]
    public string[] AssetRoots { get; set; } = Array.Empty<string>();
}
