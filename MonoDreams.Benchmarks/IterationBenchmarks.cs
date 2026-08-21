using System.Numerics;
using Arch.Core;
using BenchmarkDotNet.Attributes;
using DefaultEcs;
using ArchWorld = Arch.Core.World;
using DefaultEcsWorld = DefaultEcs.World;

namespace MonoDreams.Benchmarks;

/// <summary>
/// Iteration: one full pass over <see cref="EntityCount"/> matching entities, reading one component
/// and writing another — the shape of every per-frame system in the engine
/// (<c>VelocitySystem</c>, <c>SpritePrepSystem</c>, <c>YSortSystem</c>).
/// <para>
/// Three idioms are measured, because they are the three the migration has to choose between:
/// </para>
/// <list type="bullet">
/// <item><description><b>DefaultEcs</b> — a pre-built <see cref="EntitySet"/> enumerated as a span of
/// entities, each component fetched through <c>entity.Get&lt;T&gt;()</c>. This is what
/// <c>AEntitySetSystem</c> does today, so it is the number every current system pays.</description></item>
/// <item><description><b>Arch (per-entity query)</b> — <c>world.Query(in description, ForEach)</c>, the
/// closest analogue to today's per-entity <c>Update</c> override, and what the facade's
/// <c>EntitySystem&lt;T&gt;</c> will run by default in wave 2.</description></item>
/// <item><description><b>Arch (chunk loop)</b> — walking chunks and indexing contiguous spans. This is
/// the wave-3 conversion target, so the gap between it and the per-entity query is the size of the
/// prize wave 3 is chasing.</description></item>
/// </list>
/// The world is built once in <see cref="Setup"/>; the operation only reads and writes existing
/// components, so it is self-restoring in shape (values drift, memory does not).
/// </summary>
[Config(typeof(SelfRestoringConfig))]
public class IterationBenchmarks
{
    private const float Dt = 1f / 60f;

    [Params(10_000, 100_000, 1_000_000)]
    public int EntityCount;

    private DefaultEcsWorld _defaultEcsStructWorld = null!;
    private EntitySet _defaultEcsStructSet = null!;
    private DefaultEcsWorld _defaultEcsManagedWorld = null!;
    private EntitySet _defaultEcsManagedSet = null!;

    private ArchWorld _archStructWorld = null!;
    private ArchWorld _archManagedWorld = null!;
    private QueryDescription _structQuery;
    private QueryDescription _managedQuery;

    private ForEach<BenchPosition, BenchVelocity> _structForEach = null!;
    private ForEach<BenchPosition, BenchDrawComponent> _managedForEach = null!;

    /// <summary>
    /// Written by the delegate-based Arch queries, which cannot return a value. Keeping it on the
    /// instance (and returning it from the benchmark) is what stops the JIT from eliding the loop.
    /// </summary>
    private float _checksum;

    [GlobalSetup]
    public void Setup()
    {
        _defaultEcsStructWorld = new DefaultEcsWorld();
        for (var i = 0; i < EntityCount; i++)
        {
            var entity = _defaultEcsStructWorld.CreateEntity();
            entity.Set(new BenchPosition { X = i, Y = i });
            entity.Set(new BenchVelocity { X = 1f, Y = -1f });
        }
        _defaultEcsStructSet = _defaultEcsStructWorld.GetEntities()
            .With<BenchPosition>()
            .With<BenchVelocity>()
            .AsSet();

        _defaultEcsManagedWorld = new DefaultEcsWorld();
        for (var i = 0; i < EntityCount; i++)
        {
            var entity = _defaultEcsManagedWorld.CreateEntity();
            entity.Set(new BenchPosition { X = i, Y = i });
            entity.Set(NewDraw(i));
        }
        _defaultEcsManagedSet = _defaultEcsManagedWorld.GetEntities()
            .With<BenchPosition>()
            .With<BenchDrawComponent>()
            .AsSet();

        _archStructWorld = ArchWorld.Create();
        for (var i = 0; i < EntityCount; i++)
        {
            _archStructWorld.Create(
                new BenchPosition { X = i, Y = i },
                new BenchVelocity { X = 1f, Y = -1f });
        }

        _archManagedWorld = ArchWorld.Create();
        for (var i = 0; i < EntityCount; i++)
        {
            _archManagedWorld.Create(new BenchPosition { X = i, Y = i }, NewDraw(i));
        }

        _structQuery = new QueryDescription().WithAll<BenchPosition, BenchVelocity>();
        _managedQuery = new QueryDescription().WithAll<BenchPosition, BenchDrawComponent>();

        // Allocated once, so the per-operation cost is dispatch only, never delegate allocation.
        _structForEach = (ref BenchPosition position, ref BenchVelocity velocity) =>
        {
            position.X += velocity.X * Dt;
            position.Y += velocity.Y * Dt;
            _checksum += position.X;
        };
        _managedForEach = (ref BenchPosition position, ref BenchDrawComponent draw) =>
        {
            draw.Position = new Vector2(position.X, position.Y);
            draw.LayerDepth = position.Y * 0.0001f;
            _checksum += draw.LayerDepth;
        };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _defaultEcsStructSet.Dispose();
        _defaultEcsManagedSet.Dispose();
        _defaultEcsStructWorld.Dispose();
        _defaultEcsManagedWorld.Dispose();
        ArchWorld.Destroy(_archStructWorld);
        ArchWorld.Destroy(_archManagedWorld);
    }

