using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.Extension;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.Platform;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects PS1: <b>canonical, byte-stable scene serialization + stable scene-local ids</b>. The
/// canonical writer (<see cref="CanonicalJson"/>) must produce deterministic bytes so a
/// <c>.mdscene</c> diff is meaningful and a git merge is tractable — the precondition for versioning
/// levels. Pure logic — hand-built entities, no real disk, no live <c>GraphicsDevice</c>.
///
/// Covers the level-editor premise "The scene serializer is canonical and byte-stable; entities[] is
/// ordered by a persisted stable scene-local id" via the named tests below.
///
/// Touches the process-global <see cref="PlatformServices.Current"/> / <see cref="Logger"/> (the
/// reload test), so this class is in the non-parallel collection and restores the defaults.
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class SceneCanonicalSerializationTests
{
    private const string SceneFileName = "canonical.scene.json";

    private static ComponentSerializerRegistry NewRegistry()
    {
        var r = new ComponentSerializerRegistry();
        r.RegisterEngineComponents();
        return r;
    }

    private static SceneWriter NewWriter() => new(new SceneSerializer(NewRegistry()));

    /// <summary>A minimal save-root sprite entity (tagged), positioned at <paramref name="pos"/>.</summary>
    private static Entity MakeRoot(World w, string name, Vector2 pos, float layerDepth = 0.5f)
    {
        var e = w.CreateEntity();
        e.Set(new SceneObjectComponent());
        e.Set(new EntityInfoComponent("Prop", name));
        e.Set(new TransformComponent(pos, rotation: 0.1f, scale: new Vector2(1.5f, 1.5f)));
        e.Set(new SpriteInfoComponent
        {
            AssetKey = "Atlas/" + name,
            Source = new Rectangle(0, 0, 16, 16),
            Size = new Vector2(16, 16),
            Color = Color.White,
            Target = RenderTargetID.Main,
            LayerDepth = layerDepth,
        });
        return e;
    }

    // ---- Serialize the same world twice → identical bytes ----

    [Fact]
    public void Serialize_SameWorldTwice_IsByteIdentical()
    {
        using var world = new World();
        MakeRoot(world, "a", new Vector2(10, 20));
        MakeRoot(world, "b", new Vector2(30, 40));
        MakeRoot(world, "c", new Vector2(50, 60));

        var writer = NewWriter();
        var json1 = CanonicalJson.Serialize(writer.BuildScene(world));
        var json2 = CanonicalJson.Serialize(writer.BuildScene(world));

        Assert.Equal(json1, json2); // deterministic across calls; the first call's stable-id stamp sticks
        Assert.EndsWith("}\n", json1); // trailing-newline policy
    }

    // ---- Component-map keys serialize in ordinal-sorted order (deterministic, not storage order) ----

    [Fact]
    public void ComponentMapKeys_AreOrdinalSorted()
    {
        using var world = new World();
        var e = MakeRoot(world, "a", new Vector2(1, 2));
        // Add more components so the map has several keys whose live storage order is unspecified.
        e.Set(new BoxColliderComponent(new Vector2(8, 8), new HashSet<int> { 3, 1, 2 }, passive: true));

        var json = CanonicalJson.Serialize(NewWriter().BuildScene(world));

        // The known keys must appear in the file in ordinal order.
        var expectedOrder = new[]
        {
            EngineComponentSerializers.BoxColliderKey, // core.BoxCollider
            EngineComponentSerializers.EntityInfoKey,  // core.EntityInfo
            EngineComponentSerializers.SpriteInfoKey,  // core.SpriteInfo
            EngineComponentSerializers.TransformKey,   // core.Transform
        };
        Assert.Equal(expectedOrder, expectedOrder.OrderBy(k => k, StringComparer.Ordinal).ToArray()); // sanity: my expectation is sorted

        var positions = expectedOrder.Select(k => json.IndexOf("\"" + k + "\"", StringComparison.Ordinal)).ToArray();
        Assert.All(positions, p => Assert.True(p >= 0));
        for (var i = 1; i < positions.Length; i++)
            Assert.True(positions[i - 1] < positions[i], $"component key {expectedOrder[i]} must follow {expectedOrder[i - 1]}");

        // And the HashSet-sourced activeLayers is emitted ascending (a set has no stable order).
        var al = json.IndexOf("\"activeLayers\"", StringComparison.Ordinal);
        Assert.True(al >= 0);
        var window = json.Substring(al, Math.Min(64, json.Length - al));
        int p1 = window.IndexOf('1'), p2 = window.IndexOf('2'), p3 = window.IndexOf('3');
        Assert.True(p1 >= 0 && p1 < p2 && p2 < p3, $"activeLayers must serialize as 1,2,3 ascending: {window}");
    }

    // ---- Different insertion order + the same stable ids → identical bytes (order-independence) ----

    [Fact]
    public void DifferentInsertionOrder_SameStableIds_IsByteIdentical()
    {
        string Build(bool reversed)
        {
            using var world = new World();
            if (!reversed)
            {
                var a = MakeRoot(world, "a", new Vector2(1, 1)); a.Set(new SceneEntityIdComponent(5));
                var b = MakeRoot(world, "b", new Vector2(2, 2)); b.Set(new SceneEntityIdComponent(3));
            }
            else
            {
                var b = MakeRoot(world, "b", new Vector2(2, 2)); b.Set(new SceneEntityIdComponent(3));
                var a = MakeRoot(world, "a", new Vector2(1, 1)); a.Set(new SceneEntityIdComponent(5));
            }
            return CanonicalJson.Serialize(NewWriter().BuildScene(world));
        }

        var forward = Build(reversed: false);
        var reversed = Build(reversed: true);

        Assert.Equal(forward, reversed); // ordered by stable id, not by creation order
        // And "b" (id 3) precedes "a" (id 5) in both.
        Assert.True(forward.IndexOf("\"b\"", StringComparison.Ordinal) <
                    forward.IndexOf("\"a\"", StringComparison.Ordinal));
    }

    // ---- Moving one entity's transform → the diff touches only that entity's position lines ----

    [Fact]
    public void MovingOneEntity_TouchesOnlyThatEntitysLines()
    {
        using var world = new World();
        MakeRoot(world, "a", new Vector2(10, 20));
        var b = MakeRoot(world, "b", new Vector2(30, 40));
        MakeRoot(world, "c", new Vector2(50, 60));

        var writer = NewWriter();
        var before = CanonicalJson.Serialize(writer.BuildScene(world)); // stamps stable ids
        b.Set(new TransformComponent(new Vector2(999, 111), rotation: 0.1f, scale: new Vector2(1.5f, 1.5f)));
        var after = CanonicalJson.Serialize(writer.BuildScene(world));

        var lines1 = before.Split('\n');
        var lines2 = after.Split('\n');
        Assert.Equal(lines1.Length, lines2.Length); // structure unchanged (id ordering stable → no reshuffle)

        var diff = Enumerable.Range(0, lines1.Length)
            .Where(i => lines1[i] != lines2[i])
            .Select(i => lines2[i].Trim().TrimEnd(','))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        // Exactly the two moved position components changed — nothing else in the file.
        Assert.Equal(new[] { "111", "999" }, diff);
    }

    // ---- Floats are culture-invariant: a comma-decimal CurrentCulture still emits '.' ----

    [Fact]
    public void Floats_UnderNonInvariantCulture_UsePeriodDecimal()
    {
        var previousCulture = Thread.CurrentThread.CurrentCulture;
        var previousUi = Thread.CurrentThread.CurrentUICulture;
        try
        {
            // A locale whose number format uses a comma as the decimal separator.
            var comma = new CultureInfo("de-DE");
            Assert.Equal(",", comma.NumberFormat.NumberDecimalSeparator); // sanity: this culture really uses ','
            Thread.CurrentThread.CurrentCulture = comma;
            Thread.CurrentThread.CurrentUICulture = comma;

            using var world = new World();
            MakeRoot(world, "a", new Vector2(0, 0)); // rotation 0.1, scale [1.5, 1.5]

            var json = CanonicalJson.Serialize(NewWriter().BuildScene(world));

            Assert.Contains("0.1", json);   // period decimals, invariant
            Assert.Contains("1.5", json);
            Assert.DoesNotContain("0,1", json); // never the locale comma
            Assert.DoesNotContain("1,5", json);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previousCulture;
            Thread.CurrentThread.CurrentUICulture = previousUi;
        }
    }

    // ---- Stable ids: assigned monotonically at first serialization, preserved across load → save ----

    [Fact]
    public void StableIds_AssignedMonotonically_AndPreservedAcrossReload()
    {
        var fake = new InMemoryPlatform();
        WithPlatform(fake, () =>
        {
            // ---- BUILD + SAVE #1 (first serialization assigns 0,1,2 in creation order) ----
            using var world1 = new World();
            MakeRoot(world1, "a", new Vector2(10, 20));
            MakeRoot(world1, "b", new Vector2(30, 40));
            MakeRoot(world1, "c", new Vector2(50, 60));

            var writer1 = NewWriter();
            var scene1 = writer1.BuildScene(world1);
            fake.Files[SceneFileName] = CanonicalJson.Serialize(scene1);

            // Every root got a stable id; they are 0,1,2 (monotonic, creation order).
            var ids1 = scene1.Entities.Where(e => e.Id.HasValue).Select(e => e.Id!.Value).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { 0, 1, 2 }, ids1);

            // ---- RELOAD onto a fresh world ----
            using var world2 = new World();
            using var reader = new SceneReaderSystem(world2, new SceneSerializer(NewRegistry()),
                content: null, loadTexture: _ => null);
            world2.Publish(new LoadSceneRequest(SceneFileName, fromContent: false));

            // Each reconstructed root carries its restored stable id.
            var reloadedIds = new List<int>();
            using (var set = world2.GetEntities().With<SceneEntityIdComponent>().AsSet())
                foreach (var e in set.GetEntities()) reloadedIds.Add(e.Get<SceneEntityIdComponent>().Id);
            Assert.Equal(new[] { 0, 1, 2 }, reloadedIds.OrderBy(x => x).ToArray());

            // ---- SAVE #2 (no edit) == source bytes: the fixed point at the BYTE level ----
            var writer2 = NewWriter();
            var json2 = CanonicalJson.Serialize(writer2.BuildScene(world2));
            Assert.Equal(fake.Files[SceneFileName], json2);
        });
    }

    // ---- A brand-new root added after a load gets the next free id (max present + 1) ----

    [Fact]
    public void NewRootAfterLoad_GetsNextFreeStableId()
    {
        var fake = new InMemoryPlatform();
        WithPlatform(fake, () =>
        {
            using var world1 = new World();
            MakeRoot(world1, "a", new Vector2(1, 1));
            MakeRoot(world1, "b", new Vector2(2, 2));
            fake.Files[SceneFileName] = CanonicalJson.Serialize(NewWriter().BuildScene(world1)); // ids 0,1

            using var world2 = new World();
            using var reader = new SceneReaderSystem(world2, new SceneSerializer(NewRegistry()),
                content: null, loadTexture: _ => null);
            world2.Publish(new LoadSceneRequest(SceneFileName, fromContent: false));

            // A designer places a new root; it must get id 2 (max present 1 + 1), never collide with 0/1.
            MakeRoot(world2, "c", new Vector2(3, 3));
            var scene2 = NewWriter().BuildScene(world2);

            var idByName = scene2.Entities
                .Where(e => e.Id.HasValue)
                .ToDictionary(
                    e => e.Components[EngineComponentSerializers.EntityInfoKey].GetProperty("name").GetString()!,
                    e => e.Id!.Value);
            Assert.Equal(2, idByName["c"]);
            Assert.Equal(3, idByName.Count);
            Assert.Equal(new[] { 0, 1, 2 }, idByName.Values.OrderBy(x => x).ToArray()); // no collision
        });
    }

    // ---- Helpers: in-memory platform + isolation ----

    private static void WithPlatform(InMemoryPlatform fake, Action body)
    {
        var previous = PlatformServices.Current;
        try { PlatformServices.Current = fake; body(); }
        finally { PlatformServices.Current = previous; }
    }

    /// <summary>In-memory platform: ExportScene / ReadAllText share a dictionary, so a writer→reader
    /// hop is a real serialize/deserialize with no disk.</summary>
    private sealed class InMemoryPlatform : IPlatformServices
    {
        public Dictionary<string, string> Files { get; } = new();
        public StringWriter LogWriter { get; } = new();
        public string BaseDirectory => "/scene/";
        public string GetEnvironmentVariable(string name) => null;
        public string CombinePath(params string[] paths) => string.Join("/", paths);
        public bool FileExists(string path) => Files.ContainsKey(path);
        public string ReadAllText(string path) =>
            Files.TryGetValue(path, out var v) ? v : throw new FileNotFoundException(path);
        public void WriteAllText(string path, string contents) => Files[path] = contents;
        public void WriteAllBytes(string path, byte[] bytes) { }
        public string ExportScene(string suggestedFileName, string contents)
        {
            Files[suggestedFileName] = contents;
            return suggestedFileName;
        }
        public void CreateDirectory(string path) { }
        public TextWriter OpenLogWriter(string directory, string fileName) => LogWriter;
        public void WriteLineToConsole(string line) { }
        public void RunBackground(Action work) => work();
    }
}
