using System;
using System.Collections.Generic;
using ArchEntity = Arch.Core.Entity;
using ArchWorld = Arch.Core.World;

namespace MonoDreams.ArchSpike.FacadeEventsProof;

// ---------------------------------------------------------------------------------------------
// Component shapes. Each one stands for a real engine component, named after it, because the point
// of the proof is fidelity to the sites the migration has to keep working — not coverage of Arch.
// ---------------------------------------------------------------------------------------------

/// <summary>Struct component — the <c>TransformComponent</c> shape.</summary>
internal struct Transform
{
    public float X;
    public float Y;
}

/// <summary>Struct component whose ADD is what the M10 handler keys on (<c>BoxColliderComponent</c>).</summary>
internal struct BoxCollider
{
    public float Width;
    public float Height;
}

/// <summary>Zero-sized tag — <c>ColliderTagComponent</c>, Set from INSIDE the Added handler (M10).</summary>
internal struct ColliderTag
{
}

/// <summary>Zero-sized tag marking a level-owned entity — the LDtk sweep's handle on its own spawns.</summary>
internal struct Tile
{
}

/// <summary>Class component — <c>RigidBodyComponent</c>; <c>GravitySystem</c>'s predicate reads <c>Gravity.active</c> (M1).</summary>
internal sealed class RigidBody
{
    public readonly GravityState Gravity = new();
}

internal sealed class GravityState
{
    public bool Active = true;
}

/// <summary>Class component — <c>DrawComponent</c>; <c>MasterRenderSystem.BuildDrawSet</c>'s predicate reads <c>Target</c> (M1).</summary>
internal sealed class Draw
{
    public RenderTargetId Target;
    public float LayerDepth;
}

internal enum RenderTargetId
{
    Main,
    UI,
}

/// <summary>Class component — <c>AudioSourceComponent</c>; <c>AudioSystem.cs:141</c> discriminates on <c>ReferenceEquals(old,new)</c>.</summary>
internal sealed class AudioSource
{
    public string Cue;
    public bool Looping;
}

/// <summary>World singleton — the <c>LDtkLevelDataComponent</c> shape the LDtk parsers key on.</summary>
internal sealed class LevelData
{
    public string Identifier;
}

/// <summary>World singleton — the <c>CurrentLevelComponent</c> shape (a struct, unlike the one above).</summary>
internal struct CurrentLevel
{
    public string Identifier;
}

/// <summary>Message — the <c>EntitySpawnRequest</c> shape published once per tile.</summary>
internal readonly struct SpawnRequest
{
    public readonly int Index;

    public SpawnRequest(int index) => Index = index;
}

internal static class Scenarios
{
    /// <summary>Tile count for the mass-parser shape (item 41) — big enough to cross Arch chunk boundaries.</summary>
    private const int TileCount = 500;

    public static void RunAll(ProofReport report)
    {
        ArchNativeEventsAreOff(report);
        EntityComponentEvents(report);
        HandleLifetime(report);
        SingletonNotifications(report);
        PredicateMembership(report);
        MutationInsideAddedHandler(report);
        MassParserShape(report);
        WorldTeardownCascade(report);
        WorldTeardownWithSweepingHandler(report);
        WorldTeardownWithWorldDisposingHandler(report);
        TeardownChannelOrderAcrossLegs(report);
        WorldTeardownWithThrowingHandler(report);
    }

    // ===========================================================================================
    // S0 — D1's premise: Arch raises nothing, so everything observed later is facade-fired.
    // ===========================================================================================
    private static void ArchNativeEventsAreOff(ProofReport report)
    {
        report.Scenario("S0 — Arch's own events stay off (D1): every event below is facade-fired");

        var arch = ArchWorld.Create();
        try
        {
            var nativeCallbacks = 0;

            // The Subscribe* surface EXISTS in the shipped package — it is the raise sites that are
            // compiled out (Arch guards them behind its EVENTS build flag). Subscribing therefore
            // succeeds and then never fires, which is the trap this check exists to document.
            arch.SubscribeEntityCreated((in ArchEntity _) => nativeCallbacks++);
            arch.SubscribeEntityDestroyed((in ArchEntity _) => nativeCallbacks++);
            arch.SubscribeComponentAdded((in ArchEntity _, ref Transform __) => nativeCallbacks++);
            arch.SubscribeComponentSet((in ArchEntity _, ref Transform __) => nativeCallbacks++);
            arch.SubscribeComponentRemoved((in ArchEntity _, ref BoxCollider __) => nativeCallbacks++);

            var entity = arch.Create(new Transform { X = 1f, Y = 1f });
            arch.Add(entity, new BoxCollider { Width = 2f, Height = 2f });
            arch.Set(entity, new Transform { X = 2f, Y = 2f });
            arch.Remove<BoxCollider>(entity);
            arch.Destroy(entity);

            report.Check("native callbacks for a full create/add/set/remove/destroy", nativeCallbacks, 0);
            report.Note("Arch 2.1.0 ships the Subscribe* API but not the raise sites (EVENTS flag off), so a " +
                        "subscription compiles, runs and silently never fires. H7 option (b) — 'vendor Arch with " +
                        "EVENTS' — therefore means building Arch from source, not flipping a package switch. " +
                        "Everything checked below this line is raised by the facade itself (D1).");

            // Raw iteration order, measured here so the facade's own order below can be read against it.
            var ids = new List<int>();
            for (var i = 0; i < 6; i++) ids.Add(arch.Create(new Tile()).Id);

            var enumerated = new List<int>();
            var tileDescription = new Arch.Core.QueryDescription().WithAll<Tile>();
            foreach (ref var chunk in arch.Query(in tileDescription))
            {
                foreach (var index in chunk) enumerated.Add(chunk.Entity(index).Id);
            }

            ids.Reverse();
            report.Observe("raw Arch chunk enumeration of 6 entities", string.Join(",", enumerated));
            report.Check("raw Arch enumerates a chunk in DESCENDING order", string.Join(",", enumerated), string.Join(",", ids));

            // The OTHER iteration form. The facade's EntitySystem will run `Query(desc, ForEach)`,
            // not the chunk enumerator, so "descending" has to be measured on it rather than
            // assumed to follow — the two are different code paths in Arch.
            var forEachIds = new List<int>();
            arch.Query(in tileDescription, (ArchEntity entity) => forEachIds.Add(entity.Id));
            report.Observe("Query(desc, ForEach) over the same 6 entities", string.Join(",", forEachIds));
            report.Check("Query(desc, ForEach) enumerates in the SAME order as the chunk enumerator",
                string.Join(",", forEachIds), string.Join(",", enumerated));

            report.Note("Iteration order is therefore not merely 'unspecified' under Arch — it is REVERSED " +
                        "relative to DefaultEcs' insertion-ish order. Every first-match-and-break pick (items 48, " +
                        "58) and every membership sweep written to disk (items 70, 74) would flip. The facade " +
                        "sorts its own enumeration by id; nothing below inherits Arch's order.");
        }
        finally
        {
            ArchWorld.Destroy(arch);
        }

        CrossChunkIterationOrder(report);
    }

    /// <summary>
    /// The single-chunk probe above cannot see the order BETWEEN chunks, and items 70/74 sweep the
    /// whole world — many chunks — into a file. So the same question is asked again over an entity
    /// count that provably spans several chunks: are the chunks themselves walked forward while
    /// their contents run backward, or is the whole sweep one descending run?
    /// </summary>
    private static void CrossChunkIterationOrder(ProofReport report)
    {
        var arch = ArchWorld.Create();
        try
        {
            const int CrossChunkCount = 4_000;

            var created = new List<int>(CrossChunkCount);
            for (var i = 0; i < CrossChunkCount; i++) created.Add(arch.Create(new Transform { X = i, Y = i }).Id);

            var description = new Arch.Core.QueryDescription().WithAll<Transform>();

            var chunks = 0;
            var chunkIds = new List<int>(CrossChunkCount);
            foreach (ref var chunk in arch.Query(in description))
            {
                chunks++;
                foreach (var index in chunk) chunkIds.Add(chunk.Entity(index).Id);
            }

            report.Observe("chunks spanned by 4000 single-component entities", chunks);
            report.CheckTrue("the cross-chunk probe really spans MORE THAN ONE chunk", chunks > 1);
            report.Observe("first 3 / last 3 ids enumerated",
                $"{string.Join(",", chunkIds.GetRange(0, 3))} ... {string.Join(",", chunkIds.GetRange(chunkIds.Count - 3, 3))}");

            created.Reverse();
            report.Check("cross-chunk chunk enumeration is descending END TO END",
                string.Join(",", chunkIds) == string.Join(",", created), true);

            var forEachIds = new List<int>(CrossChunkCount);
            arch.Query(in description, (ArchEntity entity) => forEachIds.Add(entity.Id));
            report.Check("cross-chunk Query(desc, ForEach) matches the chunk enumerator",
                string.Join(",", forEachIds) == string.Join(",", chunkIds), true);

            report.Note("So the reversal is not a within-chunk artefact that averages out over a full sweep: " +
                        "across 4000 entities and several chunks the enumeration is one descending run, on both " +
                        "iteration forms. A whole-world sweep written to disk (items 70/74) inverts end to end.");
        }
        finally
        {
            ArchWorld.Destroy(arch);
        }
    }

