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

            var reborn = World.Create();
            Check("World.Destroy frees the world id for reuse", reborn.Id, extraId);
            var rebornEntity = reborn.Create(new Position { X = 2f, Y = 2f }, new Payload { Name = "reborn", Depth = 2f });
            Check("a world created AFTER Destroy still builds a managed archetype",
                reborn.Get<Payload>(rebornEntity).Name, "reborn");
            World.Destroy(reborn);
            Check("World.WorldSize back to baseline again", World.WorldSize, worldSizeBaseline);

            // ...but "back to baseline" is a claim about `World.Worlds`/`World.WorldSize` ONLY, and
            // A7's "World.Destroy per live world, nothing more" is only checkable if the set of
            // process-wide statics is ENUMERATED rather than guessed. Arch holds a THIRD one:
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

                    Report("   World." + field.Name, value == null ? "null" : value.ToString());
                }
            }
            catch (Exception ex)
            {
                Report("static-field enumeration of World", "unavailable on this target: " + ex.GetType().Name);
            }

            // The other half, and the one C12 gets wrong: the component-type registry is NOT
            // world-scoped. It survives every Destroy, which is what makes the world above usable
            // at all — and it is why "reset the component statics to baseline" cannot be a hook.
            Check("ComponentRegistry.Size is unchanged by World.Destroy", ComponentRegistry.Size, registryBaseline);
            Check("ComponentRegistry.Has<Payload>() survives World.Destroy", ComponentRegistry.Has<Payload>(), true);

            // ...and there is no way to clear it anyway: Arch 2.1.0's only entry point for that is
            // BROKEN. `Doomed` exists solely for this probe — it is left in a half-cleared state
            // afterwards, so nothing else may ever touch it.
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

            Check("ComponentRegistry.Remove<T>() THROWS — Arch ships no working registry reset", clearThrew, true);
            Report("ComponentRegistry.Remove<Doomed>() outcome", clearOutcome);
            Report("ComponentRegistry.Has<Doomed>() after the clear", ComponentRegistry.Has<Doomed>());
            Report("ComponentRegistry.Size after the clear", ComponentRegistry.Size);

            // Whether the type still WORKS after the half-clear is target-dependent, so it is
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

            Report("first world.Create<Doomed> after the clear", createOutcome);
            World.Destroy(afterClear);
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
}
