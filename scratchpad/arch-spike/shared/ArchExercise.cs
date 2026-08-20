using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Arch.Core;
using Arch.Core.Extensions;

namespace MonoDreams.ArchSpike;

/// <summary>
/// The wave-0 Arch target proof (issue #119, contract item 2), compiled into BOTH proof heads —
/// the NativeAOT desktop console and the KNI/BlazorGL WASM head — via <c>&lt;Compile Include&gt;</c>.
/// One body of checks means the two legs make the same claim about the same operations, and a
/// divergence between the targets shows up as a failing check rather than as a difference between
/// two hand-written programs.
///
/// It exercises the three operation families the engine depends on:
///
///   1. creation — struct-only archetypes AND an archetype carrying a MANAGED (class) component.
///      The managed one is the interesting case on both targets: Arch allocates component storage
///      through <c>Array.CreateInstance</c>, the exact path a trimmed/AOT image can have discarded;
///   2. queries — the per-entity <c>ForEach</c> form (what the facade's <c>EntitySystem</c> will run
///      in wave 2) and the chunk/span form (the wave-3 conversion target);
///   3. structural change — Add / Remove / Destroy, i.e. archetype moves, plus the dead-handle
///      liveness check the facade's Entity has to reproduce (contract item 17).
///
/// Every step self-verifies. <see cref="Run"/> returns the failure count next to the report, so each
/// head can turn it into an exit code or a page state: "it compiled" and "it works" stay separate
/// claims.
/// </summary>
internal static class ArchExercise
{
    private const int EntityCount = 5_000;

