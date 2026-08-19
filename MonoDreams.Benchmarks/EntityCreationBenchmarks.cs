using BenchmarkDotNet.Attributes;
using ArchWorld = Arch.Core.World;
using DefaultEcsWorld = DefaultEcs.World;

namespace MonoDreams.Benchmarks;

/// <summary>
/// Entity creation: build a world from empty and populate it with <see cref="EntityCount"/>
/// entities, once per measured operation.
/// <para>
/// This is the level-load shape — <c>LDtkTileParserSystem</c> / <c>SceneReaderSystem</c> minting a
/// whole scene's entities in one burst — and it is where the two backends differ most in kind:
/// DefaultEcs adds components one at a time to an already-created entity (each <c>Set</c> touching
/// a sparse-set pool), while Arch mints the entity directly into its final archetype from the
/// component list. Both idioms below are the natural one for their backend, so the comparison is
/// "what the calling code would actually look like", not a forced symmetry.
/// </para>
/// The world is torn down in <see cref="IterationCleanup"/>, outside the measured region.
/// </summary>
[Config(typeof(PerInvocationConfig))]
public class EntityCreationBenchmarks
{
    [Params(10_000, 100_000, 1_000_000)]
    public int EntityCount;

    private DefaultEcsWorld? _defaultEcsWorld;
    private ArchWorld? _archWorld;

    [IterationCleanup]
    public void Cleanup()
    {
        _defaultEcsWorld?.Dispose();
        _defaultEcsWorld = null;

        if (_archWorld is { } archWorld)
        {
            // Not Dispose(): Arch keeps every live world in the static World.Worlds table, and only
            // World.Destroy releases the slot. Leaking them across a million-entity benchmark would
            // both grow that table without bound and keep the archetype arrays alive.
            ArchWorld.Destroy(archWorld);
            _archWorld = null;
        }
    }

    [Benchmark(Description = "DefaultEcs · create + 2 struct components")]
    public int DefaultEcs_Struct()
    {
        var world = new DefaultEcsWorld();
        _defaultEcsWorld = world;

        for (var i = 0; i < EntityCount; i++)
        {
            var entity = world.CreateEntity();
            entity.Set(new BenchPosition { X = i, Y = i });
            entity.Set(new BenchVelocity { X = 1f, Y = -1f });
        }

        return EntityCount;
    }

    [Benchmark(Description = "Arch · create + 2 struct components")]
    public int Arch_Struct()
    {
        var world = ArchWorld.Create();
        _archWorld = world;

        for (var i = 0; i < EntityCount; i++)
        {
            world.Create(new BenchPosition { X = i, Y = i }, new BenchVelocity { X = 1f, Y = -1f });
        }

        return EntityCount;
    }

    [Benchmark(Description = "DefaultEcs · create + struct + managed DrawComponent")]
    public int DefaultEcs_Managed()
    {
        var world = new DefaultEcsWorld();
        _defaultEcsWorld = world;

        for (var i = 0; i < EntityCount; i++)
        {
            var entity = world.CreateEntity();
            entity.Set(new BenchPosition { X = i, Y = i });
            entity.Set(NewDraw(i));
        }

        return EntityCount;
    }

    [Benchmark(Description = "Arch · create + struct + managed DrawComponent")]
    public int Arch_Managed()
    {
        var world = ArchWorld.Create();
        _archWorld = world;

        for (var i = 0; i < EntityCount; i++)
        {
            world.Create(new BenchPosition { X = i, Y = i }, NewDraw(i));
        }

        return EntityCount;
    }

    /// <summary>
    /// One managed component instance per entity — the allocation is part of what the managed case
    /// costs, exactly as it is when an entity factory builds a real <c>DrawComponent</c>.
    /// </summary>
    private static BenchDrawComponent NewDraw(int i) => new()
    {
        Type = 0,
        Target = 0,
        Position = new System.Numerics.Vector2(i, i),
        LayerDepth = 0.5f,
        Size = new System.Numerics.Vector2(16f, 16f),
        SourceRectangle = new BenchRectangle { X = 0, Y = 0, Width = 16, Height = 16 },
    };
}
