using BenchmarkDotNet.Attributes;
using ArchEntity = Arch.Core.Entity;
using ArchWorld = Arch.Core.World;
using DefaultEcsEntity = DefaultEcs.Entity;
using DefaultEcsWorld = DefaultEcs.World;

namespace MonoDreams.Benchmarks;

/// <summary>
/// Shared world setup for the churn families: <see cref="EntityCount"/> entities carrying two struct
/// components in each backend, plus the entity handles the benchmarks churn over.
/// <para>
/// Structural churn — add a component to every entity, then remove it from every entity — is the
/// single most load-bearing measurement of the whole comparison. <c>CullingSystem</c> adds and
/// removes <c>VisibleComponent</c> as entities enter and leave the camera view EVERY FRAME, so the
/// cost of a structural change is a per-frame cost in MonoDreams, not a level-load cost. Under
/// DefaultEcs it is a bitmask flip in a sparse set; under Arch it moves the entity (and every
/// component it carries) into a different archetype's chunk.
/// </para>
/// </summary>
public abstract class ChurnBenchmarkBase
{
    [Params(10_000, 100_000, 1_000_000)]
    public int EntityCount;

    // Named "…UnderTest" so the field never shadows the type alias of the same name: the churn
    // methods have to say both `ArchWorld.Destroy(…)` (the type) and the world instance.
    protected DefaultEcsWorld DefaultEcsWorldUnderTest = null!;
    protected DefaultEcsEntity[] DefaultEcsEntities = null!;
    protected ArchWorld ArchWorldUnderTest = null!;
    protected ArchEntity[] ArchEntities = null!;

    /// <summary>
    /// One shared instance for every add. The managed churn benchmark measures the cost of moving a
    /// reference-typed component between archetypes/pools, not the cost of allocating it — entity
    /// creation (<see cref="EntityCreationBenchmarks"/>) is where allocation is measured.
    /// </summary>
    protected readonly BenchDrawComponent SharedDraw = new();

    [GlobalSetup]
    public void Setup()
    {
        DefaultEcsWorldUnderTest = new DefaultEcsWorld();
        DefaultEcsEntities = new DefaultEcsEntity[EntityCount];
        for (var i = 0; i < EntityCount; i++)
        {
            var entity = DefaultEcsWorldUnderTest.CreateEntity();
            entity.Set(new BenchPosition { X = i, Y = i });
            entity.Set(new BenchVelocity { X = 1f, Y = -1f });
            DefaultEcsEntities[i] = entity;
        }

        ArchWorldUnderTest = ArchWorld.Create();
        ArchEntities = new ArchEntity[EntityCount];
        for (var i = 0; i < EntityCount; i++)
        {
            ArchEntities[i] = ArchWorldUnderTest.Create(
                new BenchPosition { X = i, Y = i },
                new BenchVelocity { X = 1f, Y = -1f });
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        DefaultEcsWorldUnderTest.Dispose();
        ArchWorld.Destroy(ArchWorldUnderTest);
    }
}

/// <summary>
/// Structural churn with a payload both backends handle on their normal path: a one-byte tag, and a
/// managed (<c>DrawComponent</c>-shaped) component.
/// <para>
/// The one-byte tag is the architectural question in its purest form — sparse-set bitmask flip
/// versus archetype move — with DefaultEcs's zero-sized-component pathology
/// (see <see cref="ZeroSizedTagChurnBenchmarks"/>) deliberately designed out of it. The managed case
/// repeats the same churn with a reference-typed component riding along.
/// </para>
/// <para>
/// The operation restores the world exactly (everything added is removed again) and every cell here
/// completes in tens of milliseconds, so this family runs under the auto-tuned
/// <see cref="SelfRestoringConfig"/>: BenchmarkDotNet batches many operations per iteration, which
/// reports warm steady-state cost — the right model for work a game does every frame.
/// </para>
/// </summary>
[Config(typeof(SelfRestoringConfig))]
public class StructuralChurnBenchmarks : ChurnBenchmarkBase
{
    [Benchmark(Description = "DefaultEcs · add+remove one-byte tag")]
    public void DefaultEcs_ByteTagChurn()
    {
        var entities = DefaultEcsEntities;
        for (var i = 0; i < entities.Length; i++) entities[i].Set(new BenchTagByte { Value = 1 });
        for (var i = 0; i < entities.Length; i++) entities[i].Remove<BenchTagByte>();
    }

