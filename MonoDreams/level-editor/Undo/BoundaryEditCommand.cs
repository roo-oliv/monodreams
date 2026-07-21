#nullable enable
using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.LevelEditor.Component;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// A reversible edit to a <see cref="BoundaryComponent"/> — the command emitted when a boundary
/// vertex proxy (<see cref="ProxyBindingKind.BoundaryVertex"/>) is dragged, a vertex is
/// added / deleted, or the thickness handle (<see cref="ProxyBindingKind.BoundaryThickness"/>,
/// Slice 4) is dragged. Pure data: the boundary entity plus the before/after snapshot of both
/// <c>Points</c> and <c>Thickness</c>. <see cref="Apply"/> writes the after, <see cref="Revert"/>
/// the before — each through <c>entity.Set(new BoundaryComponent(...))</c>, which fires the
/// component-changed event <c>BoundaryBakeSystem</c> subscribes to, so the segment colliders
/// re-bake to match whichever state the history lands on (undo/redo re-bakes for free).
///
/// <para>Snapshots are cloned so later mutation never aliases the recorded arrays; a dead or
/// boundary-less target is a safe no-op.</para>
/// </summary>
public sealed class BoundaryEditCommand : IEditorCommand
{
    private readonly Entity _entity;
    private readonly Vector2[] _before;
    private readonly Vector2[] _after;
    private readonly float _beforeThickness;
    private readonly float _afterThickness;

    private BoundaryEditCommand(Entity entity, Vector2[] before, Vector2[] after,
        float beforeThickness, float afterThickness)
    {
        _entity = entity;
        _before = before;
        _after = after;
        _beforeThickness = beforeThickness;
        _afterThickness = afterThickness;
    }

    /// <summary>Builds a polyline edit from the entity's <b>current</b> <c>Points</c> (cloned) as the
    /// "before" and <paramref name="afterPoints"/> as the "after" — the thickness is unchanged.</summary>
    public static BoundaryEditCommand For(Entity entity, Vector2[] afterPoints)
    {
        var boundary = entity.Get<BoundaryComponent>();
        var points = (Vector2[])(boundary.Points ?? Array.Empty<Vector2>()).Clone();
        return new BoundaryEditCommand(entity,
            points, (Vector2[])afterPoints.Clone(),
            boundary.Thickness, boundary.Thickness);
    }

    /// <summary>Builds a thickness edit (island-authoring Slice 4): the <c>Points</c> are unchanged,
    /// the thickness goes from the entity's current value to <paramref name="afterThickness"/>.</summary>
    public static BoundaryEditCommand ForThickness(Entity entity, float afterThickness)
    {
        var boundary = entity.Get<BoundaryComponent>();
        var points = (Vector2[])(boundary.Points ?? Array.Empty<Vector2>()).Clone();
        return new BoundaryEditCommand(entity,
            points, (Vector2[])points.Clone(),
            boundary.Thickness, afterThickness);
    }

    public void Apply(World world) => Write(_after, _afterThickness);
    public void Revert(World world) => Write(_before, _beforeThickness);

    private void Write(Vector2[] points, float thickness)
    {
        if (!_entity.IsAlive || !_entity.Has<BoundaryComponent>()) return;
        // Set (not mutate) so the component-changed event fires → the bake re-runs.
        _entity.Set(new BoundaryComponent((Vector2[])points.Clone(), thickness));
    }
}
