using System.Security.Cryptography;
using MonoDreams.Input;
using MonoDreams.LevelEditor.Channel;

namespace MonoDreams.Tests.IntegrationTests;

/// <summary>
/// The test-isolation TRIPWIRE (wave PF-E, hardening item 3). The level editor can WRITE the game's
/// SOURCE content tree — <c>Content/Levels/&lt;id&gt;.mdscene</c>, <c>Content/Prefabs/&lt;id&gt;.mdprefab</c>,
/// and MGCB <c>/copy:</c> lines appended to <c>Content.mgcb</c> — gated on a resolved project root. A
/// spawned editor-enabled game process (<c>MONODREAMS_EDITOR=1</c>) run from the repo would otherwise
/// resolve the user's REAL <c>MonoDreams.Examples.Core/Content</c> and could overwrite it. Two layers guard
/// against that regressing:
///
/// <list type="number">
///   <item><b>Safe-by-construction</b> (the fix): <see cref="GameTestRunner"/> pins every spawned process
///   to an isolated temp project root via <c>MONODREAMS_PROJECT_ROOT</c> (a real manifest so resolution
///   does not fall through to the repo). The <see cref="EditorProcess_ResolvesTheIsolatedTempRoot_NeverTheRealContentTree"/>
///   fact asserts, from an editor-on run's own log, that the editor resolved THAT temp root and never a
///   path under <c>MonoDreams.Examples.Core</c>.</item>
///   <item><b>Last-line defense</b>: <see cref="ContentTreeGuardFixture"/> is a collection fixture wrapping
///   the write-prone editor-enabled spawned suites (this one, <c>UniversalOverlayTests</c>,
///   <c>DemosEditorOverlayTests</c>). It fingerprints the REAL <c>Content.mgcb</c> + the <c>Levels</c>/
///   <c>Prefabs</c> file listing when the collection starts and asserts them byte-identical when it ends —
///   so if any test in the collection ever writes the real tree, the collection FAILS.</item>
/// </list>
///
/// <para>Chosen mechanism note: a collection fixture (broad before/after over every guarded run) PLUS a
/// dedicated resolved-root assertion (direct, clearly-attributed proof the pin takes effect) — over a
/// bare per-suite hook — because the fixture brackets the OTHER suites' runs too and the fact pinpoints a
/// pin regression even before any write is attempted.</para>
/// </summary>
[Collection(ContentTreeGuardCollection.Name)]
public class ContentTreeIsolationTests
{
    private static readonly Dictionary<string, string> EditorEnv = new() { ["MONODREAMS_EDITOR"] = "1" };

    [Fact]
    public async Task EditorProcess_ResolvesTheIsolatedTempRoot_NeverTheRealContentTree()
    {
        // A menu run under the editor flag: Game1 resolves the project root at startup and logs it. The
        // menu has no InputReplaySystem, so the editor-op channel holds the session open and drives exit.
        var result = await GameTestRunner.RunAsync(new InputReplayPlan
        {
            StartScreen = "LevelSelection",
            Description = "Editor-flag menu run to observe the resolved project root",
            Commands = new List<InputReplayCommand>(),
        },
        timeoutSeconds: 60,
        environment: EditorEnv,
        editorOpPlan: new EditorOpPlan
        {
            Description = "idle a few frames, then exit",
            Ops = new List<EditorOp> { new() { Frame = 10, Kind = EditorOpKind.MoveCursor, X = 100, Y = 100 } },
            TailFrames = 5,
        });

        Assert.Equal(0, result.ExitCode);

        var resolved = result.LogLines.Where(l => l.Contains("Project resolved: root=")).ToList();
        Assert.NotEmpty(resolved); // the editor DID resolve a project root (the flag was honoured)
        // It resolved the ISOLATED temp root the runner pinned — never the real repo content tree.
        Assert.All(resolved, l => Assert.Contains(result.ProjectRoot, l));
        Assert.All(resolved, l => Assert.DoesNotContain("MonoDreams.Examples.Core", l));
    }
}

/// <summary>Fingerprints the REAL reference-game content tree at collection start and asserts it unchanged
/// at collection end — the tripwire's last-line defense (see <see cref="ContentTreeIsolationTests"/>).</summary>
public sealed class ContentTreeGuardFixture : IDisposable
{
    private readonly string _contentDir;
    private readonly string _before;

    public ContentTreeGuardFixture()
    {
        _contentDir = FindRealContentDir();
        _before = Fingerprint(_contentDir);
    }

    public void Dispose()
    {
        var after = Fingerprint(_contentDir);
        Assert.True(_before == after,
            "The real reference-game content tree changed during the guarded editor suites — test isolation " +
            "regressed (a spawned editor process resolved the REAL project root and wrote it, instead of the " +
            $"isolated MONODREAMS_PROJECT_ROOT temp tree GameTestRunner pins).\nContent dir: {_contentDir}\n" +
            $"BEFORE:\n{_before}\nAFTER:\n{after}");
    }

    /// <summary>Walks up from the test assembly base directory to the repo, returning the real
    /// <c>MonoDreams.Examples.Core/Content</c> directory (where the user's committed .mdscene / .mgcb live).</summary>
    internal static string FindRealContentDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "MonoDreams.Examples.Core", "Content");
            if (Directory.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException(
            "Could not find MonoDreams.Examples.Core/Content (the real content tree) to guard.");
    }

    /// <summary>A deterministic fingerprint: <c>Content.mgcb</c> plus every file under <c>Levels/</c> and
    /// <c>Prefabs/</c> (if present), each as <c>relativePath = sha256</c>, sorted — so any add / delete /
    /// content change shows as a diff.</summary>
    private static string Fingerprint(string contentDir)
    {
        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);

        var mgcb = Path.Combine(contentDir, "Content.mgcb");
        if (File.Exists(mgcb)) entries["Content.mgcb"] = Hash(mgcb);

        foreach (var sub in new[] { "Levels", "Prefabs" })
        {
            var subDir = Path.Combine(contentDir, sub);
            if (!Directory.Exists(subDir)) continue;
            foreach (var file in Directory.EnumerateFiles(subDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(contentDir, file).Replace('\\', '/');
                entries[rel] = Hash(file);
            }
        }

        return string.Join("\n", entries.Select(kv => $"{kv.Key} = {kv.Value}"));
    }

    private static string Hash(string path)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}

/// <summary>The collection binding <see cref="ContentTreeGuardFixture"/> around the editor-enabled
/// spawned-process suites so every one of their runs is bracketed by the real-tree integrity check.</summary>
[CollectionDefinition(Name)]
public sealed class ContentTreeGuardCollection : ICollectionFixture<ContentTreeGuardFixture>
{
    public const string Name = "ContentTreeGuard (editor-enabled spawned runs — real content tree tripwire)";
}
