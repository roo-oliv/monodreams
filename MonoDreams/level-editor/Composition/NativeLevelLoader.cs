#nullable enable
using System;
using System.IO;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// The <b>native-first bridge</b> for the game boot: builds the probe delegate that
/// <c>LevelLoadRequestSystem</c> (level-loading) calls per <c>LoadLevelRequest</c> to decide whether a
/// level id resolves to a bundled native <c>.mdscene</c> (PS4). It lives in <c>level-editor</c> — which
/// already depends on <c>level-loading</c> — so the delegate can publish <see cref="LoadSceneRequest"/>
/// (the native reader's message) without <c>level-loading</c> ever depending upward on this module.
///
/// <para><b>Where scenes live + how they are read.</b> A level id <c>"island"</c> resolves to the
/// content-relative path <c>Levels/island.mdscene</c> (see <see cref="ContentRelativePath"/>). The probe
/// checks the bundled file through <c>TitleContainer</c> (the console-portable content-stream primitive —
/// a file read on desktop, an HTTP fetch on web, matching <c>BlenderLevelParserSystem</c>), and on a hit
/// publishes <c>LoadSceneRequest(rel, fromContent: true)</c>, which the composed <c>SceneReaderSystem</c>
/// resolves the same way. The scene files are bundled by an MGCB <c>/copy:</c> entry for
/// <c>Content/Levels/*.mdscene</c> (the same mechanism as <c>blender_level.json</c> / <c>game.mdproj</c>),
/// so <c>TitleContainer</c> finds them at <c>&lt;ContentRoot&gt;/Levels/&lt;id&gt;.mdscene</c> on every platform.</para>
/// </summary>
public static class NativeLevelLoader
{
    /// <summary>The content subfolder (relative to the content root) that holds native <c>.mdscene</c>
    /// level files — the manifest's <c>levelsDir</c> for the reference game.</summary>
    public const string LevelsDirectoryName = "Levels";

    /// <summary>The content-relative path of a native scene for <paramref name="levelId"/>, e.g.
    /// <c>"Levels/island.mdscene"</c> — the path a <c>LoadSceneRequest(fromContent:true)</c> carries and
    /// that <c>SceneReaderSystem</c> combines with the content root.</summary>
    public static string ContentRelativePath(string levelId) =>
        Path.Combine(LevelsDirectoryName, levelId + SceneWriter.SceneFileExtension);

    /// <summary>The absolute content-stream path (content root + relative) a <c>TitleContainer</c> probe
    /// opens — matching what <c>SceneReaderSystem</c> opens for a <c>fromContent</c> path.</summary>
    public static string ContentStreamPath(string contentRoot, string levelId) =>
        Path.Combine(contentRoot, ContentRelativePath(levelId));

    /// <summary>
    /// Builds the native-first probe for <c>LevelLoadRequestSystem</c>. Given a level id, it checks for a
    /// bundled native scene and, if present, publishes a <see cref="LoadSceneRequest"/> (handled synchronously
    /// by the composed <c>SceneReaderSystem</c>) and returns <c>true</c>; otherwise returns <c>false</c> so the
    /// caller falls through to LDtk/Blender.
    /// </summary>
    /// <param name="world">The world to publish <see cref="LoadSceneRequest"/> into.</param>
    /// <param name="contentRoot"><c>ContentManager.RootDirectory</c> (e.g. <c>"Content"</c>).</param>
    /// <param name="exists">Existence probe for the content-stream path. Defaults to a
    /// <c>TitleContainer.OpenStream</c> try/open (the portable, console-safe check). Injectable so tests
    /// can supply a layout without a real bundled file / <c>TitleContainer</c>.</param>
    /// <param name="fromContent">The read mode of the published <see cref="LoadSceneRequest"/>: <c>true</c>
    /// (default) resolves the bundled scene through <c>TitleContainer</c> (production, console-portable);
    /// <c>false</c> resolves the same relative path through <c>IPlatformServices</c> (a host read — used by
    /// in-memory/in-process tests and any future non-bundled scene source).</param>
    public static Func<string, bool> CreateProbe(World world, string contentRoot, Func<string, bool>? exists = null,
        bool fromContent = true)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        contentRoot ??= "Content";
        exists ??= TitleContainerExists;

        return levelId =>
        {
            if (string.IsNullOrEmpty(levelId)) return false;
            var full = ContentStreamPath(contentRoot, levelId);
            if (!exists(full)) return false;

            var rel = ContentRelativePath(levelId);
            Logger.Info($"[level-editor] Native scene found for level '{levelId}' at content '{full}'; loading via SceneReaderSystem.");
            world.Publish(new LoadSceneRequest(rel, fromContent));
            return true;
        };
    }

    /// <summary>Whether a bundled native scene exists for <paramref name="levelId"/> (the console-portable
    /// <c>TitleContainer</c> probe). Used by the manifest boot (<see cref="ManifestBoot"/>) to decide whether
    /// the resolved <c>startScene</c> is bootable yet.</summary>
    public static bool NativeSceneExists(string contentRoot, string levelId) =>
        !string.IsNullOrEmpty(levelId) && TitleContainerExists(ContentStreamPath(contentRoot ?? "Content", levelId));

    /// <summary>Console-portable existence probe: opening a <c>TitleContainer</c> stream succeeds only if
    /// the bundled content asset exists. Any failure (missing file / not-bundled) is treated as "absent".</summary>
    private static bool TitleContainerExists(string contentStreamPath)
    {
        try
        {
            using var stream = TitleContainer.OpenStream(contentStreamPath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
