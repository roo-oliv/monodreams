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
            report.Note("Iteration order is therefore not merely 'unspecified' under Arch — it is REVERSED " +
                        "relative to DefaultEcs' insertion-ish order. Every first-match-and-break pick (items 48, " +
                        "58) and every membership sweep written to disk (items 70, 74) would flip. The facade " +
                        "sorts its own enumeration by id; nothing below inherits Arch's order.");
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
            report.Check("Dispose fires EntityDisposed first", log.Count > 0 ? log[0] : "<none>", "EntityDisposed");
            report.Check("Dispose then fires ComponentRemoved per component", log.Count, 2);
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
    // ===========================================================================================
    private static void WorldTeardownCascade(ProofReport report)
    {
        report.Scenario("S7 — world.Dispose fires the full cascade (contract item 50 says event-silent; DefaultEcs measures otherwise)");

        var world = EcsWorld.Create();
        var log = new List<string>();

        world.SubscribeEntityDisposed((in Entity _) => log.Add("EntityDisposed"));
        world.SubscribeEntityComponentRemoved((in Entity _, in Transform __) => log.Add("ComponentRemoved(Transform)"));
        world.SubscribeEntityComponentRemoved((in Entity _, in AudioSource value) => log.Add($"ComponentRemoved(AudioSource {value.Cue})"));
        world.SubscribeWorldComponentRemoved((EcsWorld _, in LevelData __) => log.Add("WorldComponentRemoved(LevelData)"));

        for (var i = 0; i < 3; i++) world.CreateEntity().Set(new Transform { X = i, Y = i });

        var carrier = world.CreateEntity();
        carrier.Set(new AudioSource { Cue = "music-loop", Looping = true });
        world.Set(new LevelData { Identifier = "Level_0" });

        world.Dispose();

        report.Observe("event sequence", string.Join(", ", log));
        report.Check("EntityDisposed per live entity", Occurrences(log, "EntityDisposed"), 4);
        report.Check("ComponentRemoved per struct component", Occurrences(log, "ComponentRemoved(Transform)"), 3);
        report.Check("ComponentRemoved for the managed carrier (AudioSystem.cs:133-137 runs)", Occurrences(log, "ComponentRemoved(AudioSource music-loop)"), 1);
        report.Check("WorldComponentRemoved for the singleton", Occurrences(log, "WorldComponentRemoved(LevelData)"), 1);
        report.Check("total events on teardown (item 50 predicts 0)", log.Count, 9);
        report.Note("Item 50 must be restated before wave 1 writes its contract test: DefaultEcs 0.18.0-beta01 " +
                    "fires this same 9-event cascade, so 'event-silent' would be a behaviour CHANGE, not parity.");
    }

    // ------------------------------------------------------------------------------------ helpers

    private static string Last(List<string> log) => log.Count == 0 ? "<none>" : log[log.Count - 1];

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
}