    // ===========================================================================================
    // S1 — the five reactive verbs on entity components, with the exact payloads the engine reads.
    // ===========================================================================================
    private static void EntityComponentEvents(ProofReport report)
    {
        report.Scenario("S1 — facade-fired Added / Changed(old,new) / Removed / EntityDisposed");

        var world = EcsWorld.Create();
        try
        {
            var log = new List<string>();
            var aliveInsideDisposeHandler = false;

            // Handles are kept here, but S4 proves keeping them is not what keeps the subscription alive.
            world.SubscribeEntityComponentAdded((in Entity _, in Transform value) => log.Add($"Added(Transform {value.X})"));
            world.SubscribeEntityComponentChanged((in Entity _, in Transform oldValue, in Transform newValue) =>
                log.Add($"Changed(Transform {oldValue.X}->{newValue.X})"));
            world.SubscribeEntityComponentRemoved((in Entity _, in Transform value) => log.Add($"Removed(Transform {value.X})"));

            // AudioSystem.cs:141 discriminates a notify-style publication from a real replacement on
            // exactly this: ReferenceEquals(old, new). Recorded per dispatch, asserted per operation.
            var lastChangedWasSelfIdentical = false;
            world.SubscribeEntityComponentChanged((in Entity _, in AudioSource oldValue, in AudioSource newValue) =>
            {
                log.Add("Changed(AudioSource)");
                lastChangedWasSelfIdentical = ReferenceEquals(oldValue, newValue);
            });
            world.SubscribeEntityComponentRemoved((in Entity _, in AudioSource value) => log.Add($"Removed(AudioSource {value.Cue})"));
            world.SubscribeEntityDisposed((in Entity entity) =>
            {
                log.Add("EntityDisposed");
                aliveInsideDisposeHandler = entity.IsAlive;
            });

            var entity = world.CreateEntity();

            entity.Set(new Transform { X = 1f, Y = 2f });
            report.Check("Set on an ABSENT component fires Added", Last(log), "Added(Transform 1)");

            entity.Set(new Transform { X = 3f, Y = 4f });
            report.Check("Set on a PRESENT component fires Changed(old,new)", Last(log), "Changed(Transform 1->3)");

            // M1's other half: an in-place write through `ref` publishes NOTHING.
            var before = log.Count;
            ref var transform = ref entity.Get<Transform>();
            transform.X = 99f;
            report.Check("in-place ref write fires nothing", log.Count, before);
            report.Check("in-place ref write IS visible to a later read", entity.Get<Transform>().X, 99f);

            var audio = new AudioSource { Cue = "wind", Looping = true };
            entity.Set(audio);
            entity.NotifyChanged<AudioSource>();
            report.CheckTrue("NotifyChanged delivers Changed with ReferenceEquals(old,new)", lastChangedWasSelfIdentical);

            entity.Set(new AudioSource { Cue = "thunder" });
            report.Check("Set(new instance) delivers Changed with a DIFFERENT old ref",
                lastChangedWasSelfIdentical, false);

            report.Throws<InvalidOperationException>(
                "NotifyChanged on an ABSENT component throws (D14)",
                () => entity.NotifyChanged<BoxCollider>());

            before = log.Count;
            entity.Remove<Transform>();
            report.Check("Remove on a PRESENT component fires Removed with the value", Last(log), "Removed(Transform 99)");
            entity.Remove<Transform>();
            report.Check("Remove on an ABSENT component fires nothing", log.Count, before + 1);

            log.Clear();
            entity.Dispose();

            // Item 40 says "EntityDisposed, then ComponentRemoved per present component". Measured
            // (M4d world D), that is not a phase order but this world's SUBSCRIPTION order: here
            // EntityDisposed was subscribed LAST, after both component channels, and DefaultEcs
            // 0.18.0-beta01 reports it last on exactly that fixture. S7 has the other order because
            // it subscribes EntityDisposed first.
            report.Check("Dispose reports the channels in SUBSCRIPTION order (EntityDisposed last here)",
                string.Join(" | ", log), "Removed(AudioSource thunder) | EntityDisposed");
            report.Check("Dispose fires ComponentRemoved per present component", log.Count, 2);
            report.CheckTrue("entity reads IsAlive == true inside the Dispose handler", aliveInsideDisposeHandler);
            report.Check("entity is dead once Dispose returned", entity.IsAlive, false);

            before = log.Count;
            entity.Dispose();
            report.Check("double Dispose is a silent no-op", log.Count, before);
        }
        finally
        {
            world.Dispose();
        }
    }

    // ===========================================================================================
    // S2 — handle lifetime across id recycling (items 17/56/76): the facade owns the version stamp.
    // ===========================================================================================
    private static void HandleLifetime(ProofReport report)
    {
        report.Scenario("S2 — a stale handle never reads the entity that recycled its Arch id");

        var world = EcsWorld.Create();
        try
        {
            var doomed = world.CreateEntity();
            doomed.Set(new Transform { X = 7f, Y = 7f });
            var doomedId = doomed.Handle.Id;
            doomed.Dispose();

            Entity recycled = Entity.Null;
            for (var i = 0; i < 64; i++)
            {
                var candidate = world.CreateEntity();
                candidate.Set(new Transform { X = -1f, Y = -1f });
                if (candidate.Handle.Id != doomedId) continue;
                recycled = candidate;
                break;
            }

            report.CheckTrue("Arch recycled the id (the hazard is real, not theoretical)", recycled != Entity.Null);
            report.Check("stale handle IsAlive", doomed.IsAlive, false);
            report.Check("recycled occupant IsAlive", recycled.IsAlive, true);
            report.Check("stale handle != recycled occupant (version-stamped equality)", doomed == recycled, false);
            report.Check("Has<T> through the stale handle", doomed.Has<Transform>(), false);
            report.Throws<InvalidOperationException>("Get<T> through the stale handle throws", () =>
            {
                ref var _ = ref doomed.Get<Transform>();
            });

            // Item 75's seam: a dictionary keyed by Entity must not confuse the two.
            var keyed = new Dictionary<Entity, string> { [doomed] = "dead", [recycled] = "live" };
            report.Check("Entity-keyed dictionary holds both without collision", keyed.Count, 2);
            report.Check("dead handle finds its OWN keyed entry", keyed[doomed], "dead");
        }
        finally
        {
            world.Dispose();
        }
    }

    // ===========================================================================================
    // S3 — world singletons: the 4 world-component types and the Restart transport's Changed shape.
    // ===========================================================================================
    private static void SingletonNotifications(ProofReport report)
    {
        report.Scenario("S3 — world-singleton Added / Changed(old,new) / Removed + carrier invisibility");

        var world = EcsWorld.Create();
        try
        {
            var log = new List<string>();

            // Measured on DefaultEcs (item 66): subscribing over an ALREADY-present value replays
            // nothing. The facade reproduces that, so the LDtk parsers' manual Has+Get replay stays
            // load-bearing rather than becoming a double parse.
            world.Set(new CurrentLevel { Identifier = "preexisting" });
            world.SubscribeWorldComponentAdded((EcsWorld _, in CurrentLevel value) => log.Add($"Added({value.Identifier})"));
            report.Check("subscribing over a PRESENT singleton replays nothing", log.Count, 0);

            world.SubscribeWorldComponentAdded((EcsWorld _, in LevelData value) => log.Add($"Added({value.Identifier})"));
            world.SubscribeWorldComponentChanged((EcsWorld _, in LevelData oldValue, in LevelData newValue) =>
                log.Add($"Changed({oldValue.Identifier}->{newValue.Identifier})"));
            world.SubscribeWorldComponentRemoved((EcsWorld _, in LevelData value) => log.Add($"Removed({value.Identifier})"));

            report.Check("Has before the first Set", world.Has<LevelData>(), false);

            world.Set(new LevelData { Identifier = "Level_0" });
            report.Check("Set on an ABSENT singleton fires Added", Last(log), "Added(Level_0)");

            world.Set(new LevelData { Identifier = "Level_1" });
            report.Check("Set on a PRESENT singleton fires Changed, NOT Added (CORE_TENETS §9)", Last(log), "Changed(Level_0->Level_1)");
            report.Check("Get reads the new value", world.Get<LevelData>().Identifier, "Level_1");

            var before = log.Count;
            world.Get<LevelData>().Identifier = "edited-in-place";
            report.Check("in-place edit of a singleton fires nothing", log.Count, before);

            world.Remove<LevelData>();
            report.Check("Remove on a PRESENT singleton fires Removed", Last(log), "Removed(edited-in-place)");

            before = log.Count;
            world.Remove<LevelData>();
            report.Check("Remove on an ABSENT singleton fires nothing (item 39)", log.Count, before);

            world.Set(new LevelData { Identifier = "Level_2" });
            report.Check("re-Set after Remove fires Added again (item 8)", Last(log), "Added(Level_2)");

            // Item 43: no hidden carrier entity exists to leak into an unfiltered enumeration.
            var a = world.CreateEntity();
            var b = world.CreateEntity();
            a.Set(new Transform());
            b.Set(new Transform());
            report.Check("unfiltered world.GetAllEntities() sees only game entities", world.GetAllEntities().Length, 2);

            using var everything = world.GetEntities().AsSet();
            report.Check("an unfiltered query sees only game entities", everything.GetEntities().Length, 2);
        }
        finally
        {
            world.Dispose();
        }
    }

