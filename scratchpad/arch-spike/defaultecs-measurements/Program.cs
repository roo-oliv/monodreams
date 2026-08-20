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
        MeasureTeardownPoolOrderDeterminant();
        MeasureTeardownWorldComponentOrder();
        MeasureSharedTypeAcrossLegs();
        MeasureWorldDisposeReentrantHandler();
        MeasureCreationDuringWorldDispose();
        MeasureComponentAddedDuringWorldDispose();
        MeasureWorldDisposeInsideHandler();
        MeasureWorldDisposeInsideEntityDispose();
        MeasureThrowingTeardownHandler();
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
    // M4b — WHAT determines the ComponentRemoved pool order?
    //
    // M4 measured THAT the cascade is pool-grouped, but not what orders the pools. Its own
    // fixture cannot tell: the subscription order, the component-Set order and the process-wide
    // first-touch order all coincide there. That ambiguity is not academic — the engine builds
    // ONE World per screen (LevelSelectionScreen.cs:128, SplashScreen.cs:59), so a facade that
    // mints per-WORLD type ids and an incumbent that mints them process-wide agree on the first
    // screen and diverge on the second.
    //
    // Two worlds in the same process, the SECOND one subscribing (and Setting) in the REVERSED
    // order. Same order in both logs => process-wide; flipped => per-world. PoolAlpha/PoolBeta
    // are touched by nothing else in this harness, so world A really is their first touch.
    // ---------------------------------------------------------------------------------------
    private static void MeasureTeardownPoolOrderDeterminant()
    {
        Section("M4b (item 50) — is the teardown pool order per-WORLD or process-wide first touch?");

        var first = PoolOrderOfOneWorld(alphaFirst: true);
        Report("world A: subscribe+Set PoolAlpha then PoolBeta (first touch of both)", Render(first));

        var second = PoolOrderOfOneWorld(alphaFirst: false);
        Report("world B: subscribe+Set PoolBeta then PoolAlpha (REVERSED, fresh world)", Render(second));

        var sameOrder = first.Count == second.Count && first.Count > 0 && first[0] == second[0];
        Report("=> pool order determinant",
            sameOrder
                ? "PROCESS-WIDE first touch — world B ignored its own subscription/Set order"
                : "PER-WORLD — world B followed its own subscription/Set order");

        // The flip above moves subscription order and Set order TOGETHER, so it settles
        // per-world vs process-wide but not WHICH per-world event mints the order. World C
        // splits them: subscribe Gamma-then-Delta, Set Delta-then-Gamma. The facade mints its
        // type id on the FIRST facade contact of any kind (a subscription counts), so this leg
        // is what says whether that rule matches.
        var split = new List<string>();
        var third = new World();
        third.SubscribeEntityComponentRemoved((in Entity _, in PoolGamma v) => split.Add($"PoolGamma({v.Value})"));
        third.SubscribeEntityComponentRemoved((in Entity _, in PoolDelta v) => split.Add($"PoolDelta({v.Value})"));
        for (var i = 1; i <= 2; i++)
        {
            var carrier = third.CreateEntity();
            carrier.Set(new PoolDelta { Value = i });     // Set order is the REVERSE of subscribe order
            carrier.Set(new PoolGamma { Value = i });
        }

        third.Dispose();
        Report("world C: subscribe Gamma→Delta but Set Delta→Gamma", Render(split));
        Report("=> which per-world event mints the order",
            split.Count > 0 && split[0].StartsWith("PoolGamma", StringComparison.Ordinal)
                ? "SUBSCRIPTION order (subscribing before Setting wins)"
                : "component Set order (first use on an entity)");

        // World C had subscription happen FIRST for both types, so "subscription wins" and "the
        // earlier contact of ANY kind wins" are still the same answer there. World D puts a Set
        // BEFORE any subscription: if Zeta still reports first, the rule is first-contact — which
        // is what the facade's `Channel<T>` mints on.
        var contact = new List<string>();
        var fourth = new World();
        var early = fourth.CreateEntity();
        early.Set(new PoolZeta { Value = 1 });        // FIRST contact with Zeta: a Set
        fourth.SubscribeEntityComponentRemoved((in Entity _, in PoolEpsilon v) => contact.Add($"PoolEpsilon({v.Value})"));
        fourth.SubscribeEntityComponentRemoved((in Entity _, in PoolZeta v) => contact.Add($"PoolZeta({v.Value})"));
        early.Set(new PoolEpsilon { Value = 1 });
        fourth.Dispose();

        Report("world D: Set Zeta, then subscribe Epsilon→Zeta, then Set Epsilon", Render(contact));
        Report("=> the mint is",
            contact.Count > 0 && contact[0].StartsWith("PoolZeta", StringComparison.Ordinal)
                ? "FIRST CONTACT of any kind (Set or Subscribe) — the facade's Channel<T> rule matches"
                : "SUBSCRIPTION order strictly (a pre-subscription Set does not mint)");
    }

    /// <summary>One world's teardown pool order. Subscription order AND component-Set order are
    /// flipped together, so a "per-world" answer stays per-world whichever of the two DefaultEcs
    /// actually keys on — the question here is per-world vs process-wide, not which per-world one.</summary>
    private static List<string> PoolOrderOfOneWorld(bool alphaFirst)
    {
        var log = new List<string>();
        var world = new World();

        void SubscribeAlpha() =>
            world.SubscribeEntityComponentRemoved((in Entity _, in PoolAlpha v) => log.Add($"PoolAlpha({v.Value})"));
        void SubscribeBeta() =>
            world.SubscribeEntityComponentRemoved((in Entity _, in PoolBeta v) => log.Add($"PoolBeta({v.Value})"));

        if (alphaFirst)
        {
            SubscribeAlpha();
            SubscribeBeta();
        }
        else
        {
            SubscribeBeta();
            SubscribeAlpha();
        }

        // Two carriers, so a pool-grouped log is visibly grouped rather than a coincidence of one.
        for (var i = 1; i <= 2; i++)
        {
            var carrier = world.CreateEntity();
            if (alphaFirst)
            {
                carrier.Set(new PoolAlpha { Value = i });
                carrier.Set(new PoolBeta { Value = i });
            }
            else
            {
                carrier.Set(new PoolBeta { Value = i });
                carrier.Set(new PoolAlpha { Value = i });
            }
        }

        world.Dispose();
        return log;
    }

    // ---------------------------------------------------------------------------------------
    // M4c — the order BETWEEN world components at teardown.
    //
    // M4 subscribes exactly ONE world component, so "world components last" is measured but the
    // order among several of them is not. The engine holds four (LDtkLevelDataComponent,
    // CurrentLevelComponent, and the editor/run-state singletons), and they tear down together.
    // ---------------------------------------------------------------------------------------
    private static void MeasureTeardownWorldComponentOrder()
    {
        Section("M4c (item 50) — the order BETWEEN world components at world.Dispose");

        var first = WorldComponentOrderOfOneWorld(bFirst: true);
        Report("world A: subscribe+Set WorldMarkerB then WorldMarkerC (first touch of both)", Render(first));

        var second = WorldComponentOrderOfOneWorld(bFirst: false);
        Report("world B: subscribe+Set WorldMarkerC then WorldMarkerB (REVERSED, fresh world)", Render(second));

        var sameOrder = first.Count == second.Count && first.Count > 0 && first[0] == second[0];
        Report("=> world-component order determinant",
            sameOrder
                ? "PROCESS-WIDE first touch — world B ignored its own subscription/Set order"
                : "PER-WORLD — world B followed its own subscription/Set order");

        // Same first-contact question as M4b's world D, on the world-component leg: Set D before
        // anything subscribes, then subscribe E first.
        var contact = new List<string>();
        var third = new World();
        third.Set(new WorldMarkerD { Value = 4 });     // FIRST contact with D: a Set
        third.SubscribeWorldComponentRemoved((World _, in WorldMarkerE v) => contact.Add($"WorldMarkerE({v.Value})"));
        third.SubscribeWorldComponentRemoved((World _, in WorldMarkerD v) => contact.Add($"WorldMarkerD({v.Value})"));
        third.Set(new WorldMarkerE { Value = 5 });
        third.Dispose();

        Report("world C: Set D, then subscribe E→D, then Set E", Render(contact));
        Report("=> the world-component mint is",
            contact.Count > 0 && contact[0].StartsWith("WorldMarkerD", StringComparison.Ordinal)
                ? "FIRST CONTACT of any kind — the facade's WorldChannel<T> rule matches"
                : "SUBSCRIPTION order strictly (a pre-subscription Set does not mint)");
    }

    // ---------------------------------------------------------------------------------------
    // M4d — ONE component type subscribed on BOTH legs: is the teardown order kept by a single
    // shared registry, or by two independent ones?
    //
    // M4b and M4c each measured one leg in isolation, where the two designs are indistinguishable.
    // They come apart the moment a type is subscribed on both — which the engine does today
    // (`CurrentLevelComponent` is a world singleton, and level-owned entities carry components the
    // same systems subscribe to) — and they come apart LOUDLY on world D below: under one shared
    // registry a type that took its id on the world leg keeps that id on the entity leg, so the
    // entity leg can report it BEFORE a type subscribed earlier on that leg.
    // ---------------------------------------------------------------------------------------
    private static void MeasureSharedTypeAcrossLegs()
    {
        Section("M4d (item 50) — one type on BOTH legs: one shared type-id registry, or two?");

        var log = new List<string>();
        var world = new World();

        // Subscription order: world Shared, world WorldMarkerF, entity Shared, entity EntityMarkerB.
        world.SubscribeWorldComponentRemoved((World _, in SharedMarker v) => log.Add($"World(Shared {v.Value})"));
        world.SubscribeWorldComponentRemoved((World _, in WorldMarkerF v) => log.Add($"World(F {v.Value})"));
        world.SubscribeEntityComponentRemoved((in Entity _, in SharedMarker v) => log.Add($"Entity(Shared {v.Value})"));
        world.SubscribeEntityComponentRemoved((in Entity _, in EntityMarkerB v) => log.Add($"Entity(B {v.Value})"));

        var carrier = world.CreateEntity();
        carrier.Set(new SharedMarker { Value = 1 });
        carrier.Set(new EntityMarkerB { Value = 2 });
        world.Set(new SharedMarker { Value = 3 });
        world.Set(new WorldMarkerF { Value = 4 });

        world.Dispose();
        Report("world A: subscribe world Shared, world F, entity Shared, entity B", Render(log));

        // The discriminating leg: the SHARED type takes its slot on the WORLD leg first, and only
        // then is subscribed on the entity leg — after another entity type. A single shared registry
        // reports Shared2 FIRST on the entity leg (it holds the older id); two independent registries
        // report it SECOND (its own leg's subscription order).
        var split = new List<string>();
        var second = new World();
        second.SubscribeWorldComponentRemoved((World _, in SharedMarker2 v) => split.Add($"World(Shared2 {v.Value})"));
        second.SubscribeEntityComponentRemoved((in Entity _, in EntityMarkerC v) => split.Add($"Entity(C {v.Value})"));
        second.SubscribeEntityComponentRemoved((in Entity _, in SharedMarker2 v) => split.Add($"Entity(Shared2 {v.Value})"));

        var carrier2 = second.CreateEntity();
        carrier2.Set(new EntityMarkerC { Value = 1 });
        carrier2.Set(new SharedMarker2 { Value = 2 });
        second.Set(new SharedMarker2 { Value = 3 });

        second.Dispose();
        Report("world B: subscribe world Shared2, entity C, entity Shared2", Render(split));

        var entityLeg = new List<string>();
        foreach (var line in split)
        {
            if (line.StartsWith("Entity(", StringComparison.Ordinal)) entityLeg.Add(line);
        }

        Report("=> the two legs are ordered by",
            entityLeg.Count > 0 && entityLeg[0].StartsWith("Entity(C", StringComparison.Ordinal)
                ? "each leg follows its OWN subscription order (NOT one shared type-id registry)"
                : "ONE SHARED registry — the world-leg subscription claimed the entity leg's slot too");
        Report("=> and the LEGS themselves are ordered by",
            split.Count > 0 && split[0].StartsWith("World(", StringComparison.Ordinal)
                ? "subscription order ACROSS legs — the world leg is NOT last by rule, it is last when it subscribed last"
                : "entity leg first, world leg last, by rule");

        // If the legs really are ordered by subscription across channel KINDS, then EntityDisposed —
        // which M4 saw fire for every entity before any ComponentRemoved — is a channel like any
        // other, and subscribing it LAST must move it. That is the decisive leg: it separates "the
        // cascade has three phases" from "the cascade is one list of channels in subscription order".
        var phases = new List<string>();
        var third = new World();
        third.SubscribeEntityComponentRemoved((in Entity _, in EntityMarkerD v) => phases.Add($"Removed(D {v.Value})"));
        third.SubscribeWorldComponentRemoved((World _, in WorldMarkerG v) => phases.Add($"World(G {v.Value})"));
        third.SubscribeEntityDisposed((in Entity _) => phases.Add("EntityDisposed"));

        for (var i = 1; i <= 2; i++)
        {
            var carrier3 = third.CreateEntity();
            carrier3.Set(new EntityMarkerD { Value = i });
        }

        third.Set(new WorldMarkerG { Value = 9 });
        third.Dispose();

        Report("world C: subscribe entity D, world G, EntityDisposed (in that order)", Render(phases));
        Report("=> EntityDisposed is",
            phases.Count > 0 && phases[0] == "EntityDisposed"
                ? "a PHASE that always runs first, whenever it was subscribed"
                : "a CHANNEL like the others — it runs in its own subscription slot");

        // The same question one level down, on the per-ENTITY path: item 40 pins
        // "EntityDisposed, then ComponentRemoved per component" — is that a phase order too, or is
        // it the same channel list? `entity.Dispose` is the verb the LDtk sweep and every
        // screen-teardown call reach first, so the facade's DisposeEntity has to match whichever.
        var perEntity = new List<string>();
        var fourth = new World();
        fourth.SubscribeEntityComponentRemoved((in Entity _, in EntityMarkerD v) => perEntity.Add($"Removed(D {v.Value})"));
        fourth.SubscribeEntityDisposed((in Entity _) => perEntity.Add("EntityDisposed"));

        var single = fourth.CreateEntity();
        single.Set(new EntityMarkerD { Value = 7 });
        single.Dispose();
        fourth.Dispose();

        Report("world D: entity.Dispose with Removed subscribed BEFORE EntityDisposed", Render(perEntity));
        Report("=> item 40's order is",
            perEntity.Count > 0 && perEntity[0] == "EntityDisposed"
                ? "a fixed phase order (EntityDisposed always first)"
                : "the same subscription-order channel list as world.Dispose");
    }

    private static List<string> WorldComponentOrderOfOneWorld(bool bFirst)
    {
        var log = new List<string>();
        var world = new World();

        void SubscribeB() =>
            world.SubscribeWorldComponentRemoved((World _, in WorldMarkerB v) => log.Add($"WorldMarkerB({v.Value})"));
        void SubscribeC() =>
            world.SubscribeWorldComponentRemoved((World _, in WorldMarkerC v) => log.Add($"WorldMarkerC({v.Value})"));

        if (bFirst)
        {
            SubscribeB();
            SubscribeC();
            world.Set(new WorldMarkerB { Value = 1 });
            world.Set(new WorldMarkerC { Value = 2 });
        }
        else
        {
            SubscribeC();
            SubscribeB();
            world.Set(new WorldMarkerC { Value = 2 });
            world.Set(new WorldMarkerB { Value = 1 });
        }

        world.Dispose();
        return log;
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

            // The sweep is DEPTH-CAPPED, and the cap is what this leg actually measures: inside the
            // handler the entity still reads IsAlive == true, so the sweep disposes it again, which
            // republishes EntityDisposingMessage, which re-enters the handler — with no re-entrancy
            // guard anywhere in DefaultEcs 0.18.0-beta01. What is RECORDED is bounded re-entry
            // (`sweeps stopped by the depth cap` > 0, with the state that would end the recursion
            // unchanged at every level); that an uncapped run cannot terminate follows from it, but
            // is an inference — a real stack overflow would take the harness with it, so it is
            // deliberately never run.
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
    // M5b (item 41) — entities CREATED by a handler during world.Dispose.
    //
    // Item 41 is Dispose + Create + Publish, and the LDtk parser does all three from the same
    // singleton dispatch. M5 covers the Dispose half at teardown; this covers the Create half:
    // a world-component Removed handler that publishes spawn requests while the cascade is
    // already walking. The question the facade has to answer is whether those newborn entities
    // are reported at all, or whether teardown-time creation is event-silent.
    // ---------------------------------------------------------------------------------------
    private static void MeasureCreationDuringWorldDispose()
    {
        Section("M5b (item 41) — entities CREATED by a handler DURING world.Dispose");

        var log = new List<string>();
        var world = new World();
        var born = new List<Entity>();

        world.SubscribeEntityDisposed((in Entity e) => log.Add($"EntityDisposed({Identify(e)})"));
        world.SubscribeEntityComponentRemoved((in Entity _, in EntityMarker v) => log.Add($"Removed(Marker {v.Value})"));
        world.SubscribeEntityComponentAdded((in Entity _, in EntityMarker v) => log.Add($"Added(Marker {v.Value})"));
        world.SubscribeWorldComponentRemoved((World w, in WorldMarker _) =>
        {
            log.Add("WorldComponentRemoved");
            try
            {
                for (var i = 71; i <= 73; i++)
                {
                    var newborn = w.CreateEntity();
                    newborn.Set(new EntityMarker { Value = i });
                    born.Add(newborn);
                }
            }
            catch (Exception ex)
            {
                log.Add($"(creating during teardown THREW {ex.GetType().Name})");
            }
        });

        for (var i = 1; i <= 4; i++)
        {
            var e = world.CreateEntity();
            e.Set(new EntityMarker { Value = i });
        }

        world.Set(new WorldMarker { Value = 99 });
        log.Clear();

        try
        {
            world.Dispose();
            Report("events", Render(log));
            Report("pre-existing carriers", 4);
            Report("entities created by the handler", born.Count);
            Report("EntityDisposed total", Count(log, "EntityDisposed"));
            Report("Removed(Marker) total", Count(log, "Removed(Marker"));
            var newbornAlive = 0;
            foreach (var e in born)
            {
                try
                {
                    if (e.IsAlive) newbornAlive++;
                }
                catch (Exception)
                {
                    // A handle into a torn-down world may not even answer IsAlive.
                }
            }

            Report("newborns still IsAlive after Dispose returned", newbornAlive);
            Report("=> teardown-time creation is EVENT-SILENT",
                Count(log, "EntityDisposed") <= 4 && Count(log, "Removed(Marker") <= 4 ? "YES" : "NO");
        }
        catch (Exception ex)
        {
            Report("world.Dispose THREW", ex.GetType().FullName + ": " + ex.Message);
            Report("events before the throw", Render(log));
        }
    }

    // ---------------------------------------------------------------------------------------
    // M5c (item 50) — a component ADDED to a still-live carrier during the teardown cascade.
    //
    // The mirror of M5b: not a new entity, a new component on an entity the cascade has already
    // walked past. It decides whether teardown reports each entity's component set as of cascade
    // ENTRY or as of dispatch time — which is exactly the choice a facade that snapshots the
    // carriers up front has already made, silently.
    // ---------------------------------------------------------------------------------------
    private static void MeasureComponentAddedDuringWorldDispose()
    {
        Section("M5c (item 50) — a component SET on a live carrier DURING the teardown cascade");

        var log = new List<string>();
        var world = new World();
        var carriers = new List<Entity>();

        world.SubscribeEntityComponentRemoved((in Entity _, in EntityMarker v) =>
        {
            log.Add($"Removed(Marker {v.Value})");

            // Give a carrier the cascade has NOT reported yet a component it did not have when
            // teardown began. Was it subscribed and present at destroy time? Yes to both.
            foreach (var carrier in carriers)
            {
                if (carrier.IsAlive && !carrier.Has<ManagedPayload>())
                {
                    carrier.Set(new ManagedPayload { Name = "born-mid-cascade" });
                    break;
                }
            }
        });
        world.SubscribeEntityComponentRemoved((in Entity _, in ManagedPayload v) => log.Add($"Removed(Managed {v.Name})"));

        for (var i = 1; i <= 3; i++)
        {
            var e = world.CreateEntity();
            e.Set(new EntityMarker { Value = i });
            carriers.Add(e);
        }

        try
        {
            world.Dispose();
            Report("events", Render(log));
            Report("Removed(Managed) for the mid-cascade component", Count(log, "Removed(Managed"));
            Report("=> teardown reports the component set as of",
                Count(log, "Removed(Managed") > 0 ? "DISPATCH TIME (the late component is reported)" : "CASCADE ENTRY (the late component is silent)");
        }
        catch (Exception ex)
        {
            Report("world.Dispose THREW", ex.GetType().FullName + ": " + ex.Message);
            Report("events before the throw", Render(log));
        }
    }

    // ---------------------------------------------------------------------------------------
    // M7 (item 50) — a handler that calls world.Dispose DURING world.Dispose.
    //
    // The re-entrancy shape one level up from M5: not "dispose the entities", but "dispose the
    // world". Six engine sites call world.Dispose (screen teardown, Game.UnloadContent, the
    // editor's world swap), and a handler that reaches a second one re-enters the cascade. The
    // sweep is depth-capped for the same reason M5's is: an unbounded recursion is not runnable.
    // ---------------------------------------------------------------------------------------
    private static void MeasureWorldDisposeInsideHandler()
    {
        Section("M7 (item 50) — a handler that calls world.Dispose DURING world.Dispose");

        var log = new List<string>();
        var world = new World();

        const int MaxDepth = 4;
        var depth = 0;
        var capped = 0;

        world.SubscribeEntityDisposed((in Entity e) =>
        {
            log.Add($"EntityDisposed({Identify(e)})");
            if (depth >= MaxDepth)
            {
                capped++;
                return;
            }

            depth++;
            try
            {
                world.Dispose();
            }
            catch (Exception ex)
            {
                log.Add($"(nested world.Dispose THREW {ex.GetType().Name})");
            }

            depth--;
        });
        world.SubscribeEntityComponentRemoved((in Entity _, in EntityMarker v) => log.Add($"Removed(Marker {v.Value})"));

        for (var i = 1; i <= 2; i++)
        {
            var e = world.CreateEntity();
            e.Set(new EntityMarker { Value = i });
        }

        try
        {
            world.Dispose();
            Report("events", Render(log));
            Report("carriers", 2);
            Report("EntityDisposed total", Count(log, "EntityDisposed"));
            Report("nested Dispose calls stopped by the depth cap", capped);
            Report("=> world.Dispose IS RE-ENTRANCY-GUARDED",
                capped == 0 && Count(log, "EntityDisposed") == 2 ? "YES" : "NO (the cap is what stopped it)");
        }
        catch (Exception ex)
        {
            Report("world.Dispose THREW", ex.GetType().FullName + ": " + ex.Message);
            Report("events before the throw", Render(log));
            Report("nested Dispose calls stopped by the depth cap", capped);
        }
    }

    // ---------------------------------------------------------------------------------------
    // M8 — the CROSSED re-entrancy: world.Dispose from inside a normal entity.Dispose.
    //
    // M7 covers world-inside-world. This is the direction the engine can reach without anyone
    // planning it: an EntityDisposed handler decides the screen is over and disposes the world,
    // while the entity disposal that called it is still mid-flight. What happens to the outer
    // disposal after the world underneath it is gone is a facade obligation (S9 leg B), so the
    // incumbent's answer is measured rather than guessed — including what the dead world does with
    // a CreateEntity and a world.Set afterwards.
    // ---------------------------------------------------------------------------------------
    private static void MeasureWorldDisposeInsideEntityDispose()
    {
        Section("M8 (item 50 / item 67) — world.Dispose from inside a normal entity.Dispose");

        var log = new List<string>();
        var world = new World();
        var nested = 0;

        world.SubscribeEntityDisposed((in Entity e) =>
        {
            log.Add($"EntityDisposed({Identify(e)})");
            nested++;
            try
            {
                world.Dispose();
            }
            catch (Exception ex)
            {
                log.Add($"(nested world.Dispose THREW {ex.GetType().Name})");
            }
        });
        world.SubscribeEntityComponentRemoved((in Entity _, in EntityMarker v) => log.Add($"Removed(Marker {v.Value})"));

        var carriers = new List<Entity>();
        for (var i = 1; i <= 2; i++)
        {
            var e = world.CreateEntity();
            e.Set(new EntityMarker { Value = i });
            carriers.Add(e);
        }

        try
        {
            carriers[0].Dispose();
            Report("outer entity.Dispose", "returned normally");
        }
        catch (Exception ex)
        {
            Report("outer entity.Dispose THREW", ex.GetType().FullName + ": " + ex.Message);
        }

        Report("events", Render(log));
        Report("nested world.Dispose calls made by the handler", nested);

        // Reading a handle after this teardown is itself a measurement: DefaultEcs 0.18.0-beta01
        // throws out of `Entity.IsAlive` here, the same shape M6 found for a post-teardown set.
        try
        {
            Report("carriers still alive afterwards", (carriers[0].IsAlive ? 1 : 0) + (carriers[1].IsAlive ? 1 : 0));
        }
        catch (Exception ex)
        {
            Report("reading Entity.IsAlive after the crossed teardown THREW", ex.GetType().FullName + ": " + ex.Message);
        }

        // The post-teardown surface the resumed disposal would have touched.
        try
        {
            var afterwards = world.CreateEntity();
            Report("world.CreateEntity() after the crossed teardown", $"returned a handle, IsAlive={afterwards.IsAlive}");
        }
        catch (Exception ex)
        {
            Report("world.CreateEntity() after the crossed teardown THREW", ex.GetType().Name + ": " + ex.Message);
        }

        try
        {
            world.Set(new WorldMarker { Value = 123 });
            Report("world.Set<T> after the crossed teardown", $"stored, Has<T>()={world.Has<WorldMarker>()}");
        }
        catch (Exception ex)
        {
            Report("world.Set<T> after the crossed teardown THREW", ex.GetType().Name + ": " + ex.Message);
        }
    }

    // ---------------------------------------------------------------------------------------
    // M9 — a teardown handler that THROWS: how much of the cascade survives it?
    //
    // Three carriers, a handler that throws on the second, and every other reactive verb wired. The
    // facade's answer (S11) is fail-fast with the teardown completed anyway; whether that is parity
    // or a facade choice is only knowable by asking the incumbent the same question.
    // ---------------------------------------------------------------------------------------
    private static void MeasureThrowingTeardownHandler()
    {
        Section("M9 (item 50) — a handler that THROWS during world.Dispose");

        var log = new List<string>();
        var world = new World();
        var carriers = new List<Entity>();

        world.SubscribeEntityDisposed((in Entity e) =>
        {
            log.Add($"EntityDisposed({Identify(e)})");
            if (e.IsAlive && e.Has<EntityMarker>() && e.Get<EntityMarker>().Value == 2)
            {
                throw new InvalidOperationException("teardown handler failed on carrier #2");
            }
        });
        world.SubscribeEntityComponentRemoved((in Entity _, in EntityMarker v) => log.Add($"Removed(Marker {v.Value})"));
        world.SubscribeWorldComponentRemoved((World _, in WorldMarker v) => log.Add($"WorldComponentRemoved({v.Value})"));

        for (var i = 1; i <= 3; i++)
        {
            var e = world.CreateEntity();
            e.Set(new EntityMarker { Value = i });
            carriers.Add(e);
        }

        world.Set(new WorldMarker { Value = 99 });
        var set = world.GetEntities().With<EntityMarker>().AsSet();

        try
        {
            world.Dispose();
            Report("world.Dispose", "returned normally — the throw did NOT escape");
        }
        catch (Exception ex)
        {
            Report("world.Dispose THREW (the handler's exception escapes)", ex.GetType().Name + ": " + ex.Message);
        }

        Report("events before/around the throw", Render(log));
        Report("EntityDisposed total (3 carriers)", Count(log, "EntityDisposed"));
        Report("ComponentRemoved total", Count(log, "Removed(Marker"));
        Report("WorldComponentRemoved total", Count(log, "WorldComponentRemoved"));

        var alive = 0;
        foreach (var carrier in carriers)
        {
            if (carrier.IsAlive) alive++;
        }

        Report("carriers still alive after the failed teardown", alive);

        var before = log.Count;
        try
        {
            world.Dispose();
            Report("a SECOND world.Dispose replays", log.Count - before + " more events");
        }
        catch (Exception ex)
        {
            Report("a SECOND world.Dispose THREW", ex.GetType().Name + ": " + ex.Message);
        }

        try
        {
            Report("set.Count after the failed teardown", set.Count);
        }
        catch (Exception ex)
        {
            Report("reading the set after the failed teardown THREW", ex.GetType().Name);
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

    /// <summary>Second world component — M4c's discriminator. Touched by nothing else.</summary>
    private struct WorldMarkerB
    {
        public int Value;
    }

    /// <summary>Third world component — M4c's discriminator. Touched by nothing else.</summary>
    private struct WorldMarkerC
    {
        public int Value;
    }

    /// <summary>M4c's first-contact leg only.</summary>
    private struct WorldMarkerD
    {
        public int Value;
    }

    /// <summary>M4c's first-contact leg only.</summary>
    private struct WorldMarkerE
    {
        public int Value;
    }

    /// <summary>M4d only — subscribed on the world leg AND the entity leg of the same world.</summary>
    private struct SharedMarker
    {
        public int Value;
    }

    /// <summary>M4d's discriminating leg — world-leg subscription first, entity-leg subscription last.</summary>
    private struct SharedMarker2
    {
        public int Value;
    }

    /// <summary>M4d only.</summary>
    private struct WorldMarkerF
    {
        public int Value;
    }

    private struct EntityMarker
    {
        public int Value;
    }

    /// <summary>M4d only.</summary>
    private struct EntityMarkerB
    {
        public int Value;
    }

    /// <summary>M4d only.</summary>
    private struct EntityMarkerC
    {
        public int Value;
    }

    /// <summary>M4d's phase-vs-channel leg only.</summary>
    private struct EntityMarkerD
    {
        public int Value;
    }

    /// <summary>M4d's phase-vs-channel leg only.</summary>
    private struct WorldMarkerG
    {
        public int Value;
    }

    /// <summary>M4b only — so world A below is genuinely this type's first touch in the process.</summary>
    private struct PoolAlpha
    {
        public int Value;
    }

    /// <summary>M4b only — see <see cref="PoolAlpha"/>.</summary>
    private struct PoolBeta
    {
        public int Value;
    }

    /// <summary>M4b's split leg only — subscribe order and Set order disagree for this pair.</summary>
    private struct PoolGamma
    {
        public int Value;
    }

    /// <summary>M4b's split leg only — see <see cref="PoolGamma"/>.</summary>
    private struct PoolDelta
    {
        public int Value;
    }

    /// <summary>M4b's first-contact leg only.</summary>
    private struct PoolEpsilon
    {
        public int Value;
    }

    /// <summary>M4b's first-contact leg only.</summary>
    private struct PoolZeta
    {
        public int Value;
    }

    private sealed class ManagedPayload
    {
        public string Name;
    }
}
