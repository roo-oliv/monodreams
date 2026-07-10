using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Serialization;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the PS2 project-root resolution (<see cref="EditorProjectContext.Resolve(string,System.Func{string,string?},System.Func{string,bool},System.Func{string,string},int)"/>):
/// env-var primary, walk-up fallback, and fail-safe unresolved — never throwing. The environment and
/// filesystem lookups are injected, so these are pure with a simulated layout (no real disk). The
/// resolved <b>project root is the directory that contains the manifest</b> (uniform across the
/// env-var and walk-up cases), which is what PS3's write path leans on.
/// </summary>
public class EditorProjectContextTests
{
    private static string Manifest(string startScene = "island", string levelsDir = "Levels") =>
        CanonicalJson.Serialize(new GameProject { StartScene = startScene, LevelsDir = levelsDir, AssetRoots = new[] { "Island" } });

    // ---- PRIMARY: the env var ----

    [Fact]
    public void EnvVar_ResolvesToThatRoot_AndLoadsTheManifestUnderContent()
    {
        var root = Path.Combine("/games", "MyGame");
        var manifestPath = Path.Combine(root, "Content", GameProject.FileName);
        var files = new Dictionary<string, string> { [manifestPath] = Manifest(levelsDir: "Levels") };

        var ctx = EditorProjectContext.Resolve(
            baseDirectory: Path.Combine("/games", "MyGame", "bin", "Debug") + Path.DirectorySeparatorChar,
            getEnvironmentVariable: n => n == EditorProjectContext.ProjectRootVariable ? root : null,
            fileExists: files.ContainsKey,
            readAllText: p => files[p]);

        Assert.True(ctx.Resolved);
        Assert.Equal(Path.Combine(root, "Content"), ctx.ProjectRoot); // root = the manifest's directory
        Assert.Equal(manifestPath, ctx.ManifestPath);
        Assert.Equal("Levels", ctx.LevelsDir);
        Assert.Equal(Path.Combine(root, "Content", "Levels"), ctx.LevelsPath);
        Assert.NotNull(ctx.Manifest);
        Assert.Equal("island", ctx.Manifest!.StartScene);
    }

    [Fact]
    public void EnvVar_ManifestAtBareRoot_AlsoResolves()
    {
        var root = Path.Combine("/games", "MyGame");
        var manifestPath = Path.Combine(root, GameProject.FileName); // bare, not under Content/
        var files = new Dictionary<string, string> { [manifestPath] = Manifest() };

        var ctx = EditorProjectContext.Resolve(
            baseDirectory: Path.Combine("/somewhere", "bin") + Path.DirectorySeparatorChar,
            getEnvironmentVariable: n => n == EditorProjectContext.ProjectRootVariable ? root : null,
            fileExists: files.ContainsKey,
            readAllText: p => files[p]);

        Assert.True(ctx.Resolved);
        Assert.Equal(root, ctx.ProjectRoot);
        Assert.Equal(manifestPath, ctx.ManifestPath);
    }

    // ---- FALLBACK: walk up from the base directory ----

    [Fact]
    public void WalkUp_FindsManifestUnderContent_WhenEnvUnset()
    {
        var projectDir = Path.Combine("/repo", "MyGame.Core");
        var manifestPath = Path.Combine(projectDir, "Content", GameProject.FileName);
        var files = new Dictionary<string, string> { [manifestPath] = Manifest() };
        var baseDir = Path.Combine(projectDir, "bin", "Debug", "net8.0") + Path.DirectorySeparatorChar;

        var ctx = EditorProjectContext.Resolve(baseDir, _ => null, files.ContainsKey, p => files[p]);

        Assert.True(ctx.Resolved);
        Assert.Equal(Path.Combine(projectDir, "Content"), ctx.ProjectRoot);
        Assert.Equal(manifestPath, ctx.ManifestPath);
    }

