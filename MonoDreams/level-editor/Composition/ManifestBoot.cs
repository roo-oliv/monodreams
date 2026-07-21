#nullable enable
using System;
using System.IO;
using Microsoft.Xna.Framework;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// Resolves the game's <b>boot scene from the bundled project manifest</b> (PS4). The shipped game reads
/// <c>game.mdproj</c> at startup through <c>TitleContainer</c> — the same console-portable content read the
/// scenes use — and, when the manifest names a <c>startScene</c> that resolves to a bundled native
/// <c>.mdscene</c>, boots it via <c>LoadLevelRequest(startScene)</c> instead of the game's default entry
/// (e.g. the level-selection menu).
///
/// <para><b>Back-compat guard (banked decision 4 coexistence).</b> Resolution returns the boot scene id
/// <b>only</b> when (a) a manifest is present and parseable, (b) its <c>startScene</c> is non-empty, and (c)
/// a bundled native scene actually exists for it. If any fails — no manifest, empty <c>startScene</c>, or the
/// start scene has not been migrated to a native <c>.mdscene</c> yet — it returns <c>null</c> and the caller
/// keeps its existing boot path. So a manifest whose <c>startScene</c> points at a not-yet-committed level
/// (the Examples <c>island</c> placeholder until PS5) leaves the default boot untouched; when that level's
/// <c>.mdscene</c> lands, the boot flips to it automatically with no code change.</para>
///
/// <para>This is distinct from <see cref="EditorProjectContext"/>: the editor's context resolves the
/// <b>source</b> project root (env var / walk-up) to <b>write</b> into git; this reads the <b>bundled</b>
/// manifest (read-only, every platform) to drive the <b>boot</b>.</para>
/// </summary>
public static class ManifestBoot
{
    /// <summary>
    /// Reads the bundled project manifest (<c>&lt;contentRoot&gt;/game.mdproj</c>) via <c>TitleContainer</c>.
    /// Returns <c>null</c> (never throws) when the manifest is absent, unreadable, or malformed.
    /// </summary>
    /// <param name="contentRoot"><c>ContentManager.RootDirectory</c> (e.g. <c>"Content"</c>).</param>
    /// <param name="readContent">Optional content reader (content-stream path → text, or <c>null</c> if
    /// absent). Defaults to a <c>TitleContainer.OpenStream</c> read. Injectable for tests.</param>
    public static GameProject? TryReadManifest(string contentRoot, Func<string, string?>? readContent = null)
    {
        contentRoot ??= "Content";
        readContent ??= ReadContentText;

        var path = Path.Combine(contentRoot, GameProject.FileName);
        try
        {
            var json = readContent(path);
            if (string.IsNullOrEmpty(json)) return null;
            return CanonicalJson.Deserialize<GameProject>(json!);
        }
        catch (Exception e)
        {
            Logger.Warning($"[level-editor] Failed to read bundled {GameProject.FileName} at '{path}' " +
                           $"({e.GetType().Name}: {e.Message}); the manifest boot is skipped.");
            return null;
        }
    }

    /// <summary>
    /// Pure boot resolution: the <c>startScene</c> id to boot, or <c>null</c> when the caller should keep its
    /// default boot. See the type doc for the three-part guard.
    /// </summary>
    /// <param name="manifest">The bundled manifest, or <c>null</c>.</param>
    /// <param name="nativeSceneExists">Existence probe for a native scene id (e.g.
    /// <c>id =&gt; NativeLevelLoader.NativeSceneExists(contentRoot, id)</c>).</param>
    public static string? ResolveStartScene(GameProject? manifest, Func<string, bool> nativeSceneExists)
    {
        if (nativeSceneExists == null) throw new ArgumentNullException(nameof(nativeSceneExists));
        var startScene = manifest?.StartScene;
        if (string.IsNullOrEmpty(startScene)) return null;
        if (!nativeSceneExists(startScene!))
        {
            Logger.Info($"[level-editor] Manifest startScene='{startScene}' has no bundled native scene yet; " +
                        "keeping the default boot (the start scene lands in a later migration slice).");
            return null;
        }
        Logger.Info($"[level-editor] Manifest boot: startScene='{startScene}' resolves to a bundled native scene.");
        return startScene;
    }

    private static string? ReadContentText(string contentStreamPath)
    {
        try
        {
            using var stream = TitleContainer.OpenStream(contentStreamPath);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }
}
