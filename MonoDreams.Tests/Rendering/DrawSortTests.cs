using System;
using System.Collections.Generic;
using System.Linq;
using DefaultEcs;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component.Draw;
using MonoDreams.System.Draw;
using Xunit;

namespace MonoDreams.Tests.Rendering;

/// <summary>
/// Guards the rendering premise "The per-frame draw sort is allocation-free and stably ordered".
/// <para>
/// <c>MasterRenderSystem.Update</c> used to put a pass's draw elements in painter's order through a
/// LINQ chain — <c>GetEntities().ToArray() → Select((entity, index)) → Where(mesh valid) →
/// OrderBy(LayerDepth) → ThenBy(index) → ToList()</c> — allocating an entity array, an enumerable
/// chain and a <c>List</c> every frame, per render pass. It now rebuilds and sorts a reused
/// grow-only <see cref="DrawSortBuffer"/> in place. The subtlety these tests exist for:
/// <c>OrderBy(...).ThenBy(...)</c> is a <em>stable</em> sort, <c>Array.Sort</c> is <em>not</em>, so
/// the buffer carries each element's draw-set position and compares it explicitly as the tiebreaker.
/// Drop that tiebreaker and same-depth elements scramble — which in this engine means flicker in
/// exactly the case "Y-sort tiebreaker is parent-child bias only" says falls through to insertion
/// order.
/// </para>
/// <para>
/// <c>MasterRenderSystem.Update</c> needs a live <c>GraphicsDevice</c>, so (following the
/// <c>SpriteBatchFlushTests</c> precedent) these tests drive the <em>same</em>
/// <see cref="DrawSortBuffer"/> the renderer's frame path calls — <c>Rebuild</c> then <c>Sort</c> —
/// rather than a copy of its logic.
/// </para>
/// </summary>
public class DrawSortTests
{
    /// <summary>
    /// The ordering the renderer's LINQ chain produced, verbatim, over the same draw-set order.
    /// This is the oracle: whatever <see cref="DrawSortBuffer"/> does must equal it element for
    /// element, ties included.
    /// </summary>
    private static List<(Entity entity, int index, DrawComponent dc)> LegacyLinqOrder(Entity[] entities) =>
        entities
            .Select((entity, index) => (entity, index, dc: entity.Get<DrawComponent>()))
            .Where(x => x.dc.Type != DrawElementType.Mesh || x.dc.HasValidMesh)
            .OrderBy(x => x.dc.LayerDepth)
            .ThenBy(x => x.index) // Stable sort
            .ToList();