    public static (int Failures, string Report) Run()
    {
        var report = new StringBuilder();
        var failures = 0;
        var checks = 0;

        void Report(string label, object value) => report.AppendLine($"   {label,-52} : {value}");

        void Section(string title)
        {
            report.AppendLine();
            report.AppendLine("-- " + title);
        }

        // The run reports "N checks, M failed" (same shape as the sibling facade proof) so the
        // recorded headline can be RECOUNTED from the artifact instead of taken on trust.
        void Check<T>(string label, T actual, T expected)
        {
            checks++;
            var ok = EqualityComparer<T>.Default.Equals(actual, expected);
            if (!ok) failures++;
            report.AppendLine($"   [{(ok ? "ok  " : "FAIL")}] {label,-52} : {actual} (expected {expected})");
        }

        report.AppendLine("== Arch 2.1.0 target proof (issue #119 wave 0, contract item 2) ==");
        report.AppendLine();
        Report("RuntimeInformation.OSArchitecture", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
        Report("RuntimeFeature.IsDynamicCodeSupported", RuntimeFeature.IsDynamicCodeSupported);
        Report("RuntimeFeature.IsDynamicCodeCompiled", RuntimeFeature.IsDynamicCodeCompiled);
#if ARCH_AOT_GENERATOR
        Report("Arch.AOT.SourceGenerator", "ON  (components annotated [Component], via namespace shim)");
#else
        Report("Arch.AOT.SourceGenerator", "OFF (negative control — no ComponentRegistry priming)");
#endif
        // The generator's ModuleInitializer has already run by now, so this reads whether the
        // priming took EFFECT rather than whether it was configured.
        Report("ComponentRegistry.Size before any World", ComponentRegistry.Size);
        Report("ComponentRegistry.Has<Payload>() (managed)", ComponentRegistry.Has<Payload>());

        // `World.Create()` is INSIDE the reporting try. A target that cannot build a world at all is
        // exactly the failure this exercise exists to catch (the AOT negative control dies one line
        // later, in the first `world.Create`), and a throw out here would take every check line
        // already printed with it — on the WASM head, where the report IS the page, that means a
        // blank proof rather than a failing one.
        World world = null;
        try
        {
            world = World.Create();

            // ----------------------------------------------------------------------- creation
            Section("creation");

            for (var i = 0; i < EntityCount; i++)
            {
                world.Create(new Position { X = i, Y = -i }, new Velocity { X = 1f, Y = 2f });
            }

            // A second archetype, so the query below has to select rather than sweep everything.
            for (var i = 0; i < 100; i++)
            {
                world.Create(new Position { X = i, Y = i }, new Tag());
            }

            Check("world.CountEntities(Position+Velocity)", world.CountEntities(new QueryDescription().WithAll<Position, Velocity>()), EntityCount);
            Check("world.CountEntities(Position+Tag)", world.CountEntities(new QueryDescription().WithAll<Position, Tag>()), 100);
            Check("world.CountEntities(Position)", world.CountEntities(new QueryDescription().WithAll<Position>()), EntityCount + 100);

            // ------------------------------------------------------------------------ queries
            Section("queries");

            var description = new QueryDescription().WithAll<Position, Velocity>();

            var perEntityVisits = 0;
            world.Query(in description, (ref Position position, ref Velocity velocity) =>
            {
                position.X += velocity.X;
                position.Y += velocity.Y;
                perEntityVisits++;
            });
            Check("per-entity ForEach visits", perEntityVisits, EntityCount);

            var chunkVisits = 0;
            var checksum = 0d;
            foreach (ref var chunk in world.Query(in description))
            {
                var positions = chunk.GetSpan<Position>();
                var velocities = chunk.GetSpan<Velocity>();
                foreach (var index in chunk)
                {
                    ref var position = ref positions[index];
                    ref var velocity = ref velocities[index];
                    position.X += velocity.X;
                    position.Y += velocity.Y;
                    checksum += position.X;
                    chunkVisits++;
                }
            }

            Check("chunk/span visits", chunkVisits, EntityCount);

            // Both passes ran, so every X advanced by exactly 2 * Velocity.X from its seeded value:
            // sum(i) + 2 * count. Spelled out so a silently-skipped write cannot pass as a visit count.
            var expected = (double)EntityCount * (EntityCount - 1) / 2 + 2d * EntityCount;
            Check("chunk pass wrote through (checksum)", checksum, expected);

            // -------------------------------------------------------------- structural change
            Section("structural change");

            var entity = world.Create(new Position { X = 1, Y = 1 });
            Check("Has<Velocity> before Add", world.Has<Velocity>(entity), false);

            world.Add(entity, new Velocity { X = 9f, Y = 9f });
            Check("Has<Velocity> after Add", world.Has<Velocity>(entity), true);
            Check("Position survived the archetype move", world.Get<Position>(entity).X, 1f);
            Check("Velocity readable after Add", world.Get<Velocity>(entity).X, 9f);

            // ref Get is the facade's Get<T>() shape — an in-place write with no publication.
            ref var refPosition = ref world.Get<Position>(entity);
            refPosition.X = 42f;
            Check("ref Get writes through", world.Get<Position>(entity).X, 42f);

            world.Remove<Velocity>(entity);
            Check("Has<Velocity> after Remove", world.Has<Velocity>(entity), false);
            Check("Position survived the second move", world.Get<Position>(entity).X, 42f);

            // Mass churn: the archetype table has to grow and shrink the same way on both targets.
            var churn = new List<Entity>(1_000);
            for (var i = 0; i < 1_000; i++)
            {
                churn.Add(world.Create(new Position { X = i, Y = i }));
            }

            foreach (var e in churn)
            {
                world.Add(e, new Tag());
            }

            Check("tagged after mass Add", world.CountEntities(new QueryDescription().WithAll<Position, Tag>()), 1_100);

            foreach (var e in churn)
            {
                world.Destroy(e);
            }

            Check("tagged after mass Destroy", world.CountEntities(new QueryDescription().WithAll<Position, Tag>()), 100);
            Check("destroyed handle IsAlive", world.IsAlive(churn[0]), false);

            world.Destroy(entity);
            Check("destroyed single handle IsAlive", world.IsAlive(entity), false);

            // ---------------------------------------------------- managed (class) components
            Section("managed (class) components — the trim/AOT-sensitive path");

            var carriers = new List<Entity>(256);
            for (var i = 0; i < 256; i++)
            {
                carriers.Add(world.Create(new Position { X = i, Y = i }, new Payload { Name = "draw-" + i, Depth = i * 0.001f }));
            }

            var managedDescription = new QueryDescription().WithAll<Position, Payload>();
            Check("managed archetype count", world.CountEntities(in managedDescription), 256);

            var seen = 0;
            var identityHolds = true;
            foreach (ref var chunk in world.Query(in managedDescription))
            {
                var payloads = chunk.GetSpan<Payload>();
                var positions = chunk.GetSpan<Position>();
                foreach (var index in chunk)
                {
                    var payload = payloads[index];
                    payload.Depth = positions[index].Y * 0.5f;
                    // A class component must hand back the SAME instance (foundation :707 relies on it).
                    identityHolds &= ReferenceEquals(payload, chunk.GetSpan<Payload>()[index]);
                    seen++;
                }
            }

            Check("managed chunk visits", seen, 256);
            Check("class component identity stable", identityHolds, true);
            Check("in-place managed write visible", world.Get<Payload>(carriers[10]).Depth, 5f);

            // Structural move of an archetype carrying a managed component: the reference-type array
            // is re-allocated in the destination archetype, which is where an image without a
            // registered component type falls over.
            world.Add(carriers[0], new Velocity { X = 3f, Y = 3f });
            Check("managed component survived archetype move", world.Get<Payload>(carriers[0]).Name, "draw-0");

            // ----------------------- process-wide statics across World.Destroy (H9 / item C12)
            //
            // The deep-plan's H9 dimension-violation row makes this a wave-0 obligation in so many
            // words: "C12 registers statics but Arch's World.Worlds/component-type registries may
            // lack a reset API; wave-0 spike must prove World.Destroy (or equivalent) clears them".
            // C12 itself promises ProcessWideState.Reset returns "Arch World.Worlds/component
            // statics to baseline". Those are TWO registries with two different answers, and the
            // negative control above already showed that guessing wrong about the component one is
            // fatal — so both are measured here rather than assumed.
            Section("process-wide statics across World.Destroy (H9, contract item C12)");

            var worldSizeBaseline = World.WorldSize;
            var registryBaseline = ComponentRegistry.Size;
            Report("World.WorldSize (baseline: this exercise's world)", worldSizeBaseline);
            Report("World.Worlds.Length (backing array)", World.Worlds.Length);
            Report("ComponentRegistry.Size (baseline)", registryBaseline);

            var extra = World.Create();
            var extraId = extra.Id;
            extra.Create(new Position { X = 1f, Y = 1f }, new Payload { Name = "aux", Depth = 1f });
            Check("World.WorldSize after one extra World.Create", World.WorldSize, worldSizeBaseline + 1);

            World.Destroy(extra);
            Check("World.WorldSize back to baseline after Destroy", World.WorldSize, worldSizeBaseline);
            Check("World.Worlds slot nulled by Destroy", World.Worlds[extraId] == null, true);

            // "the next world gets `extraId` back" would be an assert on a CONCRETE world id, and the
            // very next probe (the recycled-id queue) is what proves such an assert is only true while
            // that queue is empty — an unstated precondition on a static this section also proves is
            // unresettable. A7 concludes C12 must forbid asserting on a world id; a check here that
            // does exactly that would fail for a reason that is not about Arch the moment anything ran
            // a `World.Destroy` first. So the PROPERTY is asserted instead: the id came out of the
            // freed set, whichever member of it that is.
            var freedBeforeReborn = RecycledWorldIds();
            var reborn = World.Create();
            var reuseOutcome = freedBeforeReborn == null
                ? "unreadable: RecycledWorldIds not reflectable on this target"
                : freedBeforeReborn.Contains(reborn.Id)
                    ? "REUSED a previously-freed id"
                    : "took a FRESH id";
            Report("freed ids queued before this World.Create",
                freedBeforeReborn == null ? "<unreadable>" : "[" + string.Join(", ", freedBeforeReborn) + "]");
            Report("World.Create's id (" + reborn.Id + ") vs that queue", reuseOutcome);
            Check("World.Create never mints a FRESH id while a freed one is queued",
                reuseOutcome.StartsWith("took a FRESH", StringComparison.Ordinal), false);
            var rebornEntity = reborn.Create(new Position { X = 2f, Y = 2f }, new Payload { Name = "reborn", Depth = 2f });
            Check("a world created AFTER Destroy still builds a managed archetype",
                reborn.Get<Payload>(rebornEntity).Name, "reborn");
            World.Destroy(reborn);
            Check("World.WorldSize back to baseline again", World.WorldSize, worldSizeBaseline);

            // Every leg here tears a world down through the STATIC `World.Destroy`, but that is not the
            // verb the contract speaks: item 50's rewrite makes `world.Dispose()` drive the facade's
            // teardown cascade, and A7's reset hook is the same object's disposal seen from outside.
            // Arch's `World` is `IDisposable`, so the two verbs are two entry points and their
            // equivalence is a measurement, not a reading of the docs — a reset hook built on the wrong
            // one leaks. Same three registry effects, asserted against the same baselines.
            var disposable = World.Create();
            var disposableId = disposable.Id;
            disposable.Create(new Position { X = 3f, Y = 3f }, new Payload { Name = "disposed", Depth = 3f });
            var registryBeforeDispose = ComponentRegistry.Size;
            disposable.Dispose();
            Check("world.Dispose() returns World.WorldSize to baseline, exactly like World.Destroy",
                World.WorldSize, worldSizeBaseline);
            Check("world.Dispose() nulls the World.Worlds slot, exactly like World.Destroy",
                World.Worlds[disposableId] == null, true);
            Check("world.Dispose() leaves ComponentRegistry.Size untouched too",
                ComponentRegistry.Size, registryBeforeDispose);
            Check("...and Payload's registration survives world.Dispose()", ComponentRegistry.Has<Payload>(), true);

            // Idempotence matters for a reset hook that may run over a world a screen already disposed:
            // a second call must not double-free the id back onto the queue the probe below reads.
            var queuedBeforeDoubleDispose = RecycledWorldIdCount();
            disposable.Dispose();
            Check("a second world.Dispose() is a silent no-op", World.WorldSize, worldSizeBaseline);
            Report("recycled-id queue Count across the double Dispose",
                queuedBeforeDoubleDispose + " -> " + RecycledWorldIdCount() + " (equal == no double-free)");

            // ...but "back to baseline" is a claim about `World.Worlds`/`World.WorldSize` ONLY, and
            // A7's "World.Destroy per live world, nothing more" is only checkable if the set of
            // process-wide statics is ENUMERATED rather than guessed. Arch holds another one:
            // `World.Destroy` does not free an id, it ENQUEUES it, and `World.Create` dequeues. So
            // the id the next world gets is decided by the order earlier worlds were destroyed in —
            // state that outlives every world, that no API drains, and that a test-order shuffle
            // therefore permutes. Two worlds freed in a chosen order, then two created, says which:
            //   freed-order (FIFO) => secondId, firstId | reverse-order (LIFO) => firstId, secondId
            //   lowest-free-id     => firstId, secondId (indistinguishable from LIFO on one id, so
            //                                            the PAIR is what is asserted)
            var firstFree = World.Create();
            var secondFree = World.Create();
            var firstFreeId = firstFree.Id;
            var secondFreeId = secondFree.Id;
            World.Destroy(secondFree);   // freed FIRST, and it holds the HIGHER id
            World.Destroy(firstFree);
            var nextWorld = World.Create();
            var worldAfterNext = World.Create();
            Check("world ids come back in the order they were FREED, not lowest-free-first",
                $"{nextWorld.Id},{worldAfterNext.Id}", $"{secondFreeId},{firstFreeId}");
            World.Destroy(worldAfterNext);
            World.Destroy(nextWorld);
            Report("=> the recycled-id queue is process-lifetime", "World.Destroy enqueues, World.Create dequeues; nothing drains it");

            // A7's reset hook has to WALK `World.Worlds`, and the walk shape is not free to choose.
            // Every Destroy above freed the HIGHEST live id, so the only hole those could leave is at
            // the end of the array — where `World.WorldSize` is still a valid bound and the naive
            // `for (i < World.WorldSize) World.Destroy(World.Worlds[i])` hook happens to work. The
            // engine's shape is the other one: worlds are created and destroyed out of order across
            // screens, so a hole opens BELOW a live world. `World.WorldSize` is a COUNT of live
            // worlds, not a high-water mark, so on that shape the naive hook reads a null slot and
            // never reaches the world sitting above it.
            var pairA = World.Create();
            var pairB = World.Create();
            var lowerWorld = pairA.Id < pairB.Id ? pairA : pairB;
            var upperWorld = pairA.Id < pairB.Id ? pairB : pairA;
            var lowerWorldId = lowerWorld.Id;
            var upperWorldId = upperWorld.Id;
            World.Destroy(lowerWorld);   // the NON-highest live id: the hole lands in the MIDDLE

            Check("destroying a NON-highest id nulls the low slot", World.Worlds[lowerWorldId] == null, true);
            Check("...while the higher-id world stays live", ReferenceEquals(World.Worlds[upperWorldId], upperWorld), true);

            var nullsUnderSize = 0;
            for (var i = 0; i < World.WorldSize; i++)
            {
                if (World.Worlds[i] == null) nullsUnderSize++;
            }

            var liveBeyondSize = 0;
            for (var i = World.WorldSize; i < World.Worlds.Length; i++)
            {
                if (World.Worlds[i] != null) liveBeyondSize++;
            }

            Check("a `for (i < World.WorldSize)` sweep would read a NULL slot", nullsUnderSize, 1);
            Check("...and leak this many LIVE worlds past its bound", liveBeyondSize, 1);

            // The shape that does hold, and the one A7 has to name instead.
            var liveByLength = 0;
            for (var i = 0; i < World.Worlds.Length; i++)
            {
                if (World.Worlds[i] != null) liveByLength++;
            }

            Check("walking World.Worlds by LENGTH, skipping nulls, sees every live world", liveByLength, World.WorldSize);

            // ...and that walk is RUN, not counted. A7 hands wave 1 a hook shape, so the hook has to be
            // executed at least once here: `World.Destroy` mutates the very array being iterated (it
            // nulls a slot and decrements `worldSizeUnsafe`), so "a by-Length walk would see every live
            // world" does not by itself say that destroying INSIDE that walk is safe, nor that the
            // sweep drives `World.WorldSize` down. Both are the prescription wave 1 copies into engine
            // code. A scratch third world joins `upperWorld` so the sweep takes more than one; the
            // exercise's own world is skipped, because the rest of the section still needs it.
            var sweepScratch = World.Create();
            sweepScratch.Create(new Position { X = 4f, Y = 4f }, new Payload { Name = "swept", Depth = 4f });
            var sweepOutcome = "completed";
            try
            {
                for (var i = 0; i < World.Worlds.Length; i++)
                {
                    var live = World.Worlds[i];
                    if (live == null || ReferenceEquals(live, world)) continue;
                    World.Destroy(live);
                }
            }
            catch (Exception ex)
            {
                sweepOutcome = ex.GetType().Name + ": " + ex.Message;
            }

            Report("the by-Length sweep A7 prescribes, EXECUTED over 2 live worlds", sweepOutcome);
            Check("destroying INSIDE the by-Length walk does not throw", sweepOutcome, "completed");
            Check("...and the sweep drives World.WorldSize to the pre-sweep baseline", World.WorldSize, worldSizeBaseline);

            // And the enumeration itself, so "nothing more" stops being an assumption. Reflection
            // over another assembly's statics is exactly what a trimmed/AOT image may not answer, so
            // this is REPORTED per target rather than checked: a leg that cannot see the fields says
            // so instead of failing, and the two legs stay free to differ here.
            try
            {
                var statics = typeof(World).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                Report("static fields on Arch's World (reflected)", statics.Length);
                foreach (var field in statics)
                {
                    object value;
                    try
                    {
                        value = field.GetValue(null);
                    }
                    catch (Exception ex)
                    {
                        value = "<unreadable: " + ex.GetType().Name + ">";
                    }

                    // Collection-valued statics report their COUNT, so "never returns to baseline"
                    // is a number (the recycled-id queue holds the two ids freed above) instead of
                    // an inference from a type name. `const` fields are TAGGED, because one of these
                    // five (`InitialCapacity`) is a compile-time literal and therefore not state at
                    // all: nothing can write it, so no reset hook owes it anything.
                    Report("   World." + field.Name + (field.IsLiteral ? "  [const]" : string.Empty), Describe(value));
                }
            }
            catch (Exception ex)
            {
                Report("static-field enumeration of World", "unavailable on this target: " + ex.GetType().Name);
            }

            // ...but `World` is one TYPE, and "nothing more" is a claim about Arch's process-wide
            // state, not about that type's fields. Enumerating `typeof(World)` and then concluding
            // "nothing more" is the same shortcut this section was written to stop taking — the
            // recycled-id queue was invisible until the set was enumerated, and the set was the wrong
            // set. So the walk is widened to EVERY type in Arch's assembly that declares a non-const
            // static, and the ones outside `World` are named rather than discovered by wave 2.
            // Reported, not checked, for the same reason the ArrayRegistry member count is: a trimmed
            // /AOT image may enumerate fewer types than a JIT one, so the SIZE of the set is a
            // per-target observation. What IS checked degrades to vacuous rather than to a false
            // failure on such a leg: whatever the walk did see, none of it is left unaccounted for.
            var archStatics = ProbeArchStatics(Report);
            Report("assembly-wide static enumeration reachable on this target", archStatics.Found);
            Check("every mutable Arch static this walk saw falls in a set A7 NAMES (none left over)",
                archStatics.Unnamed, 0);

            // `World.SharedJobScheduler` reads `null` above — but that is equally consistent with
            // "nothing ever set it" and with "World.Destroy clears it", and A7 tells wave 2 to rely
            // on the SECOND reading the moment it installs a scheduler for a parallel runner. So the
            // claim is measured with a SENTINEL rather than read off a null. Installing one needs an
            // instance of Arch's scheduler type without running its constructor (it starts worker
            // threads, which the WASM leg has none of), i.e. exactly the reflection a trimmed/AOT
            // image may refuse — so the probe REPORTS whether it could install one, and the check
            // below degrades to "never observed being cleared" where it could not.
            var schedulerOutcome = "unavailable: no SharedJobScheduler backing field";
            FieldInfo schedulerField = null;
            try
            {
                foreach (var field in typeof(World).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!field.Name.Contains("SharedJobScheduler", StringComparison.Ordinal)) continue;
                    schedulerField = field;
                    break;
                }

                if (schedulerField != null)
                {
                    var sentinel = RuntimeHelpers.GetUninitializedObject(schedulerField.FieldType);
                    schedulerField.SetValue(null, sentinel);

                    var schedulerProbeWorld = World.Create();
                    World.Destroy(schedulerProbeWorld);

                    schedulerOutcome = ReferenceEquals(schedulerField.GetValue(null), sentinel)
                        ? "sentinel SURVIVED World.Create + World.Destroy"
                        : "CLEARED by World.Create/World.Destroy";
                }
            }
            catch (Exception ex)
            {
                schedulerOutcome = "unavailable: " + ex.GetType().Name;
            }
            finally
            {
                // Back to the baseline every other leg reads, whatever happened above.
                try
                {
                    schedulerField?.SetValue(null, null);
                }
                catch (Exception)
                {
                    // Nothing else in this exercise reads the scheduler; a target that cannot write
                    // the field could not have installed the sentinel either.
                }
            }

            Report("World.SharedJobScheduler sentinel across Create+Destroy", schedulerOutcome);
            Check("World.SharedJobScheduler is never CLEARED by World.Destroy",
                schedulerOutcome.StartsWith("CLEARED", StringComparison.Ordinal), false);

            // The other half, and the one C12 gets wrong: the component-type registry is NOT
            // world-scoped. It survives every Destroy, which is what makes the world above usable
            // at all — and it is why "reset the component statics to baseline" cannot be a hook.
            //
            // `Size` is the WEAKER half of that claim, and the probe below is what shows why: it is a
            // monotonic high-water mark, unchanged even by a removal that genuinely worked. So
            // "unchanged across Destroy" reads the same whether the registry is untouched or emptied,
            // and the process-lifetime claim rests on `Has<T>()` plus the behavioural ArrayRegistry
            // probe. `Size` is kept because a DROP would still falsify it — it just cannot confirm it.
            Check("ComponentRegistry.Size is unchanged by World.Destroy (weak: Size never shrinks)",
                ComponentRegistry.Size, registryBaseline);
            Check("ComponentRegistry.Has<Payload>() survives World.Destroy — the load-bearing half",
                ComponentRegistry.Has<Payload>(), true);

            // And its surface is enumerated exactly like ArrayRegistry's below, for the same reason:
            // "Arch ships no working registry reset" is a claim about a SET of entry points, and the
            // spike previously invoked one of three `Remove` overloads and generalised from it.
            var componentRegistrySurface = ProbeComponentRegistry(Report);
            Check("ComponentRegistry ships no Clear/Reset member at all (only Remove/Replace)",
                componentRegistrySurface.ClearOrReset, 0);

            // C12 says component-type registrIES, plural, and Arch really does have two.
            // `ComponentRegistry` is the one the checks above cover; `ArrayRegistry` is the one the
            // AOT negative control DIES in (`ArrayRegistry.GetArray`, finding 2), the one the AOT
            // generator primes through `ArchCoreUtilsShim`, and the one that keeps `Doomed` usable
            // after the half-clear below. It is enumerated the same way — and the surface IS the
            // finding: it registers and it hands out arrays, and it has no size, no clear, no remove.
            var arrayRegistryBefore = ProbeArrayRegistry(Report);
            Check("ArrayRegistry ships NO clear/reset/size entry point either",
                arrayRegistryBefore.ClearingMembers, 0);

            // Its store is indexed by component id and exposes no count, so "process-lifetime" is
            // measured the only way it can be: the factory registered by the worlds destroyed above
            // still hands out an array.
            Report("ArrayRegistry.GetArray(Payload, 1) after every World.Destroy so far", ArrayRegistryHandsOut());
            Check("ArrayRegistry keeps handing out Payload[] across World.Destroy", ArrayRegistryHandsOut(), "Payload[]");

            // ...and clearing it does not reset anything anyway — but the reason is NOT "the only
            // entry point throws", which is what this probe used to conclude from invoking one of the
            // three `Remove` overloads the enumeration above lists. Both are exercised below, and they
            // answer differently: the GENERIC overload throws and removes nothing, while `Remove(Type)`
            // returns normally and really does remove the entry. What survives that working removal is
            // the actual finding — `ArrayRegistry` is never unwired by either, so a "cleared" type
            // still has an array factory and its next `world.Create` reproduces the negative control.
            // `Doomed` and `Doomed2` exist solely for this pair of probes; both are left in a broken
            // registry state afterwards, so nothing else may ever touch them.
            var doomedWorld = World.Create();

            // Reported, not checked, and the two legs deliberately DIFFER here: `Doomed` is
            // `[Component]`-annotated, so on a generator leg the ModuleInitializer registered it
            // before Main ran and this reads true before anything used it. Only the negative control
            // (lazy registration) reads false. The check below is therefore about the state AFTER
            // first use — the precondition the clear probe needs — and is true on every leg.
            Report("ComponentRegistry.Has<Doomed>() BEFORE first use (generator legs are pre-primed)",
                ComponentRegistry.Has<Doomed>());
            doomedWorld.Create(new Doomed { N = 1 });
            Check("ComponentRegistry.Has<Doomed>() once the type HAS been used", ComponentRegistry.Has<Doomed>(), true);
            World.Destroy(doomedWorld);

            var clearThrew = false;
            var clearOutcome = "returned normally";
            try
            {
                ComponentRegistry.Remove<Doomed>();
            }
            catch (Exception ex)
            {
                clearThrew = true;
                clearOutcome = ex.GetType().Name + ": " + ex.Message;
            }

            Check("ComponentRegistry.Remove<T>() (the GENERIC overload) THROWS", clearThrew, true);
            Report("ComponentRegistry.Remove<Doomed>() outcome", clearOutcome);
            Check("...and removes NOTHING — the entry is still there afterwards",
                ComponentRegistry.Has<Doomed>(), true);
            Report("ComponentRegistry.Size after the generic clear", ComponentRegistry.Size);

            // Whether `Doomed` still WORKS after that failed clear is target-dependent, so it is
            // observed rather than checked: under the AOT generator the parallel ArrayRegistry was
            // primed at module init and survives, while a JIT run (lazy registration) dies inside
            // ArrayRegistry.GetArray with `ArgumentNullException (elementType)` — the negative
            // control's failure mode, reproduced at runtime by a "reset".
            var afterClear = World.Create();
            var createOutcome = "created normally";
            try
            {
                afterClear.Create(new Doomed { N = 2 });
            }
            catch (Exception ex)
            {
                createOutcome = ex.GetType().Name + ": " + ex.Message;
            }

            Report("first world.Create<Doomed> after the generic clear", createOutcome);
            World.Destroy(afterClear);

            // ...and the SECOND registry is what decides that outcome: `ComponentRegistry.Remove<T>`
            // reaches nothing here, so a target whose ArrayRegistry was primed by the generator
            // still finds `Doomed[]` and a lazily-registered one dies in `ArrayRegistry.GetArray`.
            Report("ArrayRegistry.GetArray(Doomed, 1) after the generic clear", ArrayRegistryHandsOut<Doomed>());

            // The other overload, on a type whose entry is still intact. `Remove(Type)` is what makes
            // "Arch ships no working registry reset" false as previously written: it works.
            var doomed2World = World.Create();
            doomed2World.Create(new Doomed2 { N = 1 });
            Check("ComponentRegistry.Has<Doomed2>() once the type HAS been used", ComponentRegistry.Has<Doomed2>(), true);
            World.Destroy(doomed2World);

            var sizeBeforeRemoveType = ComponentRegistry.Size;
            var removeTypeThrew = false;
            var removeTypeOutcome = "returned normally";
            try
            {
                ComponentRegistry.Remove(typeof(Doomed2));
            }
            catch (Exception ex)
            {
                removeTypeThrew = true;
                removeTypeOutcome = ex.GetType().Name + ": " + ex.Message;
            }

            Report("ComponentRegistry.Remove(Type) outcome", removeTypeOutcome);
            Check("ComponentRegistry.Remove(Type) RETURNS NORMALLY where the generic overload throws",
                removeTypeThrew, false);
            Check("...and it really removes the entry (Has True -> False)", ComponentRegistry.Has<Doomed2>(), false);

            // The measurement that makes the `Size` check above weak evidence: a removal that genuinely
            // took the entry out leaves `Size` exactly where it was. It is a high-water mark, not a
            // count of live entries, so no "Size unchanged" reading can distinguish an untouched
            // registry from a half-emptied one.
            Check("...while ComponentRegistry.Size does NOT shrink — it is a monotonic high-water mark",
                ComponentRegistry.Size, sizeBeforeRemoveType);

            // ...and this is what a working clear actually leaves behind. `ArrayRegistry` keeps the
            // factory, so the type is half-registered in exactly the shape the AOT negative control
            // dies in — the removal reproduced the crash instead of resetting anything.
            Check("ArrayRegistry still hands out Doomed2[] after a SUCCESSFUL ComponentRegistry removal",
                ArrayRegistryHandsOut<Doomed2>(), "Doomed2[]");

            var afterRemoveType = World.Create();
            var createAfterRemoveType = "created normally";
            try
            {
                afterRemoveType.Create(new Doomed2 { N = 2 });
            }
            catch (Exception ex)
            {
                createAfterRemoveType = ex.GetType().Name + ": " + ex.Message;
            }

            Report("first world.Create<Doomed2> after the WORKING Remove(Type)", createAfterRemoveType);
            World.Destroy(afterRemoveType);

            Check("...and Payload's factory is untouched by either clear", ArrayRegistryHandsOut(), "Payload[]");
        }
        catch (Exception ex)
        {
            // A target failure is a RESULT, not a crash: both heads have to be able to print it.
            failures++;
            report.AppendLine();
            report.AppendLine("   [FAIL] threw: " + ex.GetType().FullName + ": " + ex.Message);
            report.AppendLine(ex.StackTrace);
        }
        finally
        {
            // Teardown carries its own envelope for the same reason: an unhandled throw from
            // `World.Destroy` would discard the whole report, and "the world would not tear down"
            // is a RESULT this proof should print, not a crash it should die of.
            try
            {
                if (world != null) World.Destroy(world);
            }
            catch (Exception ex)
            {
                failures++;
                report.AppendLine();
                report.AppendLine("   [FAIL] World.Destroy(world) threw: " + ex.GetType().FullName + ": " + ex.Message);
            }
        }

        report.AppendLine();
        report.AppendLine($"== {checks} checks, {failures} failed ==");
        return (failures, report.ToString());
    }

