using System;
using System.IO;
using System.Linq;
using MonoDreams.LevelEditor.Serialization;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the PS6 <b>zero-touch level bundling</b> mechanism (project-persistence plan §3, banked
/// decision 2 — editor-appends-copy-line). Two halves: (1) the pure text transform
/// <see cref="MgcbLevelBundle.EnsureCopyEntry"/> appends an MGCB <c>/copy:</c> block for a new level
/// and is idempotent for one already present; (2) a scan asserts the committed <c>Content.mgcb</c>
/// carries a <c>/copy:</c> entry for EVERY committed <c>Content/Levels/*.mdscene</c> — i.e. the
/// bundling config is correct, so each committed level is reachable via its content-root-relative
/// <c>TitleContainer</c> path on every platform (verified landing in <c>bin/…</c> + <c>wwwroot/…</c>
/// by PS4/PS5 + the build).
///
/// Pure logic — string transforms + committed source files; no world, no <c>GraphicsDevice</c>.
/// Covers the level-loading premise "Native .mdscene levels are bundled by an MGCB /copy: entry".
/// </summary>
public class MgcbLevelBundleTests
{
    private const string ExistingMgcb =
        "#begin ./game.mdproj\n/copy:./game.mdproj\n#begin ./Levels/sample.mdscene\n/copy:./Levels/sample.mdscene\n";

    [Fact]
    public void EnsureCopyEntry_AppendsBlock_WhenAbsent()
    {
        var updated = MgcbLevelBundle.EnsureCopyEntry(ExistingMgcb, "island", out var changed);

        Assert.True(changed);
        Assert.Contains("#begin ./Levels/island.mdscene\n/copy:./Levels/island.mdscene\n", updated);
        // The original content is preserved; the block is appended (idempotent format matches existing lines).
        Assert.StartsWith(ExistingMgcb, updated);
    }

    [Fact]
    public void EnsureCopyEntry_Idempotent_WhenAlreadyPresent()
    {
        var updated = MgcbLevelBundle.EnsureCopyEntry(ExistingMgcb, "sample", out var changed);

        Assert.False(changed);
        Assert.Equal(ExistingMgcb, updated);
    }

    [Fact]
    public void EnsureCopyEntry_DistinguishesSimilarIds_WholeLineMatch()
    {
        // "sample" is present; "sample2" is NOT — a prefix match must not treat it as bundled.
        var updated = MgcbLevelBundle.EnsureCopyEntry(ExistingMgcb, "sample2", out var changed);

        Assert.True(changed);
        Assert.Contains("/copy:./Levels/sample2.mdscene", updated);
    }

    [Fact]
    public void EnsureCopyEntry_HandlesMissingTrailingNewline()
    {
        var noTrailingNewline = "/copy:./game.mdproj";

        var updated = MgcbLevelBundle.EnsureCopyEntry(noTrailingNewline, "island", out var changed);

        Assert.True(changed);
        Assert.Equal("/copy:./game.mdproj\n#begin ./Levels/island.mdscene\n/copy:./Levels/island.mdscene\n", updated);
    }

    [Fact]
    public void CopyLine_MatchesTheContentRelativeFormat()
    {
        Assert.Equal("./Levels/island.mdscene", MgcbLevelBundle.ContentRelativePath("island"));
        Assert.Equal("/copy:./Levels/island.mdscene", MgcbLevelBundle.CopyLine("island"));
        Assert.Equal("#begin ./Levels/island.mdscene", MgcbLevelBundle.BeginLine("island"));
    }

    // ---- Prefab bundling: the Prefabs dir joins the same zero-touch /copy: mechanism (PF-C) ----

    [Fact]
    public void PrefabCopyLine_MatchesTheContentRelativeFormat()
    {
        Assert.Equal("./Prefabs/npc-boldo.mdprefab", MgcbLevelBundle.PrefabContentRelativePath("npc-boldo"));
        Assert.Equal("/copy:./Prefabs/npc-boldo.mdprefab", MgcbLevelBundle.PrefabCopyLine("npc-boldo"));
        Assert.Equal("#begin ./Prefabs/npc-boldo.mdprefab", MgcbLevelBundle.PrefabBeginLine("npc-boldo"));
    }

    [Fact]
    public void EnsurePrefabCopyEntry_AppendsBlock_WhenAbsent()
    {
        var updated = MgcbLevelBundle.EnsurePrefabCopyEntry(ExistingMgcb, "npc-boldo", out var changed);

        Assert.True(changed);
        Assert.Contains("#begin ./Prefabs/npc-boldo.mdprefab\n/copy:./Prefabs/npc-boldo.mdprefab\n", updated);
        Assert.StartsWith(ExistingMgcb, updated);
    }

    [Fact]
    public void EnsurePrefabCopyEntry_Idempotent_WhenAlreadyPresent()
    {
        var withPrefab = MgcbLevelBundle.EnsurePrefabCopyEntry(ExistingMgcb, "door", out _);

        var again = MgcbLevelBundle.EnsurePrefabCopyEntry(withPrefab, "door", out var changed);

        Assert.False(changed);
        Assert.Equal(withPrefab, again);
    }

    [Fact]
    public void EnsurePrefabCopyEntry_DoesNotCollideWithASameNamedLevel()
    {
        // A level "shared" is bundled; a PREFAB "shared" is a different content path, so it must still append.
        var updated = MgcbLevelBundle.EnsurePrefabCopyEntry(ExistingMgcb, "sample", out var changed);

        Assert.True(changed); // "sample" level exists, but "./Prefabs/sample.mdprefab" does not
        Assert.Contains("/copy:./Prefabs/sample.mdprefab", updated);
    }

    // ---- The committed Content.mgcb bundles every committed level (config correctness) ----

    [Fact]
    public void CommittedMgcb_HasACopyEntry_ForEveryCommittedLevel()
    {
        var mgcbText = File.ReadAllText(RepoPath("MonoDreams.Examples.Core/Content/Content.mgcb"));
        var levelsDir = RepoPath("MonoDreams.Examples.Core/Content/Levels");
        var levels = Directory.GetFiles(levelsDir, "*.mdscene", SearchOption.AllDirectories);
        Assert.NotEmpty(levels);

        foreach (var level in levels)
        {
            var id = Path.GetFileNameWithoutExtension(level);
            // The mechanism keeps exactly ONE /copy: line per level; re-running the editor-append on the
            // committed mgcb would be a no-op (idempotent), confirming the entry is already there.
            var afterAppend = MgcbLevelBundle.EnsureCopyEntry(mgcbText, id, out var changed);
            Assert.False(changed, $"Committed level '{id}' has no /copy: entry in Content.mgcb — it would not bundle to the title.");
            Assert.Equal(mgcbText, afterAppend);
            // And the exact copy line is present as a whole line.
            Assert.Contains(MgcbLevelBundle.CopyLine(id), mgcbText.Split('\n').Select(l => l.Trim()));
        }
    }

    /// <summary>Resolves a repo-relative path by walking up from the test base dir to the repo root
    /// (the directory containing <c>MonoDreams.Examples.Core</c>).</summary>
    private static string RepoPath(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "MonoDreams.Examples.Core")))
            dir = Directory.GetParent(dir)?.FullName;
        Assert.NotNull(dir);
        return Path.Combine(dir!, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
