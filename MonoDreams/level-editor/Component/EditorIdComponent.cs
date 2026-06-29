namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// A stable, monotonically-increasing integer the selection system assigns to every pickable
/// candidate the first time it sees it. It is the <b>selection-owned deterministic tiebreak</b>
/// for the picking front: when two overlapping sprites resolve to the exact same final
/// post-Y-sort <c>DrawComponent.LayerDepth</c>, the selection system picks the one with the
/// larger <see cref="Id"/> — i.e. the more recently created / first-seen-later entity — which is
/// stable and observable, unlike <c>MasterRenderSystem</c>'s private per-frame insertion index.
///
/// <para><b>Why a selection-owned key and not the renderer's index.</b> The render front sorts on
/// <c>OrderBy(LayerDepth).ThenBy(index)</c> where <c>index</c> is a transient enumeration position
/// rebuilt every frame inside <c>MasterRenderSystem</c> — it is private and not stable across
/// frames or queries. The selection must reproduce "topmost = what the renderer draws last" with a
/// key it can read itself; an explicit monotonic id does that deterministically. The id reflects
/// first-seen order (which, for an undisturbed scene, equals creation order).</para>
///
/// <para>Pure data — the counter that allocates ids lives in the selection system, not here.</para>
/// </summary>
public struct EditorIdComponent
{
    /// <summary>The stable per-entity id. Larger = seen later = wins an exact-depth tie (drawn last).</summary>
    public int Id;

    public EditorIdComponent(int id) => Id = id;
}
