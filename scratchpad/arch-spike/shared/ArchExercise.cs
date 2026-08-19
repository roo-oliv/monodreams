using System;
using System.Collections.Generic;
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

        void Report(string label, object value) => report.AppendLine($"   {label,-52} : {value}");

        void Section(string title)
        {
            report.AppendLine();
            report.AppendLine("-- " + title);
        }

        void Check<T>(string label, T actual, T expected)
        {
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

        var world = World.Create();
        try
        {
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
            World.Destroy(world);
        }

        report.AppendLine();
        report.AppendLine(failures == 0 ? "== ALL CHECKS PASSED ==" : $"== {failures} CHECK(S) FAILED ==");
        return (failures, report.ToString());
    }
}