    [Fact]
    public void WalkUp_FindsBareManifestAtAnAncestor()
    {
        var projectDir = Path.Combine("/repo", "MyGame");
        var manifestPath = Path.Combine(projectDir, GameProject.FileName);
        var files = new Dictionary<string, string> { [manifestPath] = Manifest() };
        var baseDir = Path.Combine(projectDir, "bin", "Debug") + Path.DirectorySeparatorChar;

        var ctx = EditorProjectContext.Resolve(baseDir, _ => null, files.ContainsKey, p => files[p]);

        Assert.True(ctx.Resolved);
        Assert.Equal(projectDir, ctx.ProjectRoot);
    }

    [Fact]
    public void EnvSetButNoManifest_FallsBackToWalkUp()
    {
        var envRoot = Path.Combine("/wrong", "place"); // named, but has no manifest
        var projectDir = Path.Combine("/repo", "MyGame");
        var manifestPath = Path.Combine(projectDir, "Content", GameProject.FileName);
        var files = new Dictionary<string, string> { [manifestPath] = Manifest() };
        var baseDir = Path.Combine(projectDir, "bin") + Path.DirectorySeparatorChar;

        var ctx = EditorProjectContext.Resolve(
            baseDir,
            n => n == EditorProjectContext.ProjectRootVariable ? envRoot : null,
            files.ContainsKey,
            p => files[p]);

        Assert.True(ctx.Resolved);
        Assert.Equal(Path.Combine(projectDir, "Content"), ctx.ProjectRoot);
    }

    // ---- UNRESOLVED (fail-safe, never throws) ----

    [Fact]
    public void NeitherEnvNorWalkUp_IsUnresolved_WithoutThrowing()
    {
        var ctx = EditorProjectContext.Resolve(
            baseDirectory: Path.Combine("/isolated", "bin") + Path.DirectorySeparatorChar,
            getEnvironmentVariable: _ => null,
            fileExists: _ => false,
            readAllText: _ => "");

        Assert.False(ctx.Resolved);
        Assert.Null(ctx.ProjectRoot);
        Assert.Null(ctx.ManifestPath);
        Assert.Null(ctx.Manifest);
        Assert.Equal(GameProject.DefaultLevelsDir, ctx.LevelsDir);
        Assert.Null(ctx.LevelsPath);
    }

    [Fact]
    public void MalformedManifest_IsUnresolved_WithoutThrowing()
    {
        var root = Path.Combine("/games", "MyGame");
        var manifestPath = Path.Combine(root, "Content", GameProject.FileName);

        var ctx = EditorProjectContext.Resolve(
            baseDirectory: Path.Combine("/somewhere", "bin") + Path.DirectorySeparatorChar,
            getEnvironmentVariable: n => n == EditorProjectContext.ProjectRootVariable ? root : null,
            fileExists: p => p == manifestPath,
            readAllText: _ => "{ this is not valid json ]");

        Assert.False(ctx.Resolved); // caught, treated as unresolved — no exception escapes
    }

    // ---- FW1 (BUG #2): the walk-up must NEVER resolve a bin/obj output copy ----

    /// <summary>An in-memory filesystem with the two <c>game.mdproj</c> that a real desktop run sees:
    /// the versioned SOURCE (under the .Core content project) and the MGCB-copied OUTPUT (under the
    /// desktop head's <c>bin/…</c>), plus optional extra manifests and a <c>.git</c> repo-root marker.
    /// Wires the <c>fileExists</c> / <c>directoryExists</c> / recursive <c>enumerateFiles</c> delegates
    /// the corrected <see cref="EditorProjectContext.Resolve"/> needs — no real disk.</summary>
    private sealed class FakeRepo
    {
        private readonly HashSet<string> _manifests = new();
        private readonly HashSet<string> _gitFiles = new();

        public string RepoRoot { get; }

        public FakeRepo(string repoRoot) => RepoRoot = repoRoot;

        public FakeRepo WithManifest(string path) { _manifests.Add(path); return this; }
        public FakeRepo WithGitFile(string path) { _gitFiles.Add(path); return this; }

        public bool FileExists(string p) => _manifests.Contains(p) || _gitFiles.Contains(p);
        public bool DirectoryExists(string _) => false; // .git is a FILE here (a git worktree)

