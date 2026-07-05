using System.Collections.Generic;
using System.IO;
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
}
