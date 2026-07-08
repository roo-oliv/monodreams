using System;
using System.IO;
using DefaultEcs;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the shared source-first optional scene load (UX-C §3.1,
/// <see cref="NativeLevelLoader.TryPublishSceneLoad"/>) a bound screen calls in <c>Load</c> — and the
/// same probe UX-D will reuse for Restart. Source-first (resolved + source exists → <c>fromContent:false</c>),
/// then the bundled <c>TitleContainer</c> probe (<c>fromContent:true</c>), then a silent no-op.
/// Existence probes are injected, so no real filesystem / TitleContainer.
/// </summary>
public class OptionalSceneLoadTests
{
    // A resolved context rooted at /proj (env var), mirroring EditorDialogTests.ResolvedContext.
    private static EditorProjectContext ResolvedContext()
    {
        const string root = "/proj";
        var manifestPath = Path.Combine(root, "Content", GameProject.FileName);
        var manifestJson = CanonicalJson.Serialize(new GameProject { StartScene = "island" });
        return EditorProjectContext.Resolve(
            baseDirectory: Path.Combine("/somewhere", "bin") + Path.DirectorySeparatorChar,
            getEnvironmentVariable: name => name == EditorProjectContext.ProjectRootVariable ? root : null,
            fileExists: p => p == manifestPath,
            readAllText: _ => manifestJson);
    }

    private static LoadSceneRequest? Capture(World world)
    {
        LoadSceneRequest? captured = null;
        world.Subscribe((in LoadSceneRequest r) => captured = r);
        return captured;
    }

    [Fact]
    public void SourceFirst_WhenResolvedAndSourceExists_PublishesSourceLoad_EvenIfBundledExists()
    {
        using var world = new World();
        LoadSceneRequest? req = null;
        world.Subscribe((in LoadSceneRequest r) => req = r);
        var ctx = ResolvedContext();
        var sourcePath = Path.Combine(ctx.LevelsPath!, "island" + SceneWriter.SceneFileExtension);

        var published = NativeLevelLoader.TryPublishSceneLoad(world, "Content", "island", ctx,
            sourceExists: p => p == sourcePath, bundledExists: _ => true);

        Assert.True(published);
        Assert.Equal(sourcePath, req!.Value.Path);
        Assert.False(req.Value.FromContent); // the source tree wins over the (stale) bundled copy
    }

    [Fact]
    public void Bundled_WhenNoSource_PublishesBundledLoad()
    {
        using var world = new World();
        LoadSceneRequest? req = null;
        world.Subscribe((in LoadSceneRequest r) => req = r);

        var published = NativeLevelLoader.TryPublishSceneLoad(world, "Content", "island", ResolvedContext(),
            sourceExists: _ => false, bundledExists: _ => true);

        Assert.True(published);
        Assert.Equal(NativeLevelLoader.ContentRelativePath("island"), req!.Value.Path);
        Assert.True(req.Value.FromContent);
    }

    [Fact]
    public void Absent_IsSilentNoOp()
    {
        using var world = new World();
        var count = 0;
        world.Subscribe((in LoadSceneRequest _) => count++);

        var published = NativeLevelLoader.TryPublishSceneLoad(world, "Content", "island", ResolvedContext(),
            sourceExists: _ => false, bundledExists: _ => false);

        Assert.False(published);
        Assert.Equal(0, count);
    }

    [Fact]
    public void UnresolvedContext_NeverProbesSource_FallsToBundled()
    {
        using var world = new World();
        LoadSceneRequest? req = null;
        world.Subscribe((in LoadSceneRequest r) => req = r);

        var published = NativeLevelLoader.TryPublishSceneLoad(world, "Content", "island", projectContext: null,
            sourceExists: _ => throw new InvalidOperationException("source must not be probed when unresolved"),
            bundledExists: _ => true);

        Assert.True(published);
        Assert.True(req!.Value.FromContent);
    }

    [Fact]
    public void EmptySceneId_IsNoOp()
    {
        using var world = new World();
        var count = 0;
        world.Subscribe((in LoadSceneRequest _) => count++);
        Assert.False(NativeLevelLoader.TryPublishSceneLoad(world, "Content", "", ResolvedContext(),
            sourceExists: _ => true, bundledExists: _ => true));
        Assert.Equal(0, count);
    }
}
