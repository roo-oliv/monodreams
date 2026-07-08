#nullable enable
using System;
using System.IO;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.Platform;
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
    /// Builds the native-first probe for <c>LevelLoadRequestSystem</c>. Given a level id it resolves the
    /// scene <b>source-first when an editor <paramref name="projectContext"/> is resolved</b> (UX-D
    /// pre-mortem #5): the versioned source tree is authoritative the moment the editor saves into it,
    /// while the bundled copy is stale until the next build — so a Restart-after-Save (which re-publishes
    /// <c>LoadLevelRequest</c> through this probe) reflects the last SAVE, not the last BUILD. Resolution:
    /// <list type="number">
    ///   <item><b>source-first</b> — <paramref name="projectContext"/> resolved AND the source
    ///   <c>&lt;LevelsPath&gt;/&lt;id&gt;.mdscene</c> exists → publish
    ///   <c>LoadSceneRequest(sourcePath, fromContent:false)</c> and return <c>true</c>;</item>
    ///   <item><b>bundled</b> — else the bundled <c>Content/Levels/&lt;id&gt;.mdscene</c> exists → publish
    ///   <c>LoadSceneRequest(rel, fromContent)</c> and return <c>true</c>;</item>
    ///   <item><b>miss</b> — else <c>false</c> so the caller falls through to LDtk/Blender.</item>
    /// </list>
    /// A <b>null</b> <paramref name="projectContext"/> (a shipped / console / web build) skips the
    /// source-first branch entirely — it never touches <paramref name="sourceExists"/> — so the bundled
    /// <c>TitleContainer</c> path is <b>byte-identical</b> to the pre-UX-D behaviour. The source-first
    /// resolution is the SAME logic <see cref="TryPublishSceneLoad"/> uses (shared
    /// <see cref="TryPublishSourceFirst"/>), so the probe and the bound-screen optional load agree.
    /// </summary>
    /// <param name="world">The world to publish <see cref="LoadSceneRequest"/> into.</param>
    /// <param name="contentRoot"><c>ContentManager.RootDirectory</c> (e.g. <c>"Content"</c>).</param>
    /// <param name="projectContext">The resolved editor project context (desktop-only, host-supplied) or
    /// null. When resolved it enables the source-first branch above; null keeps the bundled path unchanged.</param>
    /// <param name="exists">Existence probe for the bundled content-stream path. Defaults to a
    /// <c>TitleContainer.OpenStream</c> try/open (the portable, console-safe check). Injectable so tests
    /// can supply a layout without a real bundled file / <c>TitleContainer</c>.</param>
    /// <param name="fromContent">The read mode of a BUNDLED <see cref="LoadSceneRequest"/>: <c>true</c>
    /// (default) resolves the bundled scene through <c>TitleContainer</c> (production, console-portable);
    /// <c>false</c> resolves the same relative path through <c>IPlatformServices</c> (a host read — used by
    /// in-memory/in-process tests and any future non-bundled scene source).</param>
    /// <param name="sourceExists">Existence probe for the source-tree path (source-first branch). Defaults
    /// to <c>IPlatformServices.Current.FileExists</c>. Injectable for tests.</param>
    public static Func<string, bool> CreateProbe(World world, string contentRoot,
        EditorProjectContext? projectContext = null, Func<string, bool>? exists = null,
        bool fromContent = true, Func<string, bool>? sourceExists = null)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        contentRoot ??= "Content";
        exists ??= TitleContainerExists;
        sourceExists ??= p => PlatformServices.Current.FileExists(p);

        return levelId =>
        {
            if (string.IsNullOrEmpty(levelId)) return false;
            // Source-first when the project is resolved (the editor's save has already landed in the
            // source tree; the bundle is stale until the next build). Unresolved → bundled, byte-identical.
            if (TryPublishSourceFirst(world, levelId, projectContext, sourceExists)) return true;

            var full = ContentStreamPath(contentRoot, levelId);
            if (!exists(full)) return false;

            var rel = ContentRelativePath(levelId);
            Logger.Info($"[level-editor] Native scene found for level '{levelId}' at content '{full}'; loading via SceneReaderSystem.");
            world.Publish(new LoadSceneRequest(rel, fromContent));
            return true;
        };
    }

    /// <summary>
    /// The <b>source-first optional scene load</b> (UX-C §3.1): a screen with a bound scene id calls this
    /// in <c>Load</c> to bring its scene up under its code-built content, if that scene EXISTS. It is the
    /// SHARED source-first probe UX-D reuses for the transport's Restart. Resolution:
    /// <list type="number">
    ///   <item><b>source-first</b> — when <paramref name="projectContext"/> is resolved and the source
    ///   <c>&lt;LevelsPath&gt;/&lt;sceneId&gt;.mdscene</c> exists, publish
    ///   <c>LoadSceneRequest(sourcePath, fromContent:false)</c> (the source tree is authoritative the
    ///   moment the editor saves — the bundled copy is stale until the next build) and return
    ///   <c>true</c>;</item>
    ///   <item><b>bundled</b> — else, when the <c>TitleContainer</c> bundled copy exists, publish
    ///   <c>LoadSceneRequest(rel, fromContent:true)</c> (console-portable) and return <c>true</c>;</item>
    ///   <item><b>absent</b> — else no-op (<c>false</c>): the screen keeps its code-built content.</item>
    /// </list>
    /// A published request is a no-op when no <c>SceneReaderSystem</c> is composed (a plain non-editor
    /// menu run), so the call is always safe. Existence probes are injectable for tests.
    /// </summary>
    public static bool TryPublishSceneLoad(
        World world, string contentRoot, string sceneId, EditorProjectContext? projectContext,
        Func<string, bool>? sourceExists = null, Func<string, bool>? bundledExists = null)
    {
        if (world == null) throw new ArgumentNullException(nameof(world));
        if (string.IsNullOrEmpty(sceneId)) return false;
        contentRoot ??= "Content";
        sourceExists ??= p => PlatformServices.Current.FileExists(p);
        bundledExists ??= TitleContainerExists;

        if (TryPublishSourceFirst(world, sceneId, projectContext, sourceExists)) return true;

        var contentStreamPath = ContentStreamPath(contentRoot, sceneId);
        if (bundledExists(contentStreamPath))
        {
            Logger.Info($"[level-editor] Optional scene load '{sceneId}': bundled from content '{contentStreamPath}'.");
            world.Publish(new LoadSceneRequest(ContentRelativePath(sceneId), fromContent: true));
            return true;
        }

        return false; // absent → silently skip
    }

    /// <summary>
    /// The shared <b>source-first</b> resolution both <see cref="CreateProbe"/> (the Restart / boot probe)
    /// and <see cref="TryPublishSceneLoad"/> (the bound-screen optional load) use: when
    /// <paramref name="projectContext"/> is resolved and the source
    /// <c>&lt;LevelsPath&gt;/&lt;sceneId&gt;.mdscene</c> exists, publish
    /// <c>LoadSceneRequest(sourcePath, fromContent:false)</c> and return <c>true</c>; otherwise <c>false</c>
    /// (the caller falls through to the bundled path). An <b>unresolved / null</b> context short-circuits
    /// BEFORE probing <paramref name="sourceExists"/>, so a shipped build never touches the (absent) source
    /// tree and the bundled path stays byte-identical.
    /// </summary>
    private static bool TryPublishSourceFirst(
        World world, string sceneId, EditorProjectContext? projectContext, Func<string, bool> sourceExists)
    {
        if (projectContext is not { Resolved: true } || string.IsNullOrEmpty(projectContext.LevelsPath))
            return false;

        var sourcePath = Path.Combine(projectContext.LevelsPath!, sceneId + SceneWriter.SceneFileExtension);
        if (!sourceExists(sourcePath)) return false;

        Logger.Info($"[level-editor] Scene '{sceneId}': source-first from '{sourcePath}' (the source tree wins over any stale bundled copy).");
        world.Publish(new LoadSceneRequest(sourcePath, fromContent: false));
        return true;
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
