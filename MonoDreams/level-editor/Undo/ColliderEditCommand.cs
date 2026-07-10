#nullable enable
using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.LevelEditor.Component;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// A reversible edit to a collider's component-local spatial data — the command the gizmo emits
/// when a <b>collider proxy</b> (Wave 8b) is dragged. It is pure data: the bound game entity, the
/// <see cref="ProxyBindingKind"/>, and the before/after snapshot of the bound field
/// (<c>BoxColliderComponent.Bounds</c> or <c>ConvexColliderComponent.ModelVertices</c>);
/// <see cref="Apply"/> writes the after, <see cref="Revert"/> the before. The target is always
/// the <b>game</b> entity — never the transient proxy, whose despawn (deselect / mode exit) must
/// not dangle the history.
///
/// <para><b>Convex writes refresh the derived world data.</b> Detection normally refreshes
/// <c>WorldVertices</c>/<c>BroadPhaseAABB</c> every frame, but physics is frozen in Edit, so this
/// command calls <c>UpdateWorldVertices</c> itself after writing <c>ModelVertices</c> — per the
/// collision premise "BroadPhaseAABB must be refreshed when vertices change" (a stale AABB makes
/// the collider invisible to the broadphase, and a stale <c>WorldVertices</c> desyncs the debug
/// outline while editing). Values are copied into the collider's arrays (or freshly cloned when
/// lengths differ) so the command's snapshots are never aliased by later mutation — undo/redo
/// stays replayable.</para>
///
/// <para>Pushed per drag frame inside the history's coalescing transaction (like
/// <see cref="TransformEditCommand"/>), so a whole proxy drag is exactly one undo step; the
/// <c>ForBox</c>/<c>ForConvex</c> factories read the live component as the "before", making the
/// composite's revert chain walk back to the pre-drag shape in one undo. A dead or
/// collider-less target is a safe no-op.</para>
/// </summary>
public sealed class ColliderEditCommand : IEditorCommand
{
    private readonly Entity _entity;
    private readonly ProxyBindingKind _kind;
    private readonly Rectangle _beforeBounds, _afterBounds;
    private readonly Vector2[]? _beforeVertices, _afterVertices;

    private ColliderEditCommand(Entity entity, ProxyBindingKind kind,
        Rectangle beforeBounds, Rectangle afterBounds,
        Vector2[]? beforeVertices, Vector2[]? afterVertices)
    {
        _entity = entity;
        _kind = kind;
        _beforeBounds = beforeBounds;
        _afterBounds = afterBounds;
        _beforeVertices = beforeVertices;
        _afterVertices = afterVertices;
    }

    /// <summary>Builds a box-size edit from the entity's <b>current</b>
    /// <c>BoxColliderComponent.Size</c> as the "before" and <paramref name="afterBounds"/>'s SIZE as
    /// the "after". TODO(CE-C): the box-resize proxy retires — a collider entity is moved/resized via
    /// the ordinary gizmo/Inspector. Only the SIZE round-trips here (the box is centered on its
    /// entity now); the former <c>Bounds.Location</c> move is dropped.</summary>
    public static ColliderEditCommand ForBox(Entity entity, Rectangle afterBounds)
    {
        var size = entity.Get<BoxColliderComponent>().Size;
        var before = new Rectangle(0, 0, (int)MathF.Round(size.X), (int)MathF.Round(size.Y));
        return new ColliderEditCommand(entity, ProxyBindingKind.BoxColliderBounds,
            before, afterBounds, null, null);
    }

    /// <summary>Builds a convex-shape edit from the entity's <b>current</b>
    /// <c>ConvexColliderComponent.ModelVertices</c> (cloned) as the "before" and
    /// <paramref name="afterModelVertices"/> as the "after".</summary>
    public static ColliderEditCommand ForConvex(Entity entity, Vector2[] afterModelVertices)
    {
        var before = (Vector2[])entity.Get<ConvexColliderComponent>().ModelVertices.Clone();
        return new ColliderEditCommand(entity, ProxyBindingKind.ConvexColliderShape,
            Rectangle.Empty, Rectangle.Empty, before, afterModelVertices);
    }

    public void Apply(World world) => Write(_afterBounds, _afterVertices);
    public void Revert(World world) => Write(_beforeBounds, _beforeVertices);

    private void Write(Rectangle bounds, Vector2[]? vertices)
    {
        if (!_entity.IsAlive) return;

        switch (_kind)
        {
            case ProxyBindingKind.BoxColliderBounds:
                if (!_entity.Has<BoxColliderComponent>()) return;
                // TODO(CE-C): only Size survives — the box is centered on its entity's transform.
                _entity.Get<BoxColliderComponent>().Size = new Vector2(bounds.Width, bounds.Height);
                break;

            case ProxyBindingKind.ConvexColliderShape:
                if (vertices == null || !_entity.Has<ConvexColliderComponent>()) return;
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
                break;
        }
    }
}
