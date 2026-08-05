using System;
using DefaultEcs;
using MonoDreams.Component.Draw;

namespace MonoDreams.System.Draw;

/// <summary>
/// The reused, grow-only scratch buffer a <c>MasterRenderSystem</c> pass rebuilds and sorts every
/// frame to put its draw elements in painter's order (back-to-front). This is the allocation-free
/// replacement for the LINQ chain the renderer used to run per frame, per pass
/// (<c>GetEntities().ToArray() → Select((entity, index)) → Where(mesh valid) → OrderBy(LayerDepth)
/// → ThenBy(index) → ToList()</c>), which allocated an entity array, an enumerable chain and a
/// <c>List</c> on every single frame — thousands of streamed terrain tiles' worth of garbage in a
/// busy pass.
/// <para>
/// <b>Ordering equivalence.</b> <see cref="Sort"/> produces the <em>same</em> order the LINQ chain
/// did. <c>OrderBy(...).ThenBy(...)</c> is a documented <em>stable</em> sort, so equal-depth
/// elements came out in draw-set order; the in-place span sort is introsort and is <em>not</em>
/// stable, so the element's draw-set position is captured in <c>index</c> at rebuild time and
/// compared explicitly as the tiebreaker. Depth first, then index, is exactly
/// <c>OrderBy(depth).ThenBy(index)</c> over a set order — with no per-frame allocation.
/// </para>
/// <para>
/// <b>Why a class and not just fields on the renderer.</b> <c>MasterRenderSystem.Update</c> needs a
/// live <c>GraphicsDevice</c>, so the rebuild+sort could not be asserted on headlessly if it were
/// inlined there. Owning it here lets the unit tests drive the <em>same</em> code the renderer runs
/// (the <c>SpriteBatchFlush</c> precedent), so an ordering or allocation regression inside the
/// renderer's frame path shows up in a test rather than only on a profiler.
/// </para>
/// </summary>
internal sealed class DrawSortBuffer
{
    // Small enough to be free for a UI/HUD pass, big enough that a typical pass grows at most a
    // couple of times before settling at its high-water mark.
    private const int InitialCapacity = 64;

    private (Entity entity, int index, DrawComponent dc)[] _items =
        new (Entity, int, DrawComponent)[InitialCapacity];

    private int _count;

    /// <summary>
    /// The backing array. Only the first <see cref="Count"/> entries are live — the buffer is
    /// grow-only, so anything past <see cref="Count"/> is leftover scratch from an earlier, larger
    /// frame. Callers must iterate <c>0..Count</c>, never <c>Items.Length</c>.
    /// </summary>
    public (Entity entity, int index, DrawComponent dc)[] Items => _items;

    /// <summary>Number of live entries in <see cref="Items"/> after the last <see cref="Rebuild"/>.</summary>
    public int Count => _count;

    /// <summary>
    /// Refills the buffer in place from a pass's draw set, keeping every element except a mesh with
    /// no valid mesh data (<c>Type == Mesh &amp;&amp; !HasValidMesh</c> — nothing to submit), and
    /// recording each kept element's position in <paramref name="entities"/> as its stable-sort
    /// tiebreaker. Grows the buffer only when the draw set outgrows it; a steady-state pass
    /// therefore allocates nothing here.
    /// </summary>
    public void Rebuild(ReadOnlySpan<Entity> entities)
    {
        var previousCount = _count;

        if (_items.Length < entities.Length)
            _items = new (Entity, int, DrawComponent)[Math.Max(entities.Length, _items.Length * 2)];

        _count = 0;
        for (var i = 0; i < entities.Length; i++)
        {
            var dc = entities[i].Get<DrawComponent>();
            // A Mesh element with no vertices/indices has nothing to draw; the old LINQ chain
            // filtered it out with .Where(x => x.dc.Type != Mesh || x.dc.HasValidMesh).
            if (dc.Type == DrawElementType.Mesh && !dc.HasValidMesh) continue;
            _items[_count++] = (entities[i], i, dc);
        }

        // Release the tail's stale entries when this frame is smaller than the last: the buffer is
        // grow-only, so a leftover tuple would otherwise pin a disposed entity's DrawComponent —
        // and with it a texture or a mesh's vertex/index arrays — for the pass's lifetime.
        // Allocation-free, and only touched on a shrink.
        if (_count < previousCount)
            Array.Clear(_items, _count, previousCount - _count);
    }

    /// <summary>
    /// Sorts the live entries into painter's order in place: shallowest <c>LayerDepth</c> first, ties
    /// broken by draw-set order (see the ordering-equivalence note on the class). Allocation-free.
    /// <para>
    /// <b>The comparer must be a cached <see cref="Comparison{T}"/> delegate</b>, not an
    /// <c>IComparer&lt;T&gt;</c> — every <c>IComparer</c> route re-materializes a delegate per call,
    /// which is precisely the per-frame garbage this buffer exists to remove. Measured on .NET 8
    /// (500 sorts of a 5000-element pass, <c>GC.GetAllocatedBytesForCurrentThread</c>):
    /// <c>Array.Sort(array, index, length, classComparer)</c> and
    /// <c>span.Sort(classComparer)</c> allocate <b>64 B per call</b> (the sort helper takes
    /// <c>comparer.Compare</c>, so a fresh <c>Comparison&lt;T&gt;</c> is bound each time); a
    /// <c>struct</c> comparer through <c>span.Sort&lt;T, TComparer&gt;</c> is <em>worse</em> at
    /// <b>88 B per call</b> (the struct is boxed to bind that delegate). Only a pre-bound
    /// <c>Comparison&lt;T&gt;</c> handed to <c>span.Sort</c> allocates <b>0</b>. The allocation test
    /// in <c>DrawSortTests</c> pins this down — if it starts failing, this is the line that changed.
    /// </para>
    /// </summary>
    public void Sort()
    {
        if (_count > 1) _items.AsSpan(0, _count).Sort(DrawOrder);
    }

    /// <summary>
    /// Painter's order: shallowest depth first, ties broken by the draw set's own order so
    /// same-depth elements keep insertion order — what the LINQ
    /// <c>OrderBy(depth).ThenBy(index)</c> stable sort produced. The index comparison is
    /// load-bearing precisely because the underlying introsort is unstable. Bound once into a static
    /// delegate so sorting allocates nothing (see <see cref="Sort"/>).
    /// </summary>
    private static readonly Comparison<(Entity entity, int index, DrawComponent dc)> DrawOrder =
        static (a, b) =>
        {
            var depth = a.dc.LayerDepth.CompareTo(b.dc.LayerDepth);
            return depth != 0 ? depth : a.index.CompareTo(b.index);
        };
}