    // ===========================================================================================
    // S4 — M1: value-predicate membership is a CACHED answer, moved only by publication.
    // ===========================================================================================
    private static void PredicateMembership(ProofReport report)
    {
        report.Scenario("S4 — value-predicate membership (M1): publication moves it, an in-place write does not");

        var world = EcsWorld.Create();
        try
        {
            // Populated BEFORE the query exists, so AsSet() has to backfill by a live scan (items 11/54).
            var bodies = new List<Entity>();
            for (var i = 0; i < 3; i++)
            {
                var entity = world.CreateEntity();
                entity.Set(new Transform { X = i, Y = 0f });
                var body = new RigidBody();
                body.Gravity.Active = i < 2;   // bodies 0 and 1 fall, body 2 does not
                entity.Set(body);
                bodies.Add(entity);
            }

            using var falling = world.GetEntities()
                .With<Transform>()
                .With((in RigidBody body) => body.Gravity.Active)
                .AsSet();

            report.Check("construction backfills membership from CURRENT values", falling.GetEntities().Length, 2);

            // The engine's exact silent bug: flip the field a predicate reads, publish nothing.
            bodies[0].Get<RigidBody>().Gravity.Active = false;
            bodies[2].Get<RigidBody>().Gravity.Active = true;
            report.Check("in-place flips do NOT move membership (both directions)", falling.GetEntities().Length, 2);
            report.CheckTrue("the switched-off body KEEPS FALLING until published", Contains(falling, bodies[0]));
            report.CheckTrue("the switched-on body stays out until published", !Contains(falling, bodies[2]));

            bodies[0].NotifyChanged<RigidBody>();
            report.CheckTrue("NotifyChanged drops the now-inactive body", !Contains(falling, bodies[0]));

            bodies[2].NotifyChanged<RigidBody>();
            report.CheckTrue("NotifyChanged admits the now-active body", Contains(falling, bodies[2]));
            report.Check("membership after both publications", falling.GetEntities().Length, 2);

            // The second engine predicate: MasterRenderSystem.BuildDrawSet retargeting.
            var drawn = new List<Entity>();
            for (var i = 0; i < 3; i++)
            {
                var entity = world.CreateEntity();
                entity.Set(new Draw { Target = RenderTargetId.Main, LayerDepth = i });
                drawn.Add(entity);
            }

            using var mainPass = world.GetEntities().With((in Draw draw) => draw.Target == RenderTargetId.Main).AsSet();
            using var uiPass = world.GetEntities().With((in Draw draw) => draw.Target == RenderTargetId.UI).AsSet();
            report.Check("main pass after construction", mainPass.GetEntities().Length, 3);

            drawn[1].Get<Draw>().Target = RenderTargetId.UI;
            report.Check("in-place retarget leaves the entity in the OLD pass", mainPass.GetEntities().Length, 3);
            report.Check("...and out of the new one", uiPass.GetEntities().Length, 0);

            drawn[1].Set(drawn[1].Get<Draw>());   // re-publish the SAME instance, as the engine does
            report.Check("re-publishing moves it out of the main pass", mainPass.GetEntities().Length, 2);
            report.Check("...and into the UI pass", uiPass.GetEntities().Length, 1);

            // Item 22: the snapshot order is the FACADE's (ascending id at construction, publication
            // order afterwards) — never the descending order raw Arch handed S0.
            var snapshot = mainPass.GetEntities();
            report.CheckTrue("snapshot order is the facade's, not Arch's reverse", snapshot[0] == drawn[0] && snapshot[1] == drawn[2]);
            report.CheckTrue("a second snapshot is identical", Same(snapshot, mainPass.GetEntities()));

            report.Throws<NotSupportedException>("EntityQuery.Count throws instead of guessing (item 9)", () =>
            {
                var _ = mainPass.Count;
            });

            // Item 10: transient using-scoped queries unhook on Dispose.
            var queriesBefore = world.QueryCount;
            for (var i = 0; i < 10_000; i++)
            {
                using var transient = world.GetEntities().With<Transform>().AsSet();
                if (transient.GetEntities().Length < 0) throw new InvalidOperationException("unreachable");
            }

            report.Check("10k transient queries leave no registration behind", world.QueryCount, queriesBefore);
        }
        finally
        {
            world.Dispose();
        }
    }