    /// <summary>
    /// A static's value plus its <c>Count</c> when it has one — so a process-lifetime accumulator
    /// (the recycled-id queue, the array factories) reports a NUMBER instead of a type name.
    /// Reflection over another assembly's generics is not guaranteed on a trimmed/AOT target, so an
    /// unreadable count degrades to the plain value rather than to a failure.
    /// </summary>
    private static string Describe(object value)
    {
        if (value == null) return "null";

        var count = CountOf(value);
        return count < 0 ? value.ToString() : value + "  (Count = " + count + ")";
    }

    /// <summary>Every int-valued instance property of a store, so its SIZE surface is enumerated too.</summary>
    private static string IntProperties(object value)
    {
        if (value == null) return string.Empty;

        var rendered = new List<string>();
        try
        {
            foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.PropertyType != typeof(int) || property.GetIndexParameters().Length != 0) continue;
                rendered.Add(property.Name + " = " + property.GetValue(value));
            }
        }
        catch (Exception ex)
        {
            rendered.Add("<unreadable: " + ex.GetType().Name + ">");
        }

        return rendered.Count == 0 ? "(no int-valued size surface)" : "[" + string.Join(", ", rendered) + "]";
    }

    /// <summary>The value's <c>Count</c>, or <c>-1</c> when it has none or the target cannot read it.</summary>
    private static int CountOf(object value)
    {
        if (value == null) return -1;

        try
        {
            var count = value.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
            if (count == null || count.PropertyType != typeof(int) || count.GetIndexParameters().Length != 0) return -1;
            return (int)count.GetValue(value);
        }
        catch (Exception)
        {
            // Report-only: an unreadable Count is not a failing claim about Arch.
            return -1;
        }
    }

    /// <summary>
    /// The ids <c>World.Destroy</c> / <c>world.Dispose</c> have enqueued and nothing has dequeued, or
    /// <c>null</c> where the target cannot reflect the private static. Reading the queue's CONTENTS —
    /// not just its <c>Count</c> — is what lets the id-reuse claim be asserted as a property ("the new
    /// world took one of the freed ids") instead of as an equality against a concrete id, which would
    /// only hold while the queue happens to be empty.
    /// </summary>
    private static List<int> RecycledWorldIds()
    {
        try
        {
            FieldInfo queueField = null;
            foreach (var field in typeof(World).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!field.Name.Contains("RecycledWorldIds", StringComparison.Ordinal)) continue;
                queueField = field;
                break;
            }

            if (queueField?.GetValue(null) is not System.Collections.IEnumerable queue) return null;

            var ids = new List<int>();
            foreach (var id in queue)
            {
                if (id is int value) ids.Add(value);
            }

            return ids;
        }
        catch (Exception)
        {
            // Report-only: an unreadable queue is not a failing claim about Arch.
            return null;
        }
    }

    /// <summary>The recycled-id queue's length, or <c>"&lt;unreadable&gt;"</c>.</summary>
    private static string RecycledWorldIdCount()
    {
        var ids = RecycledWorldIds();
        return ids == null ? "<unreadable>" : ids.Count.ToString();
    }

    /// <summary>
    /// Every non-const static field declared anywhere in Arch's assembly, not only on <c>World</c> —
    /// because "<c>World.Destroy</c> per live world, <b>nothing more</b>" is a claim about Arch's
    /// process-wide state, and enumerating one type cannot carry it. Fields are grouped into the set
    /// A7 names and the rest; the rest is what the caller checks is empty.
    /// <para>
    /// Generic type definitions (<c>Component&lt;T&gt;</c>, <c>EventType&lt;T&gt;</c>,
    /// <c>JobMeta&lt;T&gt;</c>) carry one static set PER type argument, which no reflection walk can
    /// enumerate without the closed types; they are counted and named rather than read. Compiler-
    /// generated lambda caches (<c>&lt;&gt;c</c>) are counted separately — they are write-once
    /// memoisation with no observable state. As everywhere else here, a trimmed/AOT target that
    /// cannot enumerate its own assembly reports so and yields an EMPTY set, so the check degrades to
    /// vacuous on that leg rather than failing.
    /// </para>
    /// </summary>
    private static (bool Found, int Named, int Unnamed) ProbeArchStatics(Action<string, object> report)
    {
        // The types whose statics ACCUMULATE with use — the ones a reset hook would have to reach,
        // and the ones A7 has to answer for one by one.
        var accumulating = new HashSet<string>(StringComparer.Ordinal)
        {
            "Arch.Core.World",
            "Arch.Core.ComponentRegistry",
            "Arch.Core.ArrayRegistry",
            "Arch.Core.Component",
            "Arch.Core.JobMeta",
            "Arch.Core.Events.EventTypeRegistry",
        };

        // ...and the ones that are `static` without being state: `Null` sentinels Arch initialises
        // once and a padding constant. They are non-`readonly` (so a walk that filtered on `readonly`
        // would miss the accumulators AND these alike), and they are named here rather than filtered
        // out silently, because "nothing more" has to be a claim about the WHOLE enumerated set.
        var sentinels = new HashSet<string>(StringComparer.Ordinal)
        {
            "Arch.Core.Entity",
            "Arch.Core.Signature",
            "Arch.Core.QueryDescription",
            "Arch.Core.Utils.BitSet",
        };

        var namedCount = 0;
        var sentinelCount = 0;
        var unnamedCount = 0;
        var lambdaCaches = 0;
        var perTypeArgument = new List<string>();
        var namedFields = new List<string>();
        var sentinelFields = new List<string>();
        var unnamedFields = new List<string>();

        try
        {
            Type[] types;
            try
            {
                types = typeof(World).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = Array.FindAll(ex.Types, t => t != null);
            }

            foreach (var type in types)
            {
                FieldInfo[] fields;
                try
                {
                    fields = type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                }
                catch (Exception)
                {
                    continue;
                }

                var mutable = 0;
                foreach (var field in fields)
                {
                    if (!field.IsLiteral) mutable++;
                }

                if (mutable == 0) continue;

                var name = type.FullName ?? type.Name;

                if (type.IsGenericTypeDefinition)
                {
                    perTypeArgument.Add(name + " (" + mutable + ")");
                    continue;
                }

                if (name.Contains("<>", StringComparison.Ordinal))
                {
                    lambdaCaches += mutable;
                    continue;
                }

                foreach (var field in fields)
                {
                    if (field.IsLiteral) continue;

                    object value;
                    try
                    {
                        value = field.GetValue(null);
                    }
                    catch (Exception ex)
                    {
                        value = "<unreadable: " + ex.GetType().Name + ">";
                    }

                    var rendered = "   " + name + "." + field.Name + " = " + Describe(value);
                    if (accumulating.Contains(name))
                    {
                        namedCount++;
                        namedFields.Add(rendered);
                    }
                    else if (sentinels.Contains(name))
                    {
                        sentinelCount++;
                        sentinelFields.Add(rendered);
                    }
                    else
                    {
                        unnamedCount++;
                        unnamedFields.Add(rendered);
                    }
                }
            }

            report?.Invoke("Arch assembly types enumerated", types.Length);
            report?.Invoke("ACCUMULATING statics (the set A7 must answer for)", namedCount);
            foreach (var field in namedFields) report?.Invoke(field, string.Empty);
            report?.Invoke("sentinel / config statics (static, but not state)", sentinelCount);
            foreach (var field in sentinelFields) report?.Invoke(field, string.Empty);
            report?.Invoke("mutable statics in NEITHER named set", unnamedCount);
            foreach (var field in unnamedFields) report?.Invoke(field, string.Empty);
            report?.Invoke("compiler-generated lambda caches (write-once, ignored)", lambdaCaches);
            report?.Invoke("per-type-argument statics (one set per T, not enumerable, not resettable)",
                perTypeArgument.Count == 0 ? "(none reflectable on this target)" : string.Join(", ", perTypeArgument));

            return (true, namedCount, unnamedCount);
        }
        catch (Exception ex)
        {
            report?.Invoke("assembly-wide static enumeration", "unavailable on this target: " + ex.GetType().Name);
            return (false, namedCount, unnamedCount);
        }
    }

    /// <summary>
    /// The FIRST component-type registry's declared static surface, enumerated the same way
    /// <see cref="ProbeArrayRegistry"/> does the second one — because "Arch ships no working registry
    /// reset" is a claim about a set of entry points, and this registry ships THREE <c>Remove</c>
    /// overloads that do not behave alike. Returns how many members are named Clear/Reset (the caller
    /// checks it is zero: what exists is Remove/Replace, and the exercise invokes both Remove shapes
    /// rather than generalising from one). A target that cannot reflect it yields zero, vacuously.
    /// </summary>
    private static (bool Found, int ClearOrReset) ProbeComponentRegistry(Action<string, object> report)
    {
        var clearOrReset = 0;

        try
        {
            var type = typeof(ComponentRegistry);
            var members = new List<string>();

            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                var rendered = new List<string>();
                foreach (var parameter in method.GetParameters()) rendered.Add(parameter.ParameterType.Name);
                members.Add(method.Name + "(" + string.Join(", ", rendered) + ")");
            }

            foreach (var property in type.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                members.Add(property.Name);
            }

            members.Sort(StringComparer.Ordinal);

            report?.Invoke("   ComponentRegistry static members reflected", members.Count);
            report?.Invoke("   ComponentRegistry public static surface",
                members.Count == 0 ? "(none reflectable on this target)" : string.Join(", ", members));

            foreach (var name in members)
            {
                if (name.Contains("Clear", StringComparison.Ordinal) || name.Contains("Reset", StringComparison.Ordinal))
                {
                    clearOrReset++;
                }
            }

            return (true, clearOrReset);
        }
        catch (Exception ex)
        {
            report?.Invoke("ComponentRegistry surface reflected", "unavailable on this target: " + ex.GetType().Name);
            return (false, clearOrReset);
        }
    }

    /// <summary>
    /// The second component-type registry, enumerated exactly like <c>World</c>'s statics: its
    /// backing store (with a count) and its declared static surface, from which the clear/reset/size
    /// member count — the number A7 needs — is derived. A target that cannot reflect it reports
    /// <c>Found == false</c> and yields zero clearing members, which is what the caller checks.
    /// </summary>
    private static (bool Found, int ClearingMembers) ProbeArrayRegistry(Action<string, object> report)
    {
        var clearing = 0;

        try
        {
            var type = typeof(World).Assembly.GetType("Arch.Core.ArrayRegistry");
            report?.Invoke("Arch.Core.ArrayRegistry reflected", type != null);
            if (type == null) return (false, clearing);

            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object value;
                try
                {
                    value = field.GetValue(null);
                }
                catch (Exception ex)
                {
                    value = "<unreadable: " + ex.GetType().Name + ">";
                }

                report?.Invoke("   ArrayRegistry." + field.Name, Describe(value) + " " + IntProperties(value));
            }

            var members = new List<string>();
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                var parameters = method.GetParameters();
                var rendered = new List<string>();
                foreach (var parameter in parameters) rendered.Add(parameter.ParameterType.Name);
                members.Add(method.Name + "(" + string.Join(", ", rendered) + ")");
            }

            foreach (var property in type.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                members.Add(property.Name);
            }

            members.Sort(StringComparer.Ordinal);

            // The COUNT is reported next to the surface because it is what makes the clearing-member
            // check readable: NativeAOT strips this type's member metadata (measured — the AOT leg
            // reflects the type but enumerates zero fields and zero methods, while `World`'s fields
            // survive because `typeof(World)` is statically referenced), so on that leg the check
            // below passes over an EMPTY set. The surface claim is carried by the legs that can
            // still see it.
            report?.Invoke("   ArrayRegistry static members reflected", members.Count);
            report?.Invoke("   ArrayRegistry public static surface",
                members.Count == 0 ? "(none reflectable on this target)" : string.Join(", ", members));

            foreach (var name in members)
            {
                if (name.Contains("Clear", StringComparison.Ordinal)
                    || name.Contains("Remove", StringComparison.Ordinal)
                    || name.Contains("Reset", StringComparison.Ordinal)
                    || name.Contains("Size", StringComparison.Ordinal)
                    || name.Contains("Count", StringComparison.Ordinal))
                {
                    clearing++;
                }
            }

            return (true, clearing);
        }
        catch (Exception ex)
        {
            report?.Invoke("Arch.Core.ArrayRegistry reflected", "unavailable on this target: " + ex.GetType().Name);
            return (false, clearing);
        }
    }

    /// <summary>
    /// What <c>ArrayRegistry</c> hands out for a component type today — the array's type name, or the
    /// exception the AOT negative control dies of. It is the only readable answer about that store:
    /// it is indexed by component id and exposes no count (see <see cref="ProbeArrayRegistry"/>).
    /// </summary>
    private static string ArrayRegistryHandsOut<T>()
    {
        try
        {
            var array = Arch.Core.ArrayRegistry.GetArray(typeof(T), 1);
            return array == null ? "null" : array.GetType().Name;
        }
        catch (Exception ex)
        {
            return ex.GetType().Name + ": " + ex.Message;
        }
    }

    private static string ArrayRegistryHandsOut() => ArrayRegistryHandsOut<Payload>();
}