    private static void AssertMatchesLegacyOrder(Entity[] entities, DrawSortBuffer buffer)
    {
        var expected = LegacyLinqOrder(entities);

        Assert.Equal(expected.Count, buffer.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].entity, buffer.Items[i].entity);
            Assert.Equal(expected[i].index, buffer.Items[i].index);
            Assert.Same(expected[i].dc, buffer.Items[i].dc);
        }
    }

    /// A depth-varied draw set with heavy ties — the shape a real pass has after
    /// SpritePrepSystem/YSortSystem quantize many entities onto the same layer.
    private static Entity[] SpawnMixedPass(World world, int count, int seed = 1234)
    {
        var random = new Random(seed);
        var entities = new Entity[count];
        for (var i = 0; i < count; i++)
        {
            // Only a handful of distinct depths, so most comparisons are ties: this is what makes
            // the (unstable) introsort visibly scramble the order without the index tiebreaker.
            var depth = random.Next(6) / 10f;
            var type = random.Next(4) switch
            {
                0 => DrawElementType.Text,
                1 => DrawElementType.NinePatch,
                2 => DrawElementType.Mesh,
                _ => DrawElementType.Sprite,
            };
            var entity = world.CreateEntity();
            entity.Set(NewDraw(type, depth));
            entities[i] = entity;
        }
        return entities;
    }

    private static DrawComponent NewDraw(DrawElementType type, float layerDepth)
    {
        var dc = new DrawComponent { Type = type, LayerDepth = layerDepth, Target = RenderTargetID.Main };
        // Every Mesh spawned by the mixed-pass helper carries mesh data, so it survives the filter;
        // the exclusion case is asserted on its own below.
        if (type == DrawElementType.Mesh) GiveMeshData(dc);
        return dc;
    }

    private static void GiveMeshData(DrawComponent dc)
    {
        dc.Vertices = new VertexPositionColor[3];
        dc.Indices = new[] { 0, 1, 2 };
    }

    /// <summary>
    /// The headline equivalence: over a realistic depth-varied pass, the reused buffer's order is
    /// element-for-element what the old LINQ chain produced — same entities, same recorded indices,
    /// same DrawComponent instances, ties included.
    /// </summary>
    [Fact]
    public void SortedOrder_IsElementForElementIdenticalToTheLegacyLinqChain()
    {
        using var world = new World();
        var entities = SpawnMixedPass(world, 500);

        var buffer = new DrawSortBuffer();
        buffer.Rebuild(entities);
        buffer.Sort();

        AssertMatchesLegacyOrder(entities, buffer);

        // Sanity: the fixture really does exercise ties (otherwise the parity assert above is weak).
        var depths = entities.Select(e => e.Get<DrawComponent>().LayerDepth).ToList();
        Assert.True(depths.Distinct().Count() < depths.Count / 10, "fixture must be tie-heavy");
    }

    /// <summary>
    /// Stability, isolated: a pass where EVERY element shares one depth must come out in exactly the
    /// draw-set order. Array.Sort is introsort — with all keys equal and no index tiebreaker its
    /// quicksort partitioning reorders the array, so this test fails the moment the tiebreaker (or
    /// the recorded set-order index) is dropped.
    /// </summary>
    [Fact]
    public void EqualDepthElements_KeepDrawSetOrder()
    {
        using var world = new World();
        var entities = new Entity[200];
        for (var i = 0; i < entities.Length; i++)
        {
            var entity = world.CreateEntity();
            entity.Set(NewDraw(DrawElementType.Sprite, 0.5f));
            entities[i] = entity;
        }

        var buffer = new DrawSortBuffer();
        buffer.Rebuild(entities);
        buffer.Sort();

        Assert.Equal(entities.Length, buffer.Count);
        for (var i = 0; i < entities.Length; i++)
        {
            Assert.Equal(entities[i], buffer.Items[i].entity);
            Assert.Equal(i, buffer.Items[i].index);
        }
        AssertMatchesLegacyOrder(entities, buffer);
    }

    /// <summary>
    /// The rebuild keeps the old <c>.Where(x =&gt; x.dc.Type != Mesh || x.dc.HasValidMesh)</c> filter:
    /// a Mesh element with no vertices/indices has nothing to submit and is dropped; every other
    /// element — including a Mesh that DOES have data, and a Sprite/Text/NinePatch with no mesh data
    /// at all — is kept.
    /// </summary>
    [Fact]
    public void MeshWithoutMeshData_IsExcluded_EverythingElseIsKept()
    {
        using var world = new World();

        var sprite = NewDraw(DrawElementType.Sprite, 0.1f);
        var emptyMesh = new DrawComponent { Type = DrawElementType.Mesh, LayerDepth = 0.2f }; // no data
        var text = NewDraw(DrawElementType.Text, 0.3f);
        var verticesOnlyMesh = new DrawComponent { Type = DrawElementType.Mesh, LayerDepth = 0.4f };
        verticesOnlyMesh.Vertices = new VertexPositionColor[3]; // still no indices ⇒ invalid
        var ninePatch = NewDraw(DrawElementType.NinePatch, 0.5f);
        var validMesh = NewDraw(DrawElementType.Mesh, 0.6f);

        var draws = new[] { sprite, emptyMesh, text, verticesOnlyMesh, ninePatch, validMesh };
        var entities = new Entity[draws.Length];
        for (var i = 0; i < draws.Length; i++)
        {
            var entity = world.CreateEntity();
            entity.Set(draws[i]);
            entities[i] = entity;
        }

        var buffer = new DrawSortBuffer();
        buffer.Rebuild(entities);
        buffer.Sort();

        var kept = Enumerable.Range(0, buffer.Count).Select(i => buffer.Items[i].dc).ToList();
        Assert.Equal(new[] { sprite, text, ninePatch, validMesh }, kept);
        Assert.DoesNotContain(emptyMesh, kept);
        Assert.DoesNotContain(verticesOnlyMesh, kept);
        AssertMatchesLegacyOrder(entities, buffer);
    }

    /// <summary>
    /// The point of the change: a busy pass must allocate NOTHING to sort. After warm-up (which grows
    /// the buffer to its high-water mark and settles the JIT / the sort helper's one-time statics),
    /// many rebuild+sort cycles over a 5000-element pass must not move
    /// <c>GC.GetAllocatedBytesForCurrentThread()</c> at all. The old LINQ chain allocated an entity
    /// array, three enumerator chains and a List per cycle.
    /// </summary>
    [Fact]
    public void BusyPass_RebuildAndSort_AllocatesNothing()
    {
        using var world = new World();
        var entities = SpawnMixedPass(world, 5000);
        var buffer = new DrawSortBuffer();

        // Warm-up: grow the buffer to the pass's high-water mark and let the JIT + Array.Sort's
        // cached helper/comparer statics settle, so the measured window only sees steady-state work.
        for (var i = 0; i < 200; i++)
        {
            buffer.Rebuild(entities);
            buffer.Sort();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 500; i++)
        {
            buffer.Rebuild(entities);
            buffer.Sort();
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// The buffer is grow-only, so a frame smaller than an earlier one leaves stale tuples past
    /// <c>Count</c>. Those must be cleared: a leftover tuple pins a (possibly disposed) entity's
    /// <c>DrawComponent</c> — and with it a <c>Texture2D</c> or a mesh's vertex/index arrays — for the
    /// pass's whole lifetime, which is the retention counterpart of the leak the once-per-instance
    /// draw set premise fixed.
    /// </summary>
    [Fact]
    public void ShrinkingPass_ReleasesTheStaleTail()
    {
        using var world = new World();
        var big = SpawnMixedPass(world, 300, seed: 7);
        var buffer = new DrawSortBuffer();

        buffer.Rebuild(big);
        buffer.Sort();
        var bigCount = buffer.Count;
        Assert.True(bigCount > 10);

        var small = big.Take(5).ToArray();
        buffer.Rebuild(small);
        buffer.Sort();

        Assert.Equal(5, buffer.Count);
        for (var i = buffer.Count; i < bigCount; i++)
        {
            Assert.Null(buffer.Items[i].dc);
            Assert.Equal(default(Entity), buffer.Items[i].entity);
        }
    }
}
