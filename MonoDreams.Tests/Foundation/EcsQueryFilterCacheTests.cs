using DefaultEcs;
using Xunit;

namespace MonoDreams.Tests.Foundation;

/// <summary>
/// Guards the one entry of <see cref="ProcessWideState"/> that is not the engine's own: DefaultEcs's
/// <c>static</c> query-filter memo cache.
///
/// <para><b>What can go wrong.</b> The cache keys a query's filter by a flattened string, and
/// <c>ComponentEnum.ToString()</c> renders the raw bitset as UTF-16 — so a component flag sitting on
/// bit 5 or 21 of a word produces the literal separator character and two different rules collide on
/// one key. The loser silently runs on the winner's predicate and matches nothing. Which pair
/// collides depends on the global <c>ComponentFlag</c> assignment order, i.e. on the order the
/// assembly's test classes happen to run in, which is why it presents as issue #114's "red under one
/// shuffle, green under the next" (reproduced here with <c>MONODREAMS_TEST_SEED=8</c>, where
/// <c>EditorStatusBarSystemTests</c> read an empty entity set). Emptying the cache after every test
/// cannot fix the library, but it stops a poisoned key from outliving the test that created it.</para>
///
/// <para>These tests deliberately assert on the plumbing rather than on a synthesised collision: the
/// bit positions needed to force one depend on how many components the rest of the suite has already
/// registered, so a "reproduce the collision" test would itself be order-dependent — the exact
/// property being engineered out.</para>
/// </summary>
public class EcsQueryFilterCacheTests
{
    /// <summary>
    /// The reset is reflection over an <c>internal</c> field, so a DefaultEcs upgrade that renames or
    /// moves it would turn <see cref="ProcessWideState.Reset"/>'s clear into a silent no-op and bring
    /// the seasonal failure back. This is the one place that says so out loud.
    /// </summary>
    [Fact]
    public void TheCacheIsStillReachable_SoTheResetIsNotSilentlyANoOp()
    {
        Assert.True(
            ProcessWideState.EcsQueryFilterCacheIsReachable,
            "DefaultEcs.Internal.EntityQueryFilterFactory._filters was not found. A DefaultEcs upgrade " +
            "probably renamed it; ProcessWideState.ResetEcsQueryFilterCache must be re-pointed, or its " +
            "leak (one test's filter predicate reaching another test's query) comes back.");
    }

    /// <summary>Builds a query so the cache is demonstrably populated, then proves
    /// <see cref="ProcessWideState.Reset"/> empties it — the guarantee the guard attribute extends to
    /// every test in the assembly.</summary>
    [Fact]
    public void Reset_EmptiesTheCache_SoAPredicateCannotOutliveItsTest()
    {
        using (var world = new World())
        {
            var entity = world.CreateEntity();
            entity.Set(new CacheProbe());
            using var set = world.GetEntities().With<CacheProbe>().AsSet();
            Assert.Equal(1, set.Count);
        }

        Assert.True(
            ProcessWideState.EcsQueryFilterCacheCount > 0,
            "building a query should have memoised at least one predicate");

        ProcessWideState.Reset();

        Assert.Equal(0, ProcessWideState.EcsQueryFilterCacheCount);
    }

    /// <summary>A component owned by this file alone, so the query above cannot be answered from a
    /// predicate another test put in the cache.</summary>
    private struct CacheProbe { }
}
