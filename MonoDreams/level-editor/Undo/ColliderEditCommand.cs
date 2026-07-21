#nullable enable
using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// A reversible edit to a convex collider's <c>ModelVertices</c> — the command the editor emits for
/// the vertex-level edits that stay proxy-driven: a vertex-grip drag, Add vertex, Delete vertex.
/// (Whole-shape move/scale now goes through <see cref="TransformEditCommand"/> on the collider
/// ENTITY — colliders are entities; the box-resize path is retired.) It is pure data: the collider
/// entity plus the before/after snapshot of <c>ModelVertices</c>; <see cref="Apply"/> writes the
/// after, <see cref="Revert"/> the before.
///
/// <para><b>Writes refresh the derived world data.</b> Detection normally refreshes
/// <c>WorldVertices</c>/<c>BroadPhaseAABB</c> every frame, but physics is frozen in Edit, so this
/// command calls <c>UpdateWorldVertices</c> itself after writing <c>ModelVertices</c> — per the
/// collision premise "BroadPhaseAABB must be refreshed when vertices change" (a stale AABB makes
/// the collider invisible to the broadphase, and a stale <c>WorldVertices</c> desyncs the debug
/// outline while editing). Values are copied into the collider's arrays (or freshly cloned when
/// lengths differ) so the command's snapshots are never aliased by later mutation — undo/redo
/// stays replayable.</para>
///
/// <para>Pushed per drag frame inside the history's coalescing transaction (like
/// <see cref="TransformEditCommand"/>), so a whole vertex drag is exactly one undo step; the
/// <c>ForConvex</c> factory reads the live component as the "before", making the composite's revert
/// chain walk back to the pre-drag shape in one undo. A dead or collider-less target is a safe
/// no-op.</para>
/// </summary>
public sealed class ColliderEditCommand : IEditorCommand
{
    private readonly Entity _entity;
    private readonly Vector2[] _beforeVertices, _afterVertices;

    private ColliderEditCommand(Entity entity, Vector2[] beforeVertices, Vector2[] afterVertices)
    {
        _entity = entity;
        _beforeVertices = beforeVertices;
        _afterVertices = afterVertices;
    }

    /// <summary>Builds a convex-shape edit from the collider entity's <b>current</b>
    /// <c>ConvexColliderComponent.ModelVertices</c> (cloned) as the "before" and
    /// <paramref name="afterModelVertices"/> as the "after".</summary>
    public static ColliderEditCommand ForConvex(Entity entity, Vector2[] afterModelVertices)
    {
        var before = (Vector2[])entity.Get<ConvexColliderComponent>().ModelVertices.Clone();
        return new ColliderEditCommand(entity, before, afterModelVertices);
    }

    public void Apply(World world) => Write(_afterVertices);
    public void Revert(World world) => Write(_beforeVertices);

    private void Write(Vector2[] vertices)
    {
        if (!_entity.IsAlive || vertices == null || !_entity.Has<ConvexColliderComponent>()) return;

        var collider = _entity.Get<ConvexColliderComponent>();
        if (collider.ModelVertices != null && collider.ModelVertices.Length == vertices.Length)
        {
            // Copy values in — never alias the snapshot arrays into the live component.
            Array.Copy(vertices, collider.ModelVertices, vertices.Length);
        }
        else
        {
            collider.ModelVertices = (Vector2[])vertices.Clone();
            collider.WorldVertices = new Vector2[vertices.Length];
        }
        // Physics is frozen in Edit, so nothing else refreshes the derived world data —
        // do it here (collision premise: refresh BroadPhaseAABB when vertices change).
        if (_entity.Has<TransformComponent>())
            collider.UpdateWorldVertices(_entity.Get<TransformComponent>());
    }
}
