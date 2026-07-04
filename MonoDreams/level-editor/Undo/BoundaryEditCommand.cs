#nullable enable
using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.LevelEditor.Component;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// A reversible edit to a <see cref="BoundaryComponent"/>'s polyline — the command emitted when a
/// boundary vertex proxy (<see cref="ProxyBindingKind.BoundaryVertex"/>) is dragged, or a vertex is
/// added / deleted. Pure data: the boundary entity plus the before/after snapshot of
/// <c>Points</c> (the <c>Thickness</c> is preserved). <see cref="Apply"/> writes the after,
/// <see cref="Revert"/> the before — each through <c>entity.Set(new BoundaryComponent(...))</c>,
/// which fires the component-changed event <c>BoundaryBakeSystem</c> subscribes to, so the segment
/// colliders re-bake to match whichever state the history lands on (undo/redo re-bakes for free).
///
/// <para>Snapshots are cloned so later mutation never aliases the recorded arrays; a dead or
/// boundary-less target is a safe no-op.</para>
/// </summary>
public sealed class BoundaryEditCommand : IEditorCommand
{
    private readonly Entity _entity;
    private readonly Vector2[] _before;
    private readonly Vector2[] _after;
    private readonly float _thickness;

    private BoundaryEditCommand(Entity entity, Vector2[] before, Vector2[] after, float thickness)
    {
        _entity = entity;
        _before = before;
        _after = after;
        _thickness = thickness;
    }

    /// <summary>Builds a boundary edit from the entity's <b>current</b> <c>Points</c> (cloned) as the
    /// "before" and <paramref name="afterPoints"/> as the "after".</summary>
    public static BoundaryEditCommand For(Entity entity, Vector2[] afterPoints)
    {
        var boundary = entity.Get<BoundaryComponent>();
        return new BoundaryEditCommand(entity,
            (Vector2[])(boundary.Points ?? Array.Empty<Vector2>()).Clone(),
            (Vector2[])afterPoints.Clone(),
            boundary.Thickness);
    }

    public void Apply(World world) => Write(_after);
    public void Revert(World world) => Write(_before);

    private void Write(Vector2[] points)
    {
        if (!_entity.IsAlive || !_entity.Has<BoundaryComponent>()) return;
        // Set (not mutate) so the component-changed event fires → the bake re-runs.
        _entity.Set(new BoundaryComponent((Vector2[])points.Clone(), _thickness));
    }
}