        public IEnumerable<string> EnumerateFiles(string dir, string pattern, bool recursive)
        {
            if (pattern != GameProject.FileName) return Enumerable.Empty<string>(); // no *.sln in this repo
            var prefix = dir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return _manifests.Where(m => recursive
                ? m.StartsWith(prefix, StringComparison.Ordinal)
                : Path.GetDirectoryName(m) == dir.TrimEnd(Path.DirectorySeparatorChar));
        }

        public EditorProjectContext Resolve(string baseDirectory, string? envRoot = null,
            string? preferProjectDirName = null) =>
            EditorProjectContext.Resolve(
                baseDirectory,
                n => n == EditorProjectContext.ProjectRootVariable ? envRoot : null,
                FileExists,
                _ => Manifest(),
                DirectoryExists,
                EnumerateFiles,
                preferProjectDirName);
    }

    [Fact]
    public void WalkUp_FromBinBaseDir_ResolvesTheSourceManifest_NeverTheBinOutputCopy()
    {
        // Layout mirrors the real repo: SOURCE under .Core/Content, an OUTPUT copy under the desktop
        // head's bin/…, a web wwwroot copy, and a .git file at the repo root (worktree).
        var repo = Path.Combine("/repo", "monodreams");
        var source = Path.Combine(repo, "MonoDreams.Examples.Core", "Content", GameProject.FileName);
        var webCopy = Path.Combine(repo, "MonoDreams.Examples.Web", "wwwroot", "Content", GameProject.FileName);
        var binCopy = Path.Combine(repo, "MonoDreams.Examples.Desktop", "bin", "Debug", "net8.0", "Content", GameProject.FileName);
        var binBaseDir = Path.Combine(repo, "MonoDreams.Examples.Desktop", "bin", "Debug", "net8.0") + Path.DirectorySeparatorChar;

        var fs = new FakeRepo(repo)
            .WithManifest(source).WithManifest(webCopy).WithManifest(binCopy)
            .WithGitFile(Path.Combine(repo, ".git"));

        var ctx = fs.Resolve(binBaseDir); // no env var — zero-config in-repo run

        Assert.True(ctx.Resolved);
        Assert.Equal(source, ctx.ManifestPath); // the SOURCE, never the bin copy
        Assert.Equal(Path.Combine(repo, "MonoDreams.Examples.Core", "Content"), ctx.ProjectRoot);
        // Deterministic among source candidates: the shallower .Core/Content beats .Web/wwwroot/Content.
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", ctx.ManifestPath!);
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", ctx.ManifestPath!);
    }

    [Fact]
    public void EnvVar_Wins_EvenWhenABinCopyAndSourceExist()
    {
        var repo = Path.Combine("/repo", "monodreams");
        var source = Path.Combine(repo, "MonoDreams.Examples.Core", "Content", GameProject.FileName);
        var binCopy = Path.Combine(repo, "MonoDreams.Examples.Desktop", "bin", "Debug", "net8.0", "Content", GameProject.FileName);
        var binBaseDir = Path.Combine(repo, "MonoDreams.Examples.Desktop", "bin", "Debug", "net8.0") + Path.DirectorySeparatorChar;

        // The env var names a DIFFERENT explicit root (its own Content/game.mdproj).
        var envRoot = Path.Combine("/elsewhere", "MyGame");
        var envManifest = Path.Combine(envRoot, "Content", GameProject.FileName);

        var fs = new FakeRepo(repo)
            .WithManifest(source).WithManifest(binCopy).WithManifest(envManifest)
            .WithGitFile(Path.Combine(repo, ".git"));

        var ctx = fs.Resolve(binBaseDir, envRoot);

        Assert.True(ctx.Resolved);
        Assert.Equal(envManifest, ctx.ManifestPath); // the explicit override wins over walk-up + repo search
        Assert.Equal(Path.Combine(envRoot, "Content"), ctx.ProjectRoot);
    }

    // ---- TD (multi-manifest disambiguation): two games in one repo, each host resolves ITS OWN manifest ----

