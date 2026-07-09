#nullable enable
using System;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// Zero-touch level bundling (project-persistence plan §3, banked decision 2 — the
/// <b>editor-appends-copy-line</b> mechanism). A newly-saved <c>Content/Levels/&lt;id&gt;.mdscene</c>
/// must bundle to the title WITHOUT a hand-edited <c>/copy:</c> line. The shipped game reads bundled
/// scenes read-only via <c>TitleContainer</c> over MGCB-<c>/copy:</c>-bundled files (console-portable —
/// the one all-platform read path, reaching desktop <c>bin/…/Content/Levels/</c> AND web
/// <c>wwwroot/Content/Levels/</c>), but MGCB's <c>.mgcb</c> is an explicit list with <b>no glob
/// syntax</b>. So the <c>/copy:</c> entry for a new level is appended by the dev tool that creates it:
/// the editor is the sole creator of new levels and already edits dev files (it writes the
/// <c>.mdscene</c> into the source tree, PS3), so on first Save of a new scene id it appends the
/// <c>/copy:</c> entry to the content project's <c>Content.mgcb</c>. A brand-new saved level is then
/// bootable after a normal build with zero manual MGCB editing.
///
/// <para><b>Why not a build-time glob.</b> A <c>Content.npl</c> Nopipeline glob was investigated and
/// rejected: a full regen of the hand-maintained <c>Content.mgcb</c> from the <c>.npl</c> sweeps the
/// gitignored Island placeholder-art pack into the MGCB texture build (via the recursive <c>*.png</c>
/// group), which breaks a fresh checkout where those files are absent. A raw-copy MSBuild
/// <c>&lt;None&gt;</c>/<c>.targets</c> can reach the desktop output but NOT the web
/// <c>wwwroot/Content/</c> (only the KNI content builder stages there, via the <c>.mgcb</c>). The
/// <c>/copy:</c> path is the one validated all-platform mechanism; appending its line from the editor
/// keeps exactly one mechanism (no double-copy). See the level-loading bundling premise.</para>
///
/// <para>Pure text transform (<see cref="EnsureCopyEntry"/>) — the editor overlay does the file IO
/// (read <c>Content.mgcb</c>, append if the entry is missing, write back) through
/// <c>IPlatformServices</c> on the desktop editor path only.</para>
/// </summary>
public static class MgcbLevelBundle
{
    /// <summary>The MGCB content-definition file name (at the content project root, beside the
    /// manifest — i.e. <c>EditorProjectContext.ProjectRoot/Content.mgcb</c>).</summary>
    public const string McgbFileName = "Content.mgcb";

    /// <summary>The content subfolder (under the content root) that holds native <c>.mdscene</c> levels.</summary>
    public const string LevelsDirectoryName = "Levels";

    /// <summary>The content subfolder (under the content root) that holds native <c>.mdprefab</c> prefabs.
    /// Prefabs bundle by the SAME <c>/copy:</c> mechanism as levels — the editor appends the entry on the
    /// first Save-Prefab of a new prefab id (the zero-touch rule; the Prefabs dir joins the Levels one).</summary>
    public const string PrefabsDirectoryName = "Prefabs";

    /// <summary>The content-root-relative scene path an MGCB entry references for a scene id,
    /// e.g. <c>./Levels/island.mdscene</c> (forward slashes; MGCB is platform-independent).</summary>
    public static string ContentRelativePath(string sceneId) =>
        RelativePath(LevelsDirectoryName, sceneId, SceneWriter.SceneFileExtension);

    /// <summary>The content-root-relative prefab path an MGCB entry references for a prefab id,
    /// e.g. <c>./Prefabs/npc-boldo.mdprefab</c>.</summary>
    public static string PrefabContentRelativePath(string prefabId) =>
        RelativePath(PrefabsDirectoryName, prefabId, PrefabWriter.PrefabFileExtension);