    [Benchmark(Description = "DefaultEcs · EntitySet + entity.Get (struct)")]
    public float DefaultEcs_Struct()
    {
        var sum = 0f;
        var entities = _defaultEcsStructSet.GetEntities();
        for (var i = 0; i < entities.Length; i++)
        {
            var entity = entities[i];
            ref var position = ref entity.Get<BenchPosition>();
            ref var velocity = ref entity.Get<BenchVelocity>();
            position.X += velocity.X * Dt;
            position.Y += velocity.Y * Dt;
            sum += position.X;
        }

        return sum;
    }

    [Benchmark(Description = "Arch · per-entity query (struct)")]
    public float Arch_Struct_Query()
    {
        _checksum = 0f;
        _archStructWorld.Query(in _structQuery, _structForEach);
        return _checksum;
    }

    [Benchmark(Description = "Arch · chunk loop (struct)")]
    public float Arch_Struct_Chunk()
    {
        var sum = 0f;
        foreach (ref var chunk in _archStructWorld.Query(in _structQuery))
        {
            var positions = chunk.GetSpan<BenchPosition>();
            var velocities = chunk.GetSpan<BenchVelocity>();
            foreach (var index in chunk)
            {
                ref var position = ref positions[index];
                ref var velocity = ref velocities[index];
                position.X += velocity.X * Dt;
                position.Y += velocity.Y * Dt;
                sum += position.X;
            }
        }

        return sum;
    }

    [Benchmark(Description = "DefaultEcs · EntitySet + entity.Get (managed DrawComponent)")]
    public float DefaultEcs_Managed()
    {
        var sum = 0f;
        var entities = _defaultEcsManagedSet.GetEntities();
        for (var i = 0; i < entities.Length; i++)
        {
            var entity = entities[i];
            ref var position = ref entity.Get<BenchPosition>();
            var draw = entity.Get<BenchDrawComponent>();
            draw.Position = new Vector2(position.X, position.Y);
            draw.LayerDepth = position.Y * 0.0001f;
            sum += draw.LayerDepth;
        }

        return sum;
    }

    [Benchmark(Description = "Arch · per-entity query (managed DrawComponent)")]
    public float Arch_Managed_Query()
    {
        _checksum = 0f;
        _archManagedWorld.Query(in _managedQuery, _managedForEach);
        return _checksum;
    }

    [Benchmark(Description = "Arch · chunk loop (managed DrawComponent)")]
    public float Arch_Managed_Chunk()
    {
        var sum = 0f;
        foreach (ref var chunk in _archManagedWorld.Query(in _managedQuery))
        {
            var positions = chunk.GetSpan<BenchPosition>();
            var draws = chunk.GetSpan<BenchDrawComponent>();
            foreach (var index in chunk)
            {
                ref var position = ref positions[index];
                var draw = draws[index];
                draw.Position = new Vector2(position.X, position.Y);
                draw.LayerDepth = position.Y * 0.0001f;
                sum += draw.LayerDepth;
            }
        }

        return sum;
    }

    private static BenchDrawComponent NewDraw(int i) => new()
    {
        Position = new Vector2(i, i),
        LayerDepth = 0.5f,
        Size = new Vector2(16f, 16f),
        SourceRectangle = new BenchRectangle { X = 0, Y = 0, Width = 16, Height = 16 },
    };
}
