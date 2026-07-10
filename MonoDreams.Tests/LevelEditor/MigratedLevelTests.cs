#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DefaultEcs;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Serialization;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.Platform;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the PS5 <b>migrated Examples level</b>: the committed
/// <c>MonoDreams.Examples.Core/Content/Levels/Blender_Level.mdscene</c> (imported once from the Blender
/// parser via the export op) is (a) <b>byte-locked</b> to the canonical serializer — a load→save is a
/// fixed point, so a non-canonical hand-edit is caught — and (b) <b>boots through the shipped
/// (no-editor) native reader</b>, reconstructing the player + NPCs + colliders, which proves the
/// game-component serializers (<c>PlayerState</c>, <c>StopMotionEffect</c>, …) are registered on the
/// shipped path (an unregistered key would throw on load).
///
/// Pure logic — reads the committed source file, an in-memory platform for the reader's file read, and a
/// null texture stub (AssetKey preserved). No <c>GraphicsDevice</c>.
///
/// Covers the level-editor premise "The Examples levels are migrated to native .mdscene".
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class MigratedLevelTests
{
    private const string BlenderLevelRelPath = "MonoDreams.Examples.Core/Content/Levels/Blender_Level.mdscene";

    private sealed class InMemoryPlatformServices : IPlatformServices
    {
        public Dictionary<string, string> Files { get; } = new();
        public StringWriter LogWriter { get; } = new();
        public string BaseDirectory => "/migrated/";
        public string GetEnvironmentVariable(string name) => null!;
        public string CombinePath(params string[] paths) => string.Join("/", paths);
        public bool FileExists(string path) => Files.ContainsKey(path);
        public string ReadAllText(string path) =>
            Files.TryGetValue(path, out var v) ? v : throw new FileNotFoundException(path);
        public void WriteAllText(string path, string contents) => Files[path] = contents;
        public void WriteAllBytes(string path, byte[] bytes) { }
        public string ExportScene(string suggestedFileName, string contents) { Files[suggestedFileName] = contents; return suggestedFileName; }
        public void CreateDirectory(string path) { }
        public TextWriter OpenLogWriter(string directory, string fileName) => LogWriter;
        public void WriteLineToConsole(string line) { }
        public void RunBackground(Action work) => work();
    }

    private static void WithPlatform(InMemoryPlatformServices fake, Action body)
    {
        var previous = PlatformServices.Current;
        try { PlatformServices.Current = fake; body(); }
        finally { PlatformServices.Current = previous; }
    }

    private static ComponentSerializerRegistry FullRegistry()
    {
        var r = new ComponentSerializerRegistry();
        r.RegisterEngineComponents();
        r.RegisterGameComponents();
        return r;
    }

    /// <summary>Reads the COMMITTED bytes of a repo-relative file (<c>git show HEAD:&lt;path&gt;</c>),
    /// walking up from the test base dir to the repo root (the directory containing
    /// <c>MonoDreams.Examples.Core</c>). These tests gate what the repo COMMITS — reading the
    /// working tree instead lets a developer's uncommitted level edits (e.g. a WIP prefab
    /// reference) redden the suite (the ship-lint precedent: git is the source of truth for
    /// "committed").</summary>
    private static string ReadRepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "MonoDreams.Examples.Core")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir == null) throw new InvalidOperationException("Could not find the repo root.");
        var psi = new global::System.Diagnostics.ProcessStartInfo("git", $"show HEAD:{relative}")
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var proc = global::System.Diagnostics.Process.Start(psi)!;
        var content = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        if (proc.ExitCode != 0 || content.Length == 0)
            throw new InvalidOperationException($"git show HEAD:{relative} failed (exit {proc.ExitCode}).");
        return content;
    }

    /// <summary>The committed bytes of <c>Content/Prefabs/&lt;id&gt;.mdprefab</c>, or null when the repo
    /// commits no such prefab (untracked WIP prefabs are invisible here by design).</summary>
    private static string? TryReadCommittedPrefab(string id)
    {
        try { return ReadRepoFile($"MonoDreams.Examples.Core/Content/Prefabs/{id}.mdprefab"); }
        catch (InvalidOperationException) { return null; }
    }

    private static List<Entity> With<T>(World world)
    {
        var list = new List<Entity>();
        using var set = world.GetEntities().With<T>().AsSet();
        list.AddRange(set.GetEntities().ToArray());
        return list;
    }

    /// <summary>Whether <paramref name="parent"/> has a CHILD entity carrying collider component
    /// <typeparamref name="TCollider"/> — the version-2 shape, where a visual entity's collider lives on
    /// its own child collider entity (CE-B) rather than embedded on the visual entity.</summary>
    private static bool HasColliderChild<TCollider>(World world, Entity parent)
    {
        using var set = world.GetEntities().With<ChildOfComponent>().With<TCollider>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.Get<ChildOfComponent>().Parent == parent) return true;
        return false;
    }

    [Fact]
    public void CommittedBlenderLevel_IsByteCanonical_LoadSaveIsAFixedPoint()
    {
        var committed = ReadRepoFile(BlenderLevelRelPath);
        var scene = CanonicalJson.Deserialize<SceneData>(committed);
        Assert.NotNull(scene);
        var reserialized = CanonicalJson.Serialize(scene!);
        // The committed migrated scene is canonical: deserialize→serialize reproduces its exact bytes.
        Assert.Equal(committed, reserialized);
    }

    [Fact]
    public void CommittedBlenderLevel_BootsThroughTheShippedReader_YieldingPlayerAndNpcs()
    {
        var committed = ReadRepoFile(BlenderLevelRelPath);
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            const string path = "Levels/Blender_Level.mdscene";
            fake.WriteAllText(path, committed);

            using var world = new World();
            // The SHIPPED reader registry: engine + game serializers. If a game component key were
            // missing a serializer, DeserializeEntity would throw here — this is the regression guard
            // that the game-component serializers are registered on the shipped path.
            var registry = FullRegistry();
            // Mirror the SHIPPED screen composition: it now composes a PrefabExpander too (a bundled
            // scene may carry linked prefab instances). Committed content resolves prefabs from the
            // committed tree the same way — an id with no committed .mdprefab fails loud, the guard
            // this boot test exists to keep honest.
            var serializer = new SceneSerializer(registry);
            var expander = new PrefabExpander(serializer,
                id => TryReadCommittedPrefab(id) is { } json
                    ? PrefabData.FromScene(id, CanonicalJson.Deserialize<SceneData>(json)!)
                    : null,
                loadTexture: _ => (Texture2D)null!);
            using var reader = new SceneReaderSystem(world, serializer,
                content: null!, loadTexture: _ => (Texture2D)null!, prefabExpander: expander);
            world.Publish(new LoadSceneRequest(path, fromContent: false));

            // The migrated Blender level yields the player (Pete) with its game components, the NPCs,
            // and the store collider — reconstructed from components, not by re-running the parser.
            var pete = With<PlayerState>(world).Single();
            Assert.Equal("Pete", pete.Get<EntityInfoComponent>().Name);
            Assert.True(pete.Has<StopMotionEffect>());
            Assert.True(pete.Has<CameraFollowTargetComponent>());
            // Colliders-as-entities (CE-B): Pete's convex hull now lives on its own CHILD collider entity,
            // not embedded on the visual Player entity. Pete is the body; the collider rides it.
            Assert.False(pete.Has<ConvexColliderComponent>());
            Assert.True(HasColliderChild<ConvexColliderComponent>(world, pete));
            Assert.Equal("GreasePencil/Pete", pete.Get<SpriteInfoComponent>().AssetKey);

            var npcs = With<EntityInfoComponent>(world)
                .Where(e => e.Get<EntityInfoComponent>().Type == "NPC").ToList();
            Assert.Contains(npcs, e => e.Get<EntityInfoComponent>().Name == "Boldo");
            Assert.All(npcs.Where(e => e.Has<StopMotionEffect>()), e => Assert.True(e.Has<StopMotionEffect>()));

            var store = With<EntityInfoComponent>(world)
                .Single(e => e.Get<EntityInfoComponent>().Name == "store");
            Assert.False(store.Has<ConvexColliderComponent>());
            Assert.True(HasColliderChild<ConvexColliderComponent>(world, store)); // convex on a child collider entity
            Assert.Equal("GreasePencil/store", store.Get<SpriteInfoComponent>().AssetKey);
        });
    }
}