    /// <summary>A content-root-relative <c>./&lt;dir&gt;/&lt;id&gt;&lt;ext&gt;</c> path (forward slashes).</summary>
    private static string RelativePath(string dir, string id, string ext) => $"./{dir}/{id}{ext}";

    /// <summary>The MGCB <c>/copy:</c> line for a scene id (the whole line, e.g.
    /// <c>/copy:./Levels/island.mdscene</c>).</summary>
    public static string CopyLine(string sceneId) => "/copy:" + ContentRelativePath(sceneId);

    /// <summary>The MGCB <c>#begin</c> header line for a scene id (mirrors the existing entries).</summary>
    public static string BeginLine(string sceneId) => "#begin " + ContentRelativePath(sceneId);

    /// <summary>The MGCB <c>/copy:</c> line for a prefab id (e.g. <c>/copy:./Prefabs/npc-boldo.mdprefab</c>).</summary>
    public static string PrefabCopyLine(string prefabId) => "/copy:" + PrefabContentRelativePath(prefabId);

    /// <summary>The MGCB <c>#begin</c> header line for a prefab id.</summary>
    public static string PrefabBeginLine(string prefabId) => "#begin " + PrefabContentRelativePath(prefabId);

    /// <summary>
    /// Returns <paramref name="mgcbText"/> with a <c>#begin</c> + <c>/copy:</c> block for
    /// <c>Levels/&lt;sceneId&gt;.mdscene</c> appended, or the original text unchanged (with
    /// <paramref name="changed"/> <c>false</c>) if a <c>/copy:</c> entry for that exact level already
    /// exists. <b>Idempotent</b>: re-saving an already-bundled level is a no-op, so there is never a
    /// double-copy. The idempotency check is a whole-line match, so a similar id
    /// (<c>island</c> vs <c>island2</c>) is never mistaken for present.
    /// </summary>
    public static string EnsureCopyEntry(string mgcbText, string sceneId, out bool changed) =>
        EnsureEntry(mgcbText, ContentRelativePath(sceneId), out changed);

    /// <summary>
    /// The prefab counterpart of <see cref="EnsureCopyEntry"/>: appends a <c>#begin</c> + <c>/copy:</c>
    /// block for <c>Prefabs/&lt;prefabId&gt;.mdprefab</c> if absent (idempotent, whole-line match). The
    /// editor calls it on the first Save-Prefab of a new prefab id so it bundles zero-touch, exactly as
    /// a new level does.
    /// </summary>
    public static string EnsurePrefabCopyEntry(string mgcbText, string prefabId, out bool changed) =>
        EnsureEntry(mgcbText, PrefabContentRelativePath(prefabId), out changed);

    /// <summary>The shared append: adds a <c>#begin &lt;rel&gt;</c> + <c>/copy:&lt;rel&gt;</c> block for a
    /// content-relative path if its <c>/copy:</c> whole-line is absent; idempotent otherwise.</summary>
    private static string EnsureEntry(string mgcbText, string relativePath, out bool changed)
    {
        mgcbText ??= string.Empty;
        var copyLine = "/copy:" + relativePath;
        if (ContainsLine(mgcbText, copyLine))
        {
            changed = false;
            return mgcbText;
        }

        changed = true;
        var prefix = mgcbText.Length > 0 && !mgcbText.EndsWith("\n", StringComparison.Ordinal)
            ? mgcbText + "\n"
            : mgcbText;
        return prefix + "#begin " + relativePath + "\n" + copyLine + "\n";
    }

    /// <summary>Whether <paramref name="text"/> contains <paramref name="line"/> as a whole (trimmed)
    /// line — so a <c>/copy:</c> entry is matched exactly, never as a prefix of a longer id's entry.</summary>
    private static bool ContainsLine(string text, string line)
    {
        foreach (var candidate in text.Split('\n'))
            if (candidate.Trim() == line)
                return true;
        return false;
    }
}
