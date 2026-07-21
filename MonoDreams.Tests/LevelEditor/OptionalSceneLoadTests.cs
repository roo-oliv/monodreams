using System;
using System.IO;
using System.Linq;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
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
    public void Absent_PublishesNoLoad_ButEnsuresExactlyOneCameraEntity()
    {
        using var world = new World();
        var count = 0;
        world.Subscribe((in LoadSceneRequest _) => count++);

        var published = NativeLevelLoader.TryPublishSceneLoad(world, "Content", "island", ResolvedContext(),
            sourceExists: _ => false, bundledExists: _ => false);

        Assert.False(published); // no scene file loaded → the screen keeps its code-built content
        Assert.Equal(0, count);  // no LoadSceneRequest published — the reader never runs on the absent path

        // CM-D: the file-absent branch runs the SAME ensure-one-camera the reader runs, so a code-built
        // scene context that never loads a file (LevelSelection's level_selection, every Demos screen) is
        // not left camera-less / non-uniform. The default camera lands at the origin (no scene content to
        // frame on) and is tree-visible-shaped: SceneObjectComponent-tagged + EntityInfo "Camera".
        using var cams = world.GetEntities().With<CameraComponent>().AsSet();
        var created = cams.GetEntities().ToArray();
        Assert.Single(created);
        Assert.True(created[0].Has<SceneObjectComponent>());                 // shows in the tree + saves
        Assert.Equal("Camera", created[0].Get<EntityInfoComponent>().Type);  // the "Camera" tree row
        Assert.Equal(Vector2.Zero, created[0].Get<TransformComponent>().Position);
    }

    [Fact]
    public void AbsentEnsure_IsIdempotent_WhenTheAbsentBranchRunsAgain()
    {
        // Models a Restart / repeated Load re-running the optional load over a world that already has a
        // camera (the ensure guard is what converges the sweep + re-run on exactly one — CM pre-mortem #3).
        using var world = new World();
        Func<string, bool> absent = _ => false;

        NativeLevelLoader.TryPublishSceneLoad(world, "Content", "island", ResolvedContext(),
            sourceExists: absent, bundledExists: absent);
        NativeLevelLoader.TryPublishSceneLoad(world, "Content", "island", ResolvedContext(),
            sourceExists: absent, bundledExists: absent);

        using var cams = world.GetEntities().With<CameraComponent>().AsSet();
        Assert.Single(cams.GetEntities().ToArray()); // the second ensure no-ops — still exactly one
    }

    [Fact]
    public void AbsentEnsure_ThenARealLoadThroughTheReader_DoesNotDoubleTheCamera()
    {
        // Optional-load-later: the scene is absent at first (absent branch ensures a camera), then a later
        // real load comes through the composed reader — whose ensure sees the existing camera and no-ops.
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        var serializer = new SceneSerializer(registry);

        using var world = new World();
        using var reader = new SceneReaderSystem(world, serializer, content: null!,
            loadTexture: _ => null!, ensureSingleCamera: true);

        NativeLevelLoader.TryPublishSceneLoad(world, "Content", "island", ResolvedContext(),
            sourceExists: _ => false, bundledExists: _ => false);

        world.Publish(new LoadSceneRequest(new SceneData())); // a later camera-less load through the reader

        using var cams = world.GetEntities().With<CameraComponent>().AsSet();
        Assert.Single(cams.GetEntities().ToArray());
    }

    [Fact]
    public void AbsentEnsuredCamera_FirstSavePersistsIt_AsV3()
    {
        // The first Save of a never-loaded scene context persists the ensured camera as an ordinary v3
        // scene entity (CM one-data-model: if the Inspector shows it, Save persists it).
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        var serializer = new SceneSerializer(registry);

        using var world = new World();
        NativeLevelLoader.TryPublishSceneLoad(world, "Content", "island", ResolvedContext(),
            sourceExists: _ => false, bundledExists: _ => false);

        var scene = new SceneWriter(serializer).BuildScene(world);

        Assert.Equal(3, scene.Version);
        Assert.Single(scene.Entities, e => e.Components.ContainsKey(EngineComponentSerializers.CameraKey));
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