    [Benchmark(Description = "Arch · add+remove one-byte tag")]
    public void Arch_ByteTagChurn()
    {
        var entities = ArchEntities;
        var world = ArchWorldUnderTest;
        for (var i = 0; i < entities.Length; i++) world.Add(entities[i], new BenchTagByte { Value = 1 });
        for (var i = 0; i < entities.Length; i++) world.Remove<BenchTagByte>(entities[i]);
    }

    [Benchmark(Description = "DefaultEcs · add+remove managed DrawComponent")]
    public void DefaultEcs_ManagedChurn()
    {
        var entities = DefaultEcsEntities;
        var draw = SharedDraw;
        for (var i = 0; i < entities.Length; i++) entities[i].Set(draw);
        for (var i = 0; i < entities.Length; i++) entities[i].Remove<BenchDrawComponent>();
    }

    [Benchmark(Description = "Arch · add+remove managed DrawComponent")]
    public void Arch_ManagedChurn()
    {
        var entities = ArchEntities;
        var world = ArchWorldUnderTest;
        var draw = SharedDraw;
        for (var i = 0; i < entities.Length; i++) world.Add(entities[i], draw);
        for (var i = 0; i < entities.Length; i++) world.Remove<BenchDrawComponent>(entities[i]);
    }
}

/// <summary>
/// The same churn with a ZERO-SIZED tag — <c>VisibleComponent</c>'s exact shape, so this family is
/// what the engine pays today, every frame, for culling.
/// <para>
/// It is separated from <see cref="StructuralChurnBenchmarks"/> because DefaultEcs 0.18.0-beta01
/// handles zero-sized components on a special path whose <c>Remove</c> degrades with the number of
/// entities currently carrying the component: a full 1M sweep takes over two MINUTES per operation,
/// against tens of milliseconds for the identical sweep with a one-byte tag. Auto-tuning that cell
/// would run for hours, so this family runs under <see cref="HeavyChurnConfig"/> (one invocation per
/// iteration, three iterations).
/// </para>
/// <para>
/// Consequence for reading the numbers: both backends here are measured cold — one operation per
/// iteration, no batching — so rows within THIS family compare fairly against each other, but not
/// against the warm steady-state rows of <see cref="StructuralChurnBenchmarks"/>. The one-byte tag
/// row over there is the like-for-like reference for the Arch column here.
/// </para>
/// </summary>
[Config(typeof(HeavyChurnConfig))]
public class ZeroSizedTagChurnBenchmarks : ChurnBenchmarkBase
{
    [Benchmark(Description = "DefaultEcs · add+remove zero-sized tag (VisibleComponent shape)")]
    public void DefaultEcs_ZeroSizedTagChurn()
    {
        var entities = DefaultEcsEntities;
        for (var i = 0; i < entities.Length; i++) entities[i].Set<BenchVisible>(default);
        for (var i = 0; i < entities.Length; i++) entities[i].Remove<BenchVisible>();
    }

    [Benchmark(Description = "Arch · add+remove zero-sized tag (VisibleComponent shape)")]
    public void Arch_ZeroSizedTagChurn()
    {
        var entities = ArchEntities;
        var world = ArchWorldUnderTest;
        for (var i = 0; i < entities.Length; i++) world.Add<BenchVisible>(entities[i]);
        for (var i = 0; i < entities.Length; i++) world.Remove<BenchVisible>(entities[i]);
    }
}
