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
    }

    // ---------------------------------------------------------------------------------------
    // M4 (contract item 50) — is world.Dispose event-silent?
    //
    // Item 50 asserts it is ("no per-component/singleton Removed, no cascade ... matching
    // DefaultEcs"). The adjacent-facts pass said otherwise, so this measures it on its own world
    // with every reactive verb wired, several carriers, and the entity liveness the handler sees.
    // ---------------------------------------------------------------------------------------
    private static void MeasureWorldDisposeEventSilence()
    {
        Section("M4 (item 50) — world.Dispose: event-silent, or does it fire the reactive verbs?");

        var log = new List<string>();
        var world = new World();

        world.SubscribeEntityDisposed((in Entity e) => log.Add($"EntityDisposed(#{e.GetHashCode()})"));
        world.SubscribeEntityComponentRemoved((in Entity e, in EntityMarker v) => log.Add($"ComponentRemoved({v.Value}, IsAlive={e.IsAlive})"));
        world.SubscribeEntityComponentRemoved((in Entity _, in ManagedPayload v) => log.Add($"ComponentRemoved(managed:{v.Name})"));
        world.SubscribeWorldComponentRemoved((World _, in WorldMarker v) => log.Add($"WorldComponentRemoved({v.Value})"));

        for (var i = 1; i <= 3; i++)
        {
            var e = world.CreateEntity();
            e.Set(new EntityMarker { Value = i });
        }

        var withManaged = world.CreateEntity();
        withManaged.Set(new ManagedPayload { Name = "loop" }); // AudioSourceComponent's shape
        world.Set(new WorldMarker { Value = 99 });

        // An entity disposed BEFORE teardown must not be reported twice.
        var early = world.CreateEntity();
        early.Set(new EntityMarker { Value = 4 });
        early.Dispose();

        Report("events fired by the pre-teardown entity.Dispose", Render(log));
        log.Clear();

        world.Dispose();

        Report("events fired by world.Dispose", Render(log));
        Report("=> world.Dispose IS EVENT-SILENT", log.Count == 0 ? "YES" : "NO");
    }

    // ---------------------------------------------------------------------------------------

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("-- " + title);
    }

    private static void Report(string label, object value) => Console.WriteLine($"   {label,-62} : {value}");

    private static string Render(List<string> log) => log.Count == 0 ? "(none)" : $"{log.Count}x [{string.Join(", ", log)}]";

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
