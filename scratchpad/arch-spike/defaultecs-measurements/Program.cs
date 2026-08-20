using System;
using System.Collections.Generic;
using DefaultEcs;

namespace MonoDreams.ArchSpike.Measurements;

/// <summary>
/// Wave-0 measurement harness for issue #119 (contract items 42 and 66).
///
/// The wave-1 facade contract tests pin behaviour the facade must reproduce. Three of those pins
/// cannot be written from memory — they are properties of DefaultEcs 0.18.0-beta01 that have to be
/// observed:
///
///   M1 (item 66) — does <c>SubscribeWorldComponentAdded&lt;T&gt;</c> replay for a world component
///                  that is ALREADY set when the subscription is made?
///   M2 (item 42) — does <c>SubscribeEntityComponentAdded&lt;T&gt;</c> replay for components that
///                  are ALREADY present on live entities when the subscription is made?
///   M3 (item 6 / D14) — what does <c>entity.NotifyChanged&lt;T&gt;()</c> do when T is ABSENT?
///
/// Each headline measurement ships with the control cases needed to READ it (subscribe-then-Set,
/// Set-over-present, notify-over-present), because "the handler did not fire" is only meaningful
/// next to a case where it did. Everything printed here is an observation, never an assertion:
/// this program has no expected output baked in and cannot "fail" — it reports.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        Console.WriteLine("== DefaultEcs 0.18.0-beta01 — wave-0 measurements (issue #119, items 42/66) ==");
        Console.WriteLine();

        MeasureWorldComponentSubscribeReplay();
        MeasureEntityComponentSubscribeReplay();
        MeasureNotifyChangedOnAbsent();
        MeasureAdjacentFacts();

        Console.WriteLine();
        Console.WriteLine("== end of measurements ==");
        return 0;
    }

    // ---------------------------------------------------------------------------------------
    // M1 (contract item 66) — world-component subscribe replay.
    // ---------------------------------------------------------------------------------------
    private static void MeasureWorldComponentSubscribeReplay()
    {
        Section("M1 (item 66) — SubscribeWorldComponentAdded over an ALREADY-Set world component");

        using (var world = new World())
        {
            var log = new List<string>();

            world.Set(new WorldMarker { Value = 1 });
            Report("world.Has<WorldMarker>() before subscribing", world.Has<WorldMarker>());

            using (world.SubscribeWorldComponentAdded((World _, in WorldMarker v) => log.Add($"Added({v.Value})")))
            using (world.SubscribeWorldComponentChanged((World _, in WorldMarker o, in WorldMarker n) => log.Add($"Changed({o.Value}->{n.Value})")))
            {
                Report("handler calls fired BY THE SUBSCRIPTION ITSELF (replay)", Render(log));
                Report("=> REPLAY-ON-SUBSCRIBE (world component)", log.Count > 0 ? "YES" : "NO");
            }
        }

        // Control: subscribe FIRST, then Set — proves the handler is wired and would have fired.
        using (var world = new World())
        {
            var log = new List<string>();
            using (world.SubscribeWorldComponentAdded((World _, in WorldMarker v) => log.Add($"Added({v.Value})")))
            {
                world.Set(new WorldMarker { Value = 7 });
                Report("CONTROL subscribe-then-Set", Render(log));
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // M2 (contract item 42) — entity-component subscribe replay.
    // ---------------------------------------------------------------------------------------
    private static void MeasureEntityComponentSubscribeReplay()
    {
        Section("M2 (item 42) — SubscribeEntityComponentAdded over ALREADY-present entity components");

        using (var world = new World())
        {
            var log = new List<string>();

            var a = world.CreateEntity();
            a.Set(new EntityMarker { Value = 1 });
            var b = world.CreateEntity();
            b.Set(new EntityMarker { Value = 2 });
            var bare = world.CreateEntity(); // alive, no EntityMarker — must never be reported

            using (world.SubscribeEntityComponentAdded((in Entity _, in EntityMarker v) => log.Add($"Added({v.Value})")))
            {
                Report("handler calls fired BY THE SUBSCRIPTION ITSELF (replay)", Render(log));
                Report("=> REPLAY-ON-SUBSCRIBE (entity component)", log.Count > 0 ? "YES" : "NO");
                Report("entities carrying the component at subscribe time", "2 (values 1,2) + 1 bare entity");

                log.Clear();
                var c = world.CreateEntity();
                c.Set(new EntityMarker { Value = 3 });
                Report("CONTROL add-after-subscribe", Render(log));
            }

            GC.KeepAlive(bare);
        }

        // Same question for the OTHER two entity-level verbs, so wave 1 knows whether replay is a
        // property of Added alone or of the whole reactive family.
        using (var world = new World())
        {
            var changedLog = new List<string>();
            var removedLog = new List<string>();

            var e = world.CreateEntity();
            e.Set(new EntityMarker { Value = 9 });

            using (world.SubscribeEntityComponentChanged((in Entity _, in EntityMarker o, in EntityMarker n) => changedLog.Add($"Changed({o.Value}->{n.Value})")))
            using (world.SubscribeEntityComponentRemoved((in Entity _, in EntityMarker v) => removedLog.Add($"Removed({v.Value})")))
            {
                Report("SubscribeEntityComponentChanged replay", Render(changedLog));
                Report("SubscribeEntityComponentRemoved replay", Render(removedLog));
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // M3 (contract item 6 / D14) — NotifyChanged on an absent component.
    // ---------------------------------------------------------------------------------------
    private static void MeasureNotifyChangedOnAbsent()
    {
        Section("M3 (item 6 / D14) — entity.NotifyChanged<T>() when T is ABSENT");

        using var world = new World();
        var log = new List<string>();
        using var _ = world.SubscribeEntityComponentChanged((in Entity _, in EntityMarker o, in EntityMarker n) => log.Add($"Changed({o.Value}->{n.Value})"));

        var entity = world.CreateEntity();
        Report("entity.Has<EntityMarker>()", entity.Has<EntityMarker>());

        try
        {
            entity.NotifyChanged<EntityMarker>();
            Report("=> OUTCOME", "NO THROW (silent)");
            Report("handler calls", Render(log));
        }
        catch (Exception ex)
        {
            Report("=> OUTCOME", "THROWS");
            Report("exception type", ex.GetType().FullName);
            Report("exception message", ex.Message);
            Report("handler calls before the throw", Render(log));
        }

        // Control: the same call on a PRESENT component, which is the shape ~40 engine sites use.
        var present = world.CreateEntity();
        present.Set(new EntityMarker { Value = 42 });
        log.Clear();
        present.NotifyChanged<EntityMarker>();
        Report("CONTROL NotifyChanged on present", Render(log));

        // Control: managed component — AudioSystem.cs:141 keys on ReferenceEquals(old, new).
        var managedLog = new List<string>();
        using var __ = world.SubscribeEntityComponentChanged((in Entity _, in ManagedPayload o, in ManagedPayload n) =>
            managedLog.Add($"Changed(ReferenceEquals(old,new)={ReferenceEquals(o, n)})"));

        var carrier = world.CreateEntity();
        carrier.Set(new ManagedPayload { Name = "first" });
        carrier.NotifyChanged<ManagedPayload>();
        carrier.Set(new ManagedPayload { Name = "second" });
        Report("CONTROL managed NotifyChanged then Set(new instance)", Render(managedLog));
    }

    // ---------------------------------------------------------------------------------------
    // Adjacent facts the three headline pins are read against (cheap, same harness).
    // ---------------------------------------------------------------------------------------
    private static void MeasureAdjacentFacts()
    {
        Section("Adjacent facts (read the headline pins against these)");

        using (var world = new World())
        {
            var log = new List<string>();
            using var _ = world.SubscribeWorldComponentAdded((World _, in WorldMarker v) => log.Add($"Added({v.Value})"));
            using var __ = world.SubscribeWorldComponentChanged((World _, in WorldMarker o, in WorldMarker n) => log.Add($"Changed({o.Value}->{n.Value})"));
            using var ___ = world.SubscribeWorldComponentRemoved((World _, in WorldMarker v) => log.Add($"Removed({v.Value})"));

            world.Set(new WorldMarker { Value = 1 });
            Report("world.Set when ABSENT", Render(log));

            log.Clear();
            world.Set(new WorldMarker { Value = 2 });
            Report("world.Set when PRESENT (CORE_TENETS §9 Restart shape)", Render(log));

            log.Clear();
            world.Remove<WorldMarker>();
            Report("world.Remove when PRESENT", Render(log));

            log.Clear();
            world.Remove<WorldMarker>();
            Report("world.Remove when ABSENT (LDtkLevelLoadSystem.cs:71-82 first load)", Render(log));

            log.Clear();
            world.Set(new WorldMarker { Value = 3 });
            Report("world.Set after Remove (Added-keyed LDtk parsers re-trigger?)", Render(log));
        }

        using (var world = new World())
        {
            var log = new List<string>();
            using var _ = world.SubscribeEntityComponentAdded((in Entity _, in EntityMarker v) => log.Add($"Added({v.Value})"));
            using var __ = world.SubscribeEntityComponentChanged((in Entity _, in EntityMarker o, in EntityMarker n) => log.Add($"Changed({o.Value}->{n.Value})"));
            using var ___ = world.SubscribeEntityComponentRemoved((in Entity _, in EntityMarker v) => log.Add($"Removed({v.Value})"));

            var e = world.CreateEntity();
            e.Set(new EntityMarker { Value = 1 });
            Report("entity.Set when ABSENT", Render(log));

            log.Clear();
            e.Set(new EntityMarker { Value = 2 });
            Report("entity.Set when PRESENT (add-or-update, H1)", Render(log));

            log.Clear();
            e.Remove<EntityMarker>();
            Report("entity.Remove when PRESENT", Render(log));

            log.Clear();
            e.Remove<EntityMarker>();
            Report("entity.Remove when ABSENT", Render(log));

            log.Clear();
            var doomed = world.CreateEntity();
            doomed.Set(new EntityMarker { Value = 5 });
            log.Clear();
            doomed.Dispose();
            Report("entity.Dispose with a present component (item 40)", Render(log));
        }

        MeasureWorldDisposeEventSilence();
        MeasureWorldDisposeReentrantHandler();
        MeasureEntitySetAfterWorldDispose();
    }

    // ---------------------------------------------------------------------------------------
    // M4 (contract item 50) — is world.Dispose event-silent, and in WHAT ORDER does it fire?
    //
    // Item 50 asserts it is silent ("no per-component/singleton Removed, no cascade ... matching
    // DefaultEcs"). The adjacent-facts pass said otherwise, so this measures it on its own world
    // with every reactive verb wired, several carriers, and the entity liveness the handler sees.
    //
    // The fixture is built so the ORDER is observable instead of degenerate — a cascade grouped by
    // component pool and a cascade walked per entity would log identically on a naive fixture:
    //
    //   * ONE carrier holds TWO subscribed component types (EntityMarker + ManagedPayload),
    //     interleaved between single-component carriers (A, AB, A). Pool-grouped =>
    //     "…Marker, Marker, Marker … Managed"; per-entity => "…Marker, Marker+Managed, Marker".
    //   * an id is RECYCLED before teardown, so creation order and entity-id order DIVERGE and
    //     "EntityDisposed in creation order" becomes a falsifiable claim rather than a tautology.
    //   * every handler logs the entity's OWN marker value, so the sequence carries identity.
    // ---------------------------------------------------------------------------------------
    private static void MeasureWorldDisposeEventSilence()
    {
        Section("M4 (item 50) — world.Dispose: event-silent, or does it fire the reactive verbs?");

        var log = new List<string>();
        var world = new World();

        world.SubscribeEntityDisposed((in Entity e) => log.Add($"EntityDisposed({Identify(e)})"));
        world.SubscribeEntityComponentRemoved((in Entity e, in EntityMarker v) => log.Add($"Removed(Marker {v.Value}, IsAlive={e.IsAlive})"));
        world.SubscribeEntityComponentRemoved((in Entity e, in ManagedPayload v) => log.Add($"Removed(Managed {v.Name} on {Identify(e)})"));
        world.SubscribeWorldComponentRemoved((World _, in WorldMarker v) => log.Add($"WorldComponentRemoved({v.Value})"));

        // --- creation order 1,2,3 ; entity 2 is the TWO-component carrier ------------------------
        var one = world.CreateEntity();
        one.Set(new EntityMarker { Value = 1 });

        var two = world.CreateEntity();
        two.Set(new EntityMarker { Value = 2 });
        two.Set(new ManagedPayload { Name = "music-loop" });   // AudioSourceComponent's shape

        // --- a scratch entity, freed AFTER entity 3 exists so its id is the LOW one still free ----
        var scratch = world.CreateEntity();
        scratch.Set(new EntityMarker { Value = 90 });
        var scratchId = scratch.GetHashCode();

        var three = world.CreateEntity();
        three.Set(new EntityMarker { Value = 3 });

        scratch.Dispose();
        Report("events fired by the pre-teardown entity.Dispose", Render(log));
        log.Clear();

        // Created LAST, but takes the recycled (LOWER) id. That is what makes creation order and
        // entity-id order distinguishable in the teardown log below — without it the two orders
        // coincide by construction and "EntityDisposed in creation order" is unfalsifiable.
        var four = world.CreateEntity();
        four.Set(new EntityMarker { Value = 4 });

        world.Set(new WorldMarker { Value = 99 });

        Report("freed scratch id", scratchId);
        Report("ids by creation order (markers 1,2,3,4)",
            $"{one.GetHashCode()}, {two.GetHashCode()}, {three.GetHashCode()}, {four.GetHashCode()}");
        Report("=> creation order and id order DIVERGE?",
            three.GetHashCode() > four.GetHashCode()
                ? "YES (marker 4 was created last but recycled the lower id — the log below can tell them apart)"
                : "NO (ids ascend with creation — the two orders are NOT distinguishable here)");

        world.Dispose();

        Report("events fired by world.Dispose", Render(log));
        Report("=> world.Dispose IS EVENT-SILENT", log.Count == 0 ? "YES" : "NO");
        Report("=> EntityDisposed order", string.Join(" ", Only(log, "EntityDisposed")));
        Report("=> ComponentRemoved order (pool-grouped vs per-entity)", string.Join(" ", Only(log, "Removed(")));
    }

    // ---------------------------------------------------------------------------------------
    // M5 — does a handler that DISPOSES entities during world.Dispose make the cascade fire twice?
    //
    // The engine has exactly this shape: LDtkTileParserSystem.cs:42 subscribes the world-component
    // Removed leg and CleanupTileEntities (:145-155) mass-calls entity.Dispose(). At world teardown
    // that handler runs while the same entities are being torn down, so the question "does each
    // entity report once, or once per path" is a real facade obligation, not a curiosity.
    // ---------------------------------------------------------------------------------------
    private static void MeasureWorldDisposeReentrantHandler()
    {
        Section("M5 (item 50 / item 41) — a handler that disposes entities DURING world.Dispose");

        foreach (var leg in new[] { "EntityDisposed leg", "WorldComponentRemoved leg (the engine's)" })
        {
            var log = new List<string>();
            var world = new World();
            var carriers = new List<Entity>();

            // The sweep is DEPTH-CAPPED. Uncapped, the EntityDisposed leg overflows the stack:
            // inside the handler the entity still reads IsAlive == true, so the sweep disposes it
            // again, which republishes EntityDisposingMessage, which re-enters the handler — with no
            // re-entrancy guard anywhere in DefaultEcs 0.18.0-beta01. The cap turns that unbounded
            // recursion into an OBSERVATION (`sweeps stopped by the depth cap` > 0 means it recursed
            // and would not have stopped on its own) while keeping this harness runnable.
            const int MaxSweepDepth = 3;
            var sweepDepth = 0;
            var cappedSweeps = 0;

            void SweepAll()
            {
                if (sweepDepth >= MaxSweepDepth)
                {
                    cappedSweeps++;
                    return;
                }

                sweepDepth++;
                foreach (var carrier in carriers)
                {
                    if (carrier.IsAlive) carrier.Dispose();
                }

                sweepDepth--;
            }

            world.SubscribeEntityDisposed((in Entity e) =>
            {
                log.Add($"EntityDisposed({Identify(e)})");
                if (leg.StartsWith("EntityDisposed", StringComparison.Ordinal)) SweepAll();
            });
            world.SubscribeEntityComponentRemoved((in Entity _, in EntityMarker v) => log.Add($"Removed(Marker {v.Value})"));
            world.SubscribeWorldComponentRemoved((World _, in WorldMarker _) =>
            {
                log.Add("WorldComponentRemoved");
                var aliveNow = 0;
                foreach (var carrier in carriers)
                {
                    if (carrier.IsAlive) aliveNow++;
                }

                log.Add($"(entities still alive when the world-component handler ran: {aliveNow})");
                if (leg.StartsWith("WorldComponentRemoved", StringComparison.Ordinal)) SweepAll();
            });

            for (var i = 1; i <= 3; i++)
            {
                var e = world.CreateEntity();
                e.Set(new EntityMarker { Value = i });
                carriers.Add(e);
            }

            world.Set(new WorldMarker { Value = 99 });

            try
            {
                world.Dispose();
                Report($"[{leg}] events", Render(log));
                Report($"[{leg}] EntityDisposed per entity (3 entities)", Count(log, "EntityDisposed") / 3d);
                Report($"[{leg}] Removed(Marker) per entity (3 entities)", Count(log, "Removed(Marker") / 3d);
                Report($"[{leg}] sweeps stopped by the depth cap", cappedSweeps);
            }
            catch (Exception ex)
            {
                Report($"[{leg}] world.Dispose THREW", ex.GetType().FullName + ": " + ex.Message);
                Report($"[{leg}] events before the throw", Render(log));
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // M6 — what does a live EntitySet report AFTER world.Dispose?
    //
    // entity.Dispose drops the entity from every set synchronously (contract item 67, measured
    // above). World teardown is the asymmetric case: the facade's queries have to answer the same
    // way DefaultEcs' sets do, and "the same way" is only knowable by asking.
    // ---------------------------------------------------------------------------------------
    private static void MeasureEntitySetAfterWorldDispose()
    {
        Section("M6 (item 67) — what a live EntitySet reports after world.Dispose");

        var world = new World();
        var set = world.GetEntities().With<EntityMarker>().AsSet();

        for (var i = 1; i <= 3; i++)
        {
            var e = world.CreateEntity();
            e.Set(new EntityMarker { Value = i });
        }

        Report("set.Count with 3 carriers", set.Count);

        var single = world.CreateEntity();
        single.Set(new EntityMarker { Value = 9 });
        Report("set.Count after a 4th carrier joins", set.Count);
        single.Dispose();
        Report("set.Count after ONE entity.Dispose (item 67 baseline)", set.Count);

        world.Dispose();

        try
        {
            Report("set.Count after world.Dispose", set.Count);
            var aliveAfter = 0;
            foreach (var e in set.GetEntities())
            {
                if (e.IsAlive) aliveAfter++;
            }

            Report("of those, still IsAlive", aliveAfter);
        }
        catch (Exception ex)
        {
            Report("reading the set after world.Dispose THREW", ex.GetType().FullName + ": " + ex.Message);
        }
    }

    // ---------------------------------------------------------------------------------------

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("-- " + title);
    }

    private static void Report(string label, object value) => Console.WriteLine($"   {label,-62} : {value}");

    private static string Render(List<string> log) => log.Count == 0 ? "(none)" : $"{log.Count}x [{string.Join(", ", log)}]";

    /// <summary>
    /// Identity for the teardown log: the entity's own marker value AND its DefaultEcs id. Logging
    /// a hash alone cannot tell "creation order" from "entity-id order" apart once an id has been
    /// recycled — which is exactly the ambiguity M4's fixture exists to remove.
    /// </summary>
    private static string Identify(in Entity entity)
    {
        var id = entity.GetHashCode();
        if (entity.IsAlive && entity.Has<EntityMarker>()) return $"#{entity.Get<EntityMarker>().Value}/id{id}";
        return $"#?/id{id}";
    }

    private static List<string> Only(List<string> log, string prefix)
    {
        var kept = new List<string>();
        foreach (var line in log)
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal)) kept.Add(line);
        }

        return kept;
    }

    private static int Count(List<string> log, string prefix)
    {
        var count = 0;
        foreach (var line in log)
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal)) count++;
        }

        return count;
    }

    private struct WorldMarker
    {
        public int Value;
    }

    private struct EntityMarker
    {
        public int Value;
    }

    private sealed class ManagedPayload
    {
        public string Name;
    }
}