    // ===========================================================================================
    // S5 — M10: a structural mutation performed from INSIDE an Added handler.
    // ===========================================================================================
    private static void MutationInsideAddedHandler(ProofReport report)
    {
        report.Scenario("S5 — M10: Set<ColliderTag> from inside the ComponentAdded handler (TransformCollisionDetectionSystem.cs:87-95)");

        var world = EcsWorld.Create();
        try
        {
            var tagAdds = 0;
            var nestedAddedEvents = 0;
            var maxDepth = 0;
            var depth = 0;

            // Handle DISCARDED on purpose: the real site drops both IDisposables (:74-75).
            world.SubscribeEntityComponentAdded((in Entity entity, in BoxCollider _) =>
            {
                depth++;
                maxDepth = Math.Max(maxDepth, depth);
                if (!entity.Has<ColliderTag>()) entity.Set<ColliderTag>();
                tagAdds++;
                depth--;
            });

            world.SubscribeEntityComponentAdded((in Entity _, in ColliderTag __) =>
            {
                depth++;
                maxDepth = Math.Max(maxDepth, depth);
                nestedAddedEvents++;
                depth--;
            });

            using var activeSet = world.GetEntities().With<ColliderTag>().With<Transform>().AsSet();

            var entity = world.CreateEntity();
            entity.Set(new Transform { X = 5f, Y = 5f });
            entity.Set(new BoxCollider { Width = 8f, Height = 8f });

            report.CheckTrue("tag is present the moment the outer Set returns", entity.Has<ColliderTag>());
            report.Check("the tag's own Added fired (nested dispatch)", nestedAddedEvents, 1);
            report.Check("nesting depth reached inside the publish path", maxDepth, 2);
            report.CheckTrue("the collider set sees the entity in the SAME frame (D9)", Contains(activeSet, entity));
            report.Check("the archetype move preserved Transform", entity.Get<Transform>().X, 5f);
            report.Check("the archetype move preserved BoxCollider", entity.Get<BoxCollider>().Width, 8f);

            // The handler is idempotent by its own Has check — a second Set must not double-tag.
            entity.Set(new BoxCollider { Width = 9f, Height = 9f });
            report.Check("re-Set fires Changed, not Added — no second tag", nestedAddedEvents, 1);

            var batch = new List<Entity>();
            for (var i = 0; i < TileCount; i++)
            {
                var member = world.CreateEntity();
                member.Set(new Transform { X = i, Y = i });
                member.Set(new BoxCollider { Width = 1f, Height = 1f });
                batch.Add(member);
            }

            report.Check("batch-add: every collider got tagged", tagAdds, TileCount + 1);
            report.Check("batch-add: the set holds every one of them", activeSet.GetEntities().Length, TileCount + 1);

            // Discarding the subscription handle must not end the subscription (M10's open question).
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var afterGc = world.CreateEntity();
            afterGc.Set(new Transform());
            afterGc.Set(new BoxCollider { Width = 1f, Height = 1f });
            report.CheckTrue("a DISCARDED subscription handle still dispatches after a GC", afterGc.Has<ColliderTag>());

            StaleRefProbe(report);
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// R3, demonstrated: a <c>ref</c> taken before a structural change points at the OLD chunk
    /// afterwards. This is why the facade copies values out before dispatching and re-reads after —
    /// and why wave 3's chunked conversions must never keep a span across a publication.
    /// </summary>
    private static void StaleRefProbe(ProofReport report)
    {
        var world = EcsWorld.Create();
        try
        {
            var entity = world.CreateEntity();
            entity.Set(new Transform { X = 1f, Y = 1f });

            ref var stale = ref entity.Get<Transform>();
            entity.Set(new BoxCollider { Width = 1f, Height = 1f });   // archetype move
            stale.X = 777f;

            report.Observe("value read back after writing through a pre-move ref", entity.Get<Transform>().X);
            report.CheckTrue(
                "a ref held across a structural change is STALE (write lost or aliased)",
                Math.Abs(entity.Get<Transform>().X - 777f) > float.Epsilon);
        }
        finally
        {
            world.Dispose();
        }
    }

    // ===========================================================================================
    // S6 — item 41: mass Dispose + Create + Publish inside singleton Added AND Removed dispatch.
    // ===========================================================================================
    private static void MassParserShape(ProofReport report)
    {
        report.Scenario("S6 — item 41: the LDtk parser shape — mass Dispose+Create+Publish inside singleton dispatch");

        var world = EcsWorld.Create();
        try
        {
            var addedDispatches = 0;
            var removedDispatches = 0;
            var changedDispatches = 0;
            var sweptTotal = 0;
            var spawnedTotal = 0;
            var maxDepth = 0;
            var depth = 0;

            // --- "collision system": the M10 handler, live during the whole parse ------------------
            world.SubscribeEntityComponentAdded((in Entity entity, in BoxCollider _) =>
            {
                depth++;
                maxDepth = Math.Max(maxDepth, depth);
                if (!entity.Has<ColliderTag>()) entity.Set<ColliderTag>();
                depth--;
            });

            using var activeSet = world.GetEntities().With<ColliderTag>().With<Transform>().AsSet();
            using var tiles = world.GetEntities().With<Tile>().AsSet();
            using var mainPass = world.GetEntities().With((in Draw draw) => draw.Target == RenderTargetId.Main).AsSet();

            // --- "entity spawn system": creates one entity per published request -------------------
            world.Subscribe((SpawnRequest request) =>
            {
                depth++;
                maxDepth = Math.Max(maxDepth, depth);

                var tile = world.CreateEntity();
                tile.Set(new Transform { X = request.Index, Y = request.Index });
                tile.Set(new Draw { Target = RenderTargetId.Main, LayerDepth = request.Index * 0.001f });
                tile.Set(new BoxCollider { Width = 16f, Height = 16f });   // → M10 tag, one level deeper
                tile.Set<Tile>();
                spawnedTotal++;

                depth--;
            });

            // --- "tile parser": sweeps and re-parses on the singleton's Added / Removed ------------
            void Sweep()
            {
                // The engine's shape (LDtkTileParserSystem.CleanupTileEntities): take a SNAPSHOT,
                // then dispose through it. Membership updates land while the loop is still running.
                foreach (var tile in tiles.GetEntities())
                {
                    if (!tile.IsAlive) continue;
                    tile.Dispose();
                    sweptTotal++;
                }
            }

            world.SubscribeWorldComponentAdded((EcsWorld _, in LevelData level) =>
            {
                depth++;
                maxDepth = Math.Max(maxDepth, depth);
                addedDispatches++;

                Sweep();
                for (var i = 0; i < TileCount; i++) world.Publish(new SpawnRequest(i));

                depth--;
            });

            world.SubscribeWorldComponentChanged((EcsWorld _, in LevelData __, in LevelData ___) => changedDispatches++);

            world.SubscribeWorldComponentRemoved((EcsWorld _, in LevelData __) =>
            {
                depth++;
                maxDepth = Math.Max(maxDepth, depth);
                removedDispatches++;

                Sweep();

                depth--;
            });

            // --- first load ------------------------------------------------------------------------
            world.Set(new LevelData { Identifier = "Level_0" });
            report.Check("singleton Added dispatched once", addedDispatches, 1);
            report.Check("tiles created inside the Added dispatch", spawnedTotal, TileCount);
            report.Check("tile membership after the first parse", tiles.GetEntities().Length, TileCount);
            report.Check("collider set tagged every tile (M10 inside a mass publish)", activeSet.GetEntities().Length, TileCount);
            report.Check("draw pass holds every tile", mainPass.GetEntities().Length, TileCount);
            report.Check("world holds exactly the tiles", world.GetAllEntities().Length, TileCount);
            report.Check("max nesting: singleton → message → component Added", maxDepth, 3);

            var firstGeneration = tiles.GetEntities();

            // --- a successful re-import Sets WITHOUT removing: Changed, and the parser is inert (item 63)
            world.Set(new LevelData { Identifier = "Level_0-reimported" });
            report.Check("re-Set fires Changed, not Added", changedDispatches, 1);
            report.Check("...so the parser does not re-parse", addedDispatches, 1);
            report.Check("...and the tiles are untouched", tiles.GetEntities().Length, TileCount);

            // --- unload: mass dispose inside the Removed dispatch ------------------------------------
            world.Remove<LevelData>();
            report.Check("singleton Removed dispatched once", removedDispatches, 1);
            report.Check("every tile was swept", sweptTotal, TileCount);
            report.Check("tile membership dropped synchronously (item 67)", tiles.GetEntities().Length, 0);
            report.Check("collider membership dropped too", activeSet.GetEntities().Length, 0);
            report.Check("predicate membership dropped too", mainPass.GetEntities().Length, 0);
            report.Check("the world is empty", world.GetAllEntities().Length, 0);
            report.CheckTrue("every first-generation handle is dead", AllDead(firstGeneration));

            // --- reload: Added fires again, over recycled ids ----------------------------------------
            world.Set(new LevelData { Identifier = "Level_1" });
            report.Check("re-Set after Remove fires Added again (item 8)", addedDispatches, 2);
            report.Check("second parse created a full generation", tiles.GetEntities().Length, TileCount);
            report.CheckTrue("first-generation handles stay dead across id recycling", AllDead(firstGeneration));
            report.Check("the sweep at the head of the second parse found nothing to dispose", sweptTotal, TileCount);

            // --- load a second level over the first: sweep + reparse in ONE dispatch -----------------
            world.Remove<LevelData>();
            world.Set(new LevelData { Identifier = "Level_2" });
            report.Check("third parse swept the previous generation", sweptTotal, TileCount * 2);
            report.Check("third parse repopulated", tiles.GetEntities().Length, TileCount);
            report.Check("collider set is consistent after three parses", activeSet.GetEntities().Length, TileCount);
            report.Check("no entity leaked across three parses", world.GetAllEntities().Length, TileCount);
            report.Check("total spawns across three parses", spawnedTotal, TileCount * 3);
        }
        finally
        {
            world.Dispose();
        }
    }

    // ===========================================================================================
    // S7 — world teardown: reproduces the MEASURED DefaultEcs cascade that contradicts item 50.
    //
    // The fixture mirrors the measurement harness's M4 exactly, because a naive one cannot tell the
    // candidate orders apart:
    //   * ONE carrier holds TWO subscribed component types, INTERLEAVED between single-component
    //     carriers — so "ComponentRemoved grouped by component pool" and "…walked per entity"
    //     produce different logs instead of the same one;
    //   * an entity id is RECYCLED before teardown, so creation order and entity-id order diverge
    //     and "EntityDisposed in creation order" stops being unfalsifiable;
    //   * the whole SEQUENCE is asserted, not per-type counts, which is the only assertion shape
    //     that can fail when the order is wrong;
    //   * TWO world singletons are subscribed, Set in the REVERSE of their subscription order — so
    //     the last third of the cascade is an asserted order too, and one that can distinguish
    //     "subscription order" (what M4c measured DefaultEcs doing) from "the order they were Set"
    //     and from a raw Dictionary walk.
    //
    // Every handler here is INERT (it only logs). That is the scope of the orders asserted below:
    // S8 measures what a MUTATING handler does to them, and it is not the same shape.
    //
    // NOTE on what this fixture can and cannot show: its subscriptions are made in the order
    // EntityDisposed → entity components → world components, so its log agrees with "three phases"
    // and with "one channel list in subscription order" at the same time. S10 is the fixture that
    // separates them (M4d does it on DefaultEcs), and the answer is the channel list.
    // ===========================================================================================
    private static void WorldTeardownCascade(ProofReport report)
    {
        report.Scenario("S7 — world.Dispose fires the full cascade, in the measured ORDER (item 50 says event-silent)");

        var world = EcsWorld.Create();
        var log = new List<string>();

        // Subscription order mints the facade's component-type ids: Transform = 0, AudioSource = 1,
        // LevelData = 2, CurrentLevel = 3 — world and entity components share one registry.
        world.SubscribeEntityDisposed((in Entity entity) => log.Add($"EntityDisposed({Mark(entity)})"));
        world.SubscribeEntityComponentRemoved((in Entity entity, in Transform value) =>
            log.Add($"Removed(Transform #{value.X} on {Mark(entity)})"));
        world.SubscribeEntityComponentRemoved((in Entity entity, in AudioSource value) =>
            log.Add($"Removed(AudioSource {value.Cue} on {Mark(entity)})"));
        world.SubscribeWorldComponentRemoved((EcsWorld _, in LevelData __) => log.Add("WorldComponentRemoved(LevelData)"));
        world.SubscribeWorldComponentRemoved((EcsWorld _, in CurrentLevel __) => log.Add("WorldComponentRemoved(CurrentLevel)"));

        var one = world.CreateEntity();
        one.Set(new Transform { X = 1f, Y = 1f });

        // The TWO-component carrier, second of five — the discriminator between pool-grouped and
        // per-entity order (AudioSystem.cs:133-137 is the real reader of this leg).
        var two = world.CreateEntity();
        two.Set(new Transform { X = 2f, Y = 2f });
        two.Set(new AudioSource { Cue = "music-loop", Looping = true });

        var scratch = world.CreateEntity();
        scratch.Set(new Transform { X = 90f, Y = 90f });

        var three = world.CreateEntity();
        three.Set(new Transform { X = 3f, Y = 3f });

        scratch.Dispose();   // frees the LOW id that "four" is about to recycle
        report.Check("an entity disposed BEFORE teardown reports once, there and then",
            string.Join(" | ", log), "EntityDisposed(#90) | Removed(Transform #90 on #90)");
        log.Clear();

        var four = world.CreateEntity();
        four.Set(new Transform { X = 4f, Y = 4f });

        // Set in the REVERSE of the subscription order, so the world-component leg below asserts a
        // real order rather than agreeing with every candidate at once.
        world.Set(new CurrentLevel { Identifier = "Level_0" });
        world.Set(new LevelData { Identifier = "Level_0" });

        var held = world.GetEntities().With<Transform>().AsSet();
        report.Check("membership before teardown", held.GetEntities().Length, 4);
        report.CheckTrue("the last-created entity recycled a LOWER id, so creation order != id order",
            four.Handle.Id < three.Handle.Id);

        world.Dispose();

        var sequence = string.Join(" | ", log);
        report.Observe("event sequence", sequence);

        // Measured on DefaultEcs 0.18.0-beta01 by the sibling harness (M4), on the same fixture:
        //   EntityDisposed in ascending ENTITY-ID order — #1, #2, #4(recycled low id), #3 — NOT
        //   creation order; then ComponentRemoved POOL-GROUPED (every Transform, then the single
        //   AudioSource, which belongs to the SECOND carrier); then world components.
        report.Check("the teardown sequence is the measured DefaultEcs one, in order", sequence,
            "EntityDisposed(#1) | EntityDisposed(#2) | EntityDisposed(#4) | EntityDisposed(#3) | "
            + "Removed(Transform #1 on #1) | Removed(Transform #2 on #2) | Removed(Transform #4 on #4) | "
            + "Removed(Transform #3 on #3) | Removed(AudioSource music-loop on #2) | "
            + "WorldComponentRemoved(LevelData) | WorldComponentRemoved(CurrentLevel)");
        report.Check("EntityDisposed per live entity", Starting(log, "EntityDisposed"), 4);
        report.Check("the pre-teardown entity is NOT reported a second time", Starting(log, "EntityDisposed(#90)"), 0);
        report.Check("total events on teardown (item 50 predicts 0)", log.Count, 11);
        report.Check("the two world components report in SUBSCRIPTION order, not in Set order",
            string.Join(" | ", Filtered(log, "WorldComponentRemoved")),
            "WorldComponentRemoved(LevelData) | WorldComponentRemoved(CurrentLevel)");

        // The teardown obligations the screen-teardown cell names, made observable instead of assumed.
        report.Check("a query held across teardown reads EMPTY, not stale dead handles", held.GetEntities().Length, 0);
        report.Check("a disposed world holds no subscriptions", world.SubscriberCount, 0);
        report.Check("a disposed world holds no query registrations", world.QueryCount, 0);
        report.CheckTrue("every pre-teardown handle is dead", AllDead(new[] { one, two, three, four }));

        report.Note("Item 50 must be restated before wave 1 writes its contract test: DefaultEcs 0.18.0-beta01 " +
                    "fires this same cascade in this same order, so 'event-silent' would be a " +
                    "behaviour CHANGE, not parity. The ORDER is facade-imposed on THREE axes: entity id " +
                    "within a channel (Arch enumerates reversed — S0), publication order for query " +
                    "membership, and — carrying both the entity-component pools and the world components — " +
                    "this world's SUBSCRIPTION order over one channel list (Arch's archetype signature order " +
                    "and BCL Dictionary enumeration are what the facade would otherwise inherit there).");
        report.Note("SCOPE: the orders asserted above hold while no handler MUTATES during the " +
                    "cascade. Every handler here only logs. S8 runs the engine's real unload sweep from " +
                    "inside teardown and measures what survives — pool-grouping does not, and neither does " +
                    "this fixture's world-components-last shape.");
        report.Note("Post-teardown query membership and subscription clearing are facade GUARANTEES, not " +
                    "parity: DefaultEcs was measured (M6) to leave EntitySet.Count stale after world.Dispose " +
                    "and to throw NullReferenceException when that set is enumerated. Nothing in the engine " +
                    "reads a set after teardown, so defining the answer costs no behaviour.");
    }

    // ===========================================================================================
    // S8 — item 41 AT TEARDOWN: the LDtk unload sweep runs while world.Dispose is already firing.
    //
    // This is not hypothetical. LDtkTileParserSystem.cs:42 subscribes the world-component Removed
    // leg and CleanupTileEntities (:145-155) mass-calls entity.Dispose(); at world teardown that
    // handler runs against entities the cascade is already reporting. S6 covers the sweep during
    // normal play; this covers it during teardown, which S6 and S7 both leave unasserted.
    //
    // Every leg here uses S7's IDENTITY-carrying fixture (a marker in Transform.X, and one carrier
    // holding a SECOND subscribed component type) and asserts the FULL sequence, because the whole
    // point of these legs is what a mutating handler does to the ORDER — which count-only asserts
    // cannot see. The measured answer is that BOTH of the shapes S7 shows break here: pool-grouping
    // collapses to per-entity, and ComponentRemoved lands after WorldComponentRemoved.
    // ===========================================================================================
    private static void WorldTeardownWithSweepingHandler(ProofReport report)
    {
        report.Scenario("S8 — a handler that mass-disposes entities DURING world.Dispose (the LDtk unload sweep)");

        // --- leg A: the engine's real shape — the sweep hangs off world-component Removed ---------
        {
            var world = EcsWorld.Create();
            var log = new List<string>();
            var carriers = new List<Entity>();

            // Subscription order mints the ids: Transform = 0, AudioSource = 1, LevelData = 2.
            world.SubscribeEntityDisposed((in Entity entity) => log.Add($"EntityDisposed({Mark(entity)})"));
            world.SubscribeEntityComponentRemoved((in Entity entity, in Transform value) =>
                log.Add($"Removed(Transform #{value.X} on {Mark(entity)})"));
            world.SubscribeEntityComponentRemoved((in Entity entity, in AudioSource value) =>
                log.Add($"Removed(AudioSource {value.Cue} on {Mark(entity)})"));
            world.SubscribeWorldComponentRemoved((EcsWorld _, in LevelData __) =>
            {
                log.Add("WorldComponentRemoved(LevelData)");
                var aliveNow = 0;
                foreach (var carrier in carriers)
                {
                    if (carrier.IsAlive) aliveNow++;
                }

                report.Check("leg A: the sweep still sees its entities ALIVE at teardown", aliveNow, 3);
                foreach (var carrier in carriers)
                {
                    if (carrier.IsAlive) carrier.Dispose();
                }
            });

            for (var i = 1; i <= 3; i++)
            {
                var tile = world.CreateEntity();
                tile.Set(new Transform { X = i, Y = i });

                // The SECOND carrier holds a second subscribed type — the same discriminator S7
                // uses, so pool-grouped and per-entity produce different logs here too.
                if (i == 2) tile.Set(new AudioSource { Cue = "music-loop", Looping = true });
                carriers.Add(tile);
            }

            world.Set(new LevelData { Identifier = "Level_0" });
            world.Dispose();

            var sequence = string.Join(" | ", log);
            report.Observe("leg A event sequence", sequence);

            // MEASURED on DefaultEcs (M5, same shape): 2 EntityDisposed and 2 ComponentRemoved per
            // entity — the teardown walk reports them, then the sweep disposes them and reports them
            // AGAIN. Double-firing is the incumbent behaviour, so the facade reproduces it rather
            // than "fixing" it behind an unmeasured guard.
            //
            // The SEQUENCE is what the count-only version of this check could not see: everything
            // the sweep re-reports routes through DisposeEntity, which is per-ENTITY
            // (EntityDisposed then that entity's own components, ascending type id) — so the
            // pool-grouping S7 asserts survives only for the first, walk-driven half, and the
            // sweep's ComponentRemoved events land AFTER WorldComponentRemoved.
            report.Check("leg A: the FULL sequence under a sweeping handler", sequence,
                "EntityDisposed(#1) | EntityDisposed(#2) | EntityDisposed(#3) | "
                + "Removed(Transform #1 on #1) | Removed(Transform #2 on #2) | Removed(Transform #3 on #3) | "
                + "Removed(AudioSource music-loop on #2) | "
                + "WorldComponentRemoved(LevelData) | "
                + "EntityDisposed(#1) | Removed(Transform #1 on #1) | "
                + "EntityDisposed(#2) | Removed(Transform #2 on #2) | Removed(AudioSource music-loop on #2) | "
                + "EntityDisposed(#3) | Removed(Transform #3 on #3)");
            report.Check("leg A: EntityDisposed, 2 per entity (DefaultEcs parity)", Starting(log, "EntityDisposed"), 6);
            report.Check("leg A: Removed(Transform), 2 per entity (DefaultEcs parity)", Starting(log, "Removed(Transform"), 6);
            report.Check("leg A: Removed(AudioSource), 2 for its one carrier", Starting(log, "Removed(AudioSource"), 2);
            report.CheckTrue("leg A: every carrier is dead once Dispose returned", AllDead(carriers.ToArray()));

            // The two order claims S7 makes, re-tested here — and both fail on this shape.
            report.CheckTrue("leg A: pool-grouping does NOT survive the sweep (the re-report is per-entity)",
                sequence.Contains("EntityDisposed(#2) | Removed(Transform #2 on #2) | Removed(AudioSource music-loop on #2)",
                    StringComparison.Ordinal));
            report.CheckTrue("leg A: ComponentRemoved fires AFTER WorldComponentRemoved on this shape",
                sequence.IndexOf("WorldComponentRemoved", StringComparison.Ordinal)
                < sequence.LastIndexOf("Removed(Transform", StringComparison.Ordinal));

            report.Note("Wave 1 owns this as a REAL hazard, not a curiosity: AudioSystem.OnAudioSourceRemoved and " +
                        "the LDtk sweep both run twice at world teardown TODAY. Item 50's restatement has to say " +
                        "which of the two is the contract — and it must scope the ordering claims to teardowns " +
                        "with INERT handlers, because the engine runs this shape at every screen change. The " +
                        "re-reported entities go through DisposeEntity, which walks the SAME channel list for one " +
                        "entity (S10) — that is why the collapse is per-entity and why it can land after the " +
                        "world-component channel that triggered it.");
        }

        // --- leg B: the same sweep from the EntityDisposed leg — the recursion case ---------------
        {
            var world = EcsWorld.Create();
            var log = new List<string>();
            var carriers = new List<Entity>();

            world.SubscribeEntityDisposed((in Entity entity) =>
            {
                log.Add($"EntityDisposed({Mark(entity)})");
                foreach (var carrier in carriers)
                {
                    if (carrier.IsAlive) carrier.Dispose();
                }
            });
            world.SubscribeEntityComponentRemoved((in Entity entity, in Transform value) =>
                log.Add($"Removed(Transform #{value.X} on {Mark(entity)})"));
            world.SubscribeEntityComponentRemoved((in Entity entity, in AudioSource value) =>
                log.Add($"Removed(AudioSource {value.Cue} on {Mark(entity)})"));

            for (var i = 1; i <= 3; i++)
            {
                var tile = world.CreateEntity();
                tile.Set(new Transform { X = i, Y = i });
                if (i == 2) tile.Set(new AudioSource { Cue = "music-loop", Looping = true });
                carriers.Add(tile);
            }

            world.Dispose();

            var sequence = string.Join(" | ", log);
            report.Observe("leg B event sequence", sequence);

            // The sweep recurses depth-first from the FIRST entity, so the components unwind in
            // REVERSE carrier order — the opposite of the ascending-id walk S7 asserts. Only a
            // sequence assertion on an identity-carrying fixture can see that.
            report.Check("leg B: the FULL sequence under a recursive sweep", sequence,
                "EntityDisposed(#1) | EntityDisposed(#1) | EntityDisposed(#2) | EntityDisposed(#3) | "
                + "Removed(Transform #3 on #3) | "
                + "Removed(Transform #2 on #2) | Removed(AudioSource music-loop on #2) | "
                + "Removed(Transform #1 on #1)");
            report.Check("leg B: the recursion TERMINATES (per-entity guard), EntityDisposed total", Starting(log, "EntityDisposed"), 4);
            report.Check("leg B: exactly one Removed(Transform) per entity", Starting(log, "Removed(Transform"), 3);
            report.CheckTrue("leg B: every carrier is dead once Dispose returned", AllDead(carriers.ToArray()));
            report.Note("DefaultEcs 0.18.0-beta01 does NOT terminate on this leg: the entity reads IsAlive == true " +
                        "inside its own EntityDisposed handler and disposing it republishes with no re-entrancy " +
                        "guard, so the harness's depth-capped run re-enters the same entity with unchanged state " +
                        "at every level (measured: `sweeps stopped by the depth cap` > 0, and the state that would " +
                        "end the recursion never changes). Uncapped it therefore cannot terminate — that is an " +
                        "inference from the measurement, not a recorded stack overflow. The facade's per-entity " +
                        "_disposing guard bounds it. That is a deliberate improvement wave 1 must keep, and the " +
                        "only one on this leg.");
        }

        // --- leg C: item 41's CREATE half at teardown --------------------------------------------
        //
        // S6 exercises Dispose+Create+Publish inside a singleton dispatch during play; leg A covers
        // the Dispose half at teardown. This is the Create half: a handler that spawns while the
        // cascade is already walking. The snapshot the cascade took cannot contain the newborns, so
        // the question is whether they are reported at all — and DefaultEcs was measured (M5b) to
        // fire their Added and nothing else, then free them silently.
        {
            var world = EcsWorld.Create();
            var log = new List<string>();
            var born = new List<Entity>();

            world.SubscribeEntityDisposed((in Entity entity) => log.Add($"EntityDisposed({Mark(entity)})"));
            world.SubscribeEntityComponentAdded((in Entity _, in Transform value) => log.Add($"Added(Transform #{value.X})"));
            world.SubscribeEntityComponentRemoved((in Entity entity, in Transform value) =>
                log.Add($"Removed(Transform #{value.X} on {Mark(entity)})"));
            world.SubscribeWorldComponentRemoved((EcsWorld w, in LevelData __) =>
            {
                log.Add("WorldComponentRemoved(LevelData)");
                for (var i = 71; i <= 73; i++)
                {
                    var newborn = w.CreateEntity();
                    newborn.Set(new Transform { X = i, Y = i });
                    born.Add(newborn);
                }
            });

            for (var i = 1; i <= 2; i++)
            {
                var carrier = world.CreateEntity();
                carrier.Set(new Transform { X = i, Y = i });
            }

            world.Set(new LevelData { Identifier = "Level_0" });
            log.Clear();
            world.Dispose();

            var sequence = string.Join(" | ", log);
            report.Observe("leg C event sequence", sequence);

            report.Check("leg C: the FULL sequence when a teardown handler CREATES entities", sequence,
                "EntityDisposed(#1) | EntityDisposed(#2) | "
                + "Removed(Transform #1 on #1) | Removed(Transform #2 on #2) | "
                + "WorldComponentRemoved(LevelData) | "
                + "Added(Transform #71) | Added(Transform #72) | Added(Transform #73)");
            report.Check("leg C: the newborns fire their Added normally", Starting(log, "Added(Transform"), 3);
            report.Check("leg C: the newborns are NEVER reported by the cascade", Starting(log, "EntityDisposed"), 2);
            report.CheckTrue("leg C: the newborns are dead once Dispose returned", AllDead(born.ToArray()));
            report.Note("Teardown-time CREATION is cascade-silent on both backends: the create-time Added fires, " +
                        "the entity is then freed with the world and gets no EntityDisposed and no " +
                        "ComponentRemoved. DefaultEcs 0.18.0-beta01 does the same (M5b: 4 pre-existing carriers " +
                        "give 4 EntityDisposed and 4 Removed, the 3 newborns give 3 Added and nothing else, and " +
                        "none of them survives the Dispose). Item 41's Create half therefore has a measured " +
                        "answer at teardown, not only during play.");
        }
    }

    // ===========================================================================================
    // S9 — the OTHER re-entrancy: a handler that calls world.Dispose from inside a disposal.
    //
    // S8's legs re-enter entity disposal; these re-enter WORLD disposal, from both directions. Six
    // engine sites call world.Dispose (screen teardown, Game.UnloadContent, the editor's world
    // swap), so a handler that reaches a second one is a live shape rather than a thought
    // experiment.
    //
    //   leg A — world.Dispose from inside world.Dispose. Without a guard set BEFORE the cascade the
    //           second call passes the `_disposed` check (which only flips in the finally),
    //           re-snapshots a still-live world and replays every event — measured at 3x on two
    //           carriers before a probe cap stopped it.
    //   leg B — world.Dispose from inside a NORMAL entity.Dispose (the crossed direction). The
    //           nested teardown runs to completion, destroying the Arch world, and then the outer
    //           DisposeEntity resumes: without a guard it goes on to bump versions and call
    //           _arch.Destroy against a world that no longer exists.
    // ===========================================================================================
    private static void WorldTeardownWithWorldDisposingHandler(ProofReport report)
    {
        report.Scenario("S9 — a handler that calls world.Dispose DURING a disposal (re-entrancy, both directions)");

        // --- leg A: world.Dispose from inside world.Dispose ---------------------------------------
        {
        var world = EcsWorld.Create();
        var log = new List<string>();
        var nestedCalls = 0;
        var probeCapHits = 0;
        var depth = 0;
        const int MaxProbeDepth = 4;

        world.SubscribeEntityDisposed((in Entity entity) =>
        {
            log.Add($"EntityDisposed({Mark(entity)})");

            // The probe cap exists only so an UNGUARDED facade stays runnable — with the guard in
            // place it is never reached, and `probeCapHits == 0` is the assertion that says so.
            if (depth >= MaxProbeDepth)
            {
                probeCapHits++;
                return;
            }

            depth++;
            nestedCalls++;
            world.Dispose();
            depth--;
        });
        world.SubscribeEntityComponentRemoved((in Entity entity, in Transform value) =>
            log.Add($"Removed(Transform #{value.X} on {Mark(entity)})"));

        var carriers = new List<Entity>();
        for (var i = 1; i <= 2; i++)
        {
            var carrier = world.CreateEntity();
            carrier.Set(new Transform { X = i, Y = i });
            carriers.Add(carrier);
        }

        world.Dispose();

        var sequence = string.Join(" | ", log);
        report.Observe("leg A event sequence", sequence);
        report.Observe("nested world.Dispose calls made by the handler", nestedCalls);

        report.Check("leg A: the nested Dispose is a NO-OP — one event per entity", sequence,
            "EntityDisposed(#1) | EntityDisposed(#2) | "
            + "Removed(Transform #1 on #1) | Removed(Transform #2 on #2)");
        report.Check("leg A: the handler really did re-enter Dispose", nestedCalls, 2);
        report.Check("leg A: the probe cap was never needed (the guard terminated it)", probeCapHits, 0);
        report.CheckTrue("leg A: every carrier is dead once Dispose returned", AllDead(carriers.ToArray()));
        report.Check("leg A: the world is fully torn down", world.SubscriberCount, 0);
        report.Note("PARITY, not an invention: M7 measured DefaultEcs 0.18.0-beta01 on this exact shape — 2 " +
                    "carriers, 2 EntityDisposed, 2 ComponentRemoved, the depth cap never hit. The guard has to " +
                    "be a flag set BEFORE the cascade and separate from `_disposed`, because entities must still " +
                    "read IsAlive == true inside their own teardown handlers (S8 leg A asserts that) and IsAlive " +
                    "keys on `_disposed`.");
        }

        // --- leg B: the CROSSED direction — world.Dispose from inside a normal entity.Dispose -----
        //
        // Nothing in the engine forbids it: an EntityDisposed handler that decides the screen is
        // over calls world.Dispose, and the entity whose disposal it is sitting inside is still
        // mid-flight. The nested teardown runs to COMPLETION — it destroys the Arch world — and
        // then the outer DisposeEntity resumes with an Arch world that no longer exists.
        {
            var world = EcsWorld.Create();
            var log = new List<string>();
            var nested = 0;

            world.SubscribeEntityDisposed((in Entity entity) =>
            {
                log.Add($"EntityDisposed({Mark(entity)})");
                nested++;
                world.Dispose();
            });
            world.SubscribeEntityComponentRemoved((in Entity entity, in Transform value) =>
                log.Add($"Removed(Transform #{value.X} on {Mark(entity)})"));

            var carriers = new List<Entity>();
            for (var i = 1; i <= 2; i++)
            {
                var carrier = world.CreateEntity();
                carrier.Set(new Transform { X = i, Y = i });
                carriers.Add(carrier);
            }

            var threw = "returned normally";
            try
            {
                carriers[0].Dispose();   // a NORMAL entity disposal — the world teardown rides it
            }
            catch (Exception exception)
            {
                threw = exception.GetType().Name + ": " + exception.Message;
            }

            var sequence = string.Join(" | ", log);
            report.Observe("leg B event sequence", sequence);
            report.Check("leg B: entity.Dispose returns normally even though the world went with it", threw,
                "returned normally");

            // The first EntityDisposed is the outer entity.Dispose; the teardown it triggers then
            // reports the SAME entity again (it is still alive at that point — S8 leg A's parity)
            // and its sibling, before the component channel runs for both.
            report.Check("leg B: the FULL sequence of the crossed teardown", sequence,
                "EntityDisposed(#1) | EntityDisposed(#1) | EntityDisposed(#2) | "
                + "Removed(Transform #1 on #1) | Removed(Transform #2 on #2)");
            report.Check("leg B: the handler re-entered from both entities", nested, 3);
            report.CheckTrue("leg B: every carrier is dead once it returned", AllDead(carriers.ToArray()));
            report.Check("leg B: the world is fully torn down", world.SubscriberCount, 0);
            report.Check("leg B: and it holds no query registrations", world.QueryCount, 0);

            // The surface a resumed DisposeEntity would have touched, now that the world is gone:
            // before the guard, CreateEntity handed back a dead handle and a singleton Set stored a
            // value on a destroyed world (Has<T>() == true, and nothing left to ever remove it).
            report.Throws<InvalidOperationException>("leg B: CreateEntity on the disposed world throws",
                () => world.CreateEntity());
            report.Throws<InvalidOperationException>("leg B: a singleton Set on the disposed world throws",
                () => world.Set(new LevelData { Identifier = "after-teardown" }));
            report.Check("leg B: ...so no singleton survives the teardown", world.Has<LevelData>(), false);

            report.Note("The crossed direction needs its own guard: leg A's `_tearingDown` flag stops a nested " +
                        "world.Dispose, but here the nested teardown COMPLETED, so what has to stop is the outer " +
                        "DisposeEntity — it keys on `_disposed` and returns before touching Arch. It cannot key on " +
                        "`_tearingDown`: disposing entities from inside a running teardown is the engine's own " +
                        "unload sweep, which S8 leg A asserts must still double-fire.");
            report.Note("PARITY on the sequence, a GUARANTEE on the aftermath: M8 ran this shape on DefaultEcs " +
                        "0.18.0-beta01 and logged the same five events in the same order (EntityDisposed twice for " +
                        "the outer entity, once for its sibling, then both components). Afterwards DefaultEcs " +
                        "answers every one of these reads with a NullReferenceException — `Entity.IsAlive`, " +
                        "`world.CreateEntity()` and `world.Set<T>` alike — so the facade's typed " +
                        "InvalidOperationException is the same refusal with a message, in the same family as the " +
                        "post-teardown query read it defines in M6.");
        }
    }

    // ===========================================================================================
    // S10 — the teardown order is ONE list of channels in subscription order, across BOTH legs.
    //
    // S7 subscribes EntityDisposed, then the entity components, then the world components, so its
    // asserted sequence agrees with every candidate rule at once: "three phases", "world components
    // last", "one channel list". M4d took those apart on DefaultEcs 0.18.0-beta01 and only the last
    // survives — subscribing a world component FIRST makes it fire first, subscribing
    // EntityDisposed LAST makes it fire last, and a type subscribed on BOTH legs takes TWO slots,
    // each keeping its own leg's order.
    //
    // This scenario is that fixture on the facade: subscriptions deliberately interleaved, and
    // LevelData subscribed on both legs (a world singleton AND a component on the carrier). It is
    // also the regression for the shared type-id registry the facade used to keep: re-minting on
    // the second leg reordered the world leg and collided the next type's id.
    // ===========================================================================================
    private static void TeardownChannelOrderAcrossLegs(ProofReport report)
    {
        report.Scenario("S10 — teardown walks ONE channel list in subscription order, both legs interleaved");

        var world = EcsWorld.Create();
        var log = new List<string>();

        // Subscription order: world LevelData, entity Transform, entity LevelData, world
        // CurrentLevel, EntityDisposed. Note LevelData takes a slot on EACH leg, and EntityDisposed
        // takes the LAST one.
        world.SubscribeWorldComponentRemoved((EcsWorld _, in LevelData value) =>
            log.Add($"World(LevelData {value.Identifier})"));
        world.SubscribeEntityComponentRemoved((in Entity entity, in Transform value) =>
            log.Add($"Entity(Transform #{value.X} on {Mark(entity)})"));
        world.SubscribeEntityComponentRemoved((in Entity entity, in LevelData value) =>
            log.Add($"Entity(LevelData {value.Identifier} on {Mark(entity)})"));
        world.SubscribeWorldComponentRemoved((EcsWorld _, in CurrentLevel value) =>
            log.Add($"World(CurrentLevel {value.Identifier})"));
        world.SubscribeEntityDisposed((in Entity entity) => log.Add($"EntityDisposed({Mark(entity)})"));

        var carrier = world.CreateEntity();
        carrier.Set(new Transform { X = 1f, Y = 1f });
        carrier.Set(new LevelData { Identifier = "on-entity" });   // the SAME type, on the entity leg

        world.Set(new CurrentLevel { Identifier = "Level_0" });
        world.Set(new LevelData { Identifier = "singleton" });

        world.Dispose();

        var sequence = string.Join(" | ", log);
        report.Observe("S10 event sequence", sequence);

        report.Check("S10: the cascade follows SUBSCRIPTION order across both legs", sequence,
            "World(LevelData singleton) | Entity(Transform #1 on #1) | "
            + "Entity(LevelData on-entity on #1) | World(CurrentLevel Level_0) | EntityDisposed(#1)");
        report.CheckTrue("S10: a world component can fire FIRST — 'world components last' is not a rule",
            sequence.StartsWith("World(LevelData", StringComparison.Ordinal));
        report.CheckTrue("S10: EntityDisposed is a channel, not a phase — subscribed last, fired last",
            sequence.EndsWith("EntityDisposed(#1)", StringComparison.Ordinal));
        report.CheckTrue("S10: a type on BOTH legs keeps each leg's own slot (no re-mint, no collision)",
            sequence.IndexOf("World(LevelData", StringComparison.Ordinal)
            < sequence.IndexOf("Entity(Transform", StringComparison.Ordinal)
            && sequence.IndexOf("Entity(Transform", StringComparison.Ordinal)
            < sequence.IndexOf("Entity(LevelData", StringComparison.Ordinal));
        report.Check("S10: the disposed world holds no subscriptions", world.SubscriberCount, 0);
        report.Note("Measured on DefaultEcs 0.18.0-beta01 by M4d, on the same three questions: world-leg-first " +
                    "(world A/B), EntityDisposed-is-a-channel (world C) and entity.Dispose following the same " +
                    "list (world D). Wave 1 inherits the rule as ONE order — this world's subscription order — " +
                    "not as three phases, and item 40's 'EntityDisposed then ComponentRemoved' has to be read as " +
                    "the order that fixture's subscriptions were made in.");
    }

    // ===========================================================================================
    // S11 — a teardown handler that THROWS.
    //
    // The Dispose doc claims the try/finally exists so a throwing handler cannot leave a half-torn
    // world behind that replays the cascade on the next call. That much is asserted here — but the
    // other half of the shape had never been measured: what happens to the REST of the cascade.
    // It is dropped, silently, while teardown still completes and the exception escapes to the
    // caller. For the engine that means AudioSystem.OnAudioSourceRemoved never cuts the remaining
    // loops and the LDtk sweep never runs, on a world that is nevertheless gone.
    // ===========================================================================================
    private static void WorldTeardownWithThrowingHandler(ProofReport report)
    {
        report.Scenario("S11 — a handler that THROWS during world.Dispose (the rest of the cascade is dropped)");

        var world = EcsWorld.Create();
        var log = new List<string>();
        var carriers = new List<Entity>();

        world.SubscribeEntityDisposed((in Entity entity) =>
        {
            log.Add($"EntityDisposed({Mark(entity)})");
            if (entity.Has<Transform>() && entity.Get<Transform>().X == 2f)
            {
                throw new InvalidOperationException("teardown handler failed on carrier #2");
            }
        });
        world.SubscribeEntityComponentRemoved((in Entity entity, in Transform value) =>
            log.Add($"Removed(Transform #{value.X} on {Mark(entity)})"));
        world.SubscribeWorldComponentRemoved((EcsWorld _, in LevelData __) => log.Add("WorldComponentRemoved(LevelData)"));

        for (var i = 1; i <= 3; i++)
        {
            var carrier = world.CreateEntity();
            carrier.Set(new Transform { X = i, Y = i });
            carriers.Add(carrier);
        }

        world.Set(new LevelData { Identifier = "Level_0" });
        var held = world.GetEntities().With<Transform>().AsSet();

        report.Throws<InvalidOperationException>("S11: the handler's exception ESCAPES world.Dispose",
            () => world.Dispose());

        var sequence = string.Join(" | ", log);
        report.Observe("S11 event sequence", sequence);

        report.Check("S11: the cascade stops at the throw — nothing after it fires", sequence,
            "EntityDisposed(#1) | EntityDisposed(#2)");
        report.Check("S11: the third carrier is never reported", Starting(log, "EntityDisposed(#3)"), 0);
        report.Check("S11: not one ComponentRemoved fires", Starting(log, "Removed(Transform"), 0);
        report.Check("S11: WorldComponentRemoved never fires either", Starting(log, "WorldComponentRemoved"), 0);

        // ...and yet the world IS torn down: that is the finally, and it is what stops the next
        // Dispose from replaying every handler that already ran.
        report.CheckTrue("S11: every carrier is dead anyway (the finally completed)", AllDead(carriers.ToArray()));
        report.Check("S11: the disposed world holds no subscriptions", world.SubscriberCount, 0);
        report.Check("S11: a query held across the failed teardown reads EMPTY", held.GetEntities().Length, 0);

        var afterThrow = log.Count;
        world.Dispose();
        report.Check("S11: a second Dispose replays NOTHING", log.Count, afterThrow);
        report.Note("PARTIAL PARITY, measured: M9 ran this fixture on DefaultEcs 0.18.0-beta01 and got the same " +
                    "partial cascade — the exception escapes, 2 EntityDisposed, 0 ComponentRemoved, 0 " +
                    "WorldComponentRemoved. What DIFFERS is the aftermath: DefaultEcs leaves all 3 carriers " +
                    "ALIVE with a stale set of 3, and its second Dispose is silent, so the world is stuck " +
                    "half-torn-down forever. The facade's finally completes the teardown instead — a deliberate " +
                    "guarantee, like the post-teardown query read (M6), and one wave 1 must keep or reject " +
                    "explicitly.");
        report.Note("Wave 1 still has to CHOOSE the cascade policy, and item 50's restatement is where it says " +
                    "so: keep FAIL-FAST (this shape) or AGGREGATE (run every channel, throw an " +
                    "AggregateException at the end). Fail-fast is what the engine gets today by accident; under " +
                    "it a throwing AudioSystem.OnAudioSourceRemoved silently cancels the LDtk sweep, and " +
                    "vice-versa, at every screen change.");
    }

    // ------------------------------------------------------------------------------------ helpers

    private static string Last(List<string> log) => log.Count == 0 ? "<none>" : log[log.Count - 1];

    /// <summary>
    /// Identity for the teardown log — the entity's OWN marker, carried in <c>Transform.X</c>. A
    /// handler that discards the entity (or logs only a hash) cannot tell "creation order" from
    /// "entity-id order" apart, which is exactly what S7's fixture exists to distinguish.
    /// </summary>
    private static string Mark(in Entity entity) =>
        entity.IsAlive && entity.Has<Transform>() ? $"#{entity.Get<Transform>().X}" : "#?";

    private static bool Contains(EntityQuery query, in Entity entity)
    {
        foreach (var member in query.GetEntities())
        {
            if (member == entity) return true;
        }

        return false;
    }

    private static bool Same(Entity[] left, Entity[] right)
    {
        if (left.Length != right.Length) return false;
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i]) return false;
        }

        return true;
    }

    private static bool AllDead(Entity[] entities)
    {
        foreach (var entity in entities)
        {
            if (entity.IsAlive) return false;
        }

        return true;
    }

    private static int Occurrences(List<string> log, string value)
    {
        var count = 0;
        foreach (var line in log)
        {
            if (line == value) count++;
        }

        return count;
    }

    private static List<string> Filtered(List<string> log, string prefix)
    {
        var kept = new List<string>();
        foreach (var line in log)
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal)) kept.Add(line);
        }

        return kept;
    }

    private static int Starting(List<string> log, string prefix)
    {
        var count = 0;
        foreach (var line in log)
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal)) count++;
        }

        return count;
    }
}