    /// <summary>The repo now holds BOTH Examples' and Demos' source manifests at the SAME depth
    /// (<c>.Core/Content</c> and <c>Demos/Content</c>). The Examples HEAD runs from
    /// <c>MonoDreams.Examples.Desktop/bin</c> (content is in a SIBLING <c>.Core</c>, so the walk-up finds
    /// nothing and the repo-root source search runs): without the hint the search tie-breaks to Demos'
    /// manifest on ordinal (D &lt; E) — the WRONG one — but the head's
    /// <c>preferProjectDirName: "MonoDreams.Examples.Core"</c> hint keeps it on Examples' manifest.</summary>
    [Fact]
    public void MultiManifest_ExamplesHost_ResolvesExamplesManifest_ViaHint_NotDemos()
    {
        var repo = Path.Combine("/repo", "monodreams");
        var examples = Path.Combine(repo, "MonoDreams.Examples.Core", "Content", GameProject.FileName);
        var demos = Path.Combine(repo, "MonoDreams.Demos", "Content", GameProject.FileName);
        var examplesBinBaseDir = Path.Combine(repo, "MonoDreams.Examples.Desktop", "bin", "Debug", "net8.0") + Path.DirectorySeparatorChar;

        var fs = new FakeRepo(repo).WithManifest(examples).WithManifest(demos)
            .WithGitFile(Path.Combine(repo, ".git"));

        // WITHOUT the hint the same-depth tie resolves to Demos on ordinal (the regression the hint fixes).
        var noHint = fs.Resolve(examplesBinBaseDir);
        Assert.Equal(demos, noHint.ManifestPath);

        // WITH the Examples head's hint it lands on Examples' manifest (unchanged from before Demos existed).
        var ctx = fs.Resolve(examplesBinBaseDir, preferProjectDirName: "MonoDreams.Examples.Core");
        Assert.True(ctx.Resolved);
        Assert.Equal(examples, ctx.ManifestPath);
        Assert.Equal(Path.Combine(repo, "MonoDreams.Examples.Core", "Content"), ctx.ProjectRoot);
    }

    /// <summary>The Demos HEAD runs from <c>MonoDreams.Demos/bin</c> and its content is CO-LOCATED under
    /// <c>MonoDreams.Demos/Content</c>, so the walk-up resolves Demos' manifest directly — before the
    /// repo-root source search is even reached — even though Examples' manifest also exists. Its
    /// <c>preferProjectDirName: "MonoDreams.Demos"</c> hint is defence-in-depth (moot here).</summary>
    [Fact]
    public void MultiManifest_DemosHost_ResolvesDemosManifest_ViaWalkUp_NotExamples()
    {
        var repo = Path.Combine("/repo", "monodreams");
        var examples = Path.Combine(repo, "MonoDreams.Examples.Core", "Content", GameProject.FileName);
        var demos = Path.Combine(repo, "MonoDreams.Demos", "Content", GameProject.FileName);
        var demosBinBaseDir = Path.Combine(repo, "MonoDreams.Demos", "bin", "Debug", "net8.0") + Path.DirectorySeparatorChar;

        var fs = new FakeRepo(repo).WithManifest(examples).WithManifest(demos)
            .WithGitFile(Path.Combine(repo, ".git"));

        var ctx = fs.Resolve(demosBinBaseDir, preferProjectDirName: "MonoDreams.Demos");

        Assert.True(ctx.Resolved);
        Assert.Equal(demos, ctx.ManifestPath); // Demos', never Examples'
        Assert.Equal(Path.Combine(repo, "MonoDreams.Demos", "Content"), ctx.ProjectRoot);
        Assert.NotEqual(examples, ctx.ManifestPath);
    }

    [Fact]
    public void OnlyABinOutputCopy_AndNoSource_IsUnresolved_NeverTheBinCopy()
    {
        // A relocated/shipped-style layout: the only manifest is the bin output copy, no repo marker.
        var binCopy = Path.Combine("/shipped", "bin", "Debug", "net8.0", "Content", GameProject.FileName);
        var binBaseDir = Path.Combine("/shipped", "bin", "Debug", "net8.0") + Path.DirectorySeparatorChar;

        var fs = new FakeRepo("/shipped").WithManifest(binCopy); // no .git, no source

        var ctx = fs.Resolve(binBaseDir);

        Assert.False(ctx.Resolved); // never resolves the bin copy; Save is disabled with an actionable reason
        Assert.Null(ctx.ProjectRoot);
        Assert.Null(ctx.LevelsPath);
    }
}
