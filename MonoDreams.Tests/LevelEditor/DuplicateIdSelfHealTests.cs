using System.Collections.Generic;
using System.Linq;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// PF-F duplicate-stable-id self-healing. A corrupt/double-loaded scene (the user's <c>island2</c>: two
/// roots sharing a stable id) must LOAD and self-heal — the reader re-stamps later duplicates in-world,
/// and the writer re-stamps them on save — so the scene becomes byte-stable again (colliding ids never
/// persist across a re-save). Protects the "ids are unique, monotonic, preserved" stable-id premise.
/// Pure logic — hand-built entities, the in-memory reader path (no disk).
/// </summary>
public class DuplicateIdSelfHealTests
{
    private static ComponentSerializerRegistry NewRegistry()
    {
        var r = new ComponentSerializerRegistry();
        r.RegisterEngineComponents();
        return r;
    }

    private static Entity MakeRoot(World w, string name, Vector2 pos)
    {
        var e = w.CreateEntity();
        e.Set(new SceneObjectComponent());
        e.Set(new EntityInfoComponent("Prop", name));
        e.Set(new TransformComponent(pos));
        return e;
    }

    // ── Writer guard: two roots sharing an id → the later one is re-stamped on save ─────────────────

    [Fact]
    public void Writer_TwoRootsShareAnId_ReStampsTheLaterOne_AndConvergesByteStable()
    {
        var serializer = new SceneSerializer(NewRegistry());
        using var world = new World();
        var a = MakeRoot(world, "A", new Vector2(10, 10));
        var b = MakeRoot(world, "B", new Vector2(20, 20));
        // Force the collision an in-world double-load would leave.
        a.Set(new SceneEntityIdComponent(3));
        b.Set(new SceneEntityIdComponent(3));

        var writer = new SceneWriter(serializer);
        var scene = writer.BuildScene(world);

        Assert.Equal(1, writer.LastBuildDuplicateIdRestamps);
        var rootIds = scene.Entities.Where(e => e.Parent == null).Select(e => e.Id).ToList();
        Assert.Equal(2, rootIds.Count);
        Assert.Equal(2, rootIds.Distinct().Count()); // no longer colliding

        // The heal stuck to the live world → a second build finds nothing to fix and is byte-stable.
        var scene2 = writer.BuildScene(world);
        Assert.Equal(0, writer.LastBuildDuplicateIdRestamps);
        Assert.Equal(CanonicalJson.Serialize(scene), CanonicalJson.Serialize(scene2));
    }

    // ── Reader guard: a FILE with duplicate ids loads + self-heals in-world ──────────────────────────

    [Fact]
    public void Reader_DuplicateIdsInFile_LoadSucceeds_LaterDuplicateReStampedInWorld()
    {
        var serializer = new SceneSerializer(NewRegistry());

        // Hand-build a corrupt SceneData: two roots BOTH id=3 (the island2 shape). The low-level
        // serializer does not stamp ids, so we force the collision directly.
        SceneData corrupt;
        using (var src = new World())
        {
            var a = MakeRoot(src, "A", new Vector2(10, 10));
            var b = MakeRoot(src, "B", new Vector2(20, 20));
            corrupt = serializer.Serialize(new List<Entity> { a, b });
        }
        Assert.Null(corrupt.Entities[0].Parent);
        Assert.Null(corrupt.Entities[1].Parent);
        corrupt.Entities[0].Id = 3;
        corrupt.Entities[1].Id = 3;

        using var world = new World();
        using var reader = new SceneReaderSystem(world, serializer, content: null, loadTexture: _ => null);
        world.Publish(new LoadSceneRequest(corrupt)); // in-memory restore — no disk

        // Both roots exist, with DISTINCT ids now (the later duplicate was re-stamped).
        var ids = new List<int>();
        using (var set = world.GetEntities().With<SceneObjectComponent>().With<SceneEntityIdComponent>().AsSet())
            foreach (var e in set.GetEntities())
                ids.Add(e.Get<SceneEntityIdComponent>().Id);
        Assert.Equal(2, ids.Count);
        Assert.Equal(2, ids.Distinct().Count());
        Assert.Contains(3, ids); // the first duplicate kept its id

        // A save now writes distinct ids and re-saving is byte-stable (the corruption is healed).
        var writer = new SceneWriter(serializer);
        var healed = writer.BuildScene(world);
        Assert.Equal(0, writer.LastBuildDuplicateIdRestamps);
        var savedIds = healed.Entities.Where(e => e.Parent == null).Select(e => e.Id).ToList();
        Assert.Equal(2, savedIds.Distinct().Count());
    }
}
