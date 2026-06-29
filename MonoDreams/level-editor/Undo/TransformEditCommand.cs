#nullable enable
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// A reversible edit to an entity's <see cref="TransformComponent"/> — the command the gizmo (Wave
/// 4b) emits on a drag (move / rotate / scale). It is pure data: the target entity plus the before
/// and after (position, rotation, scale, origin); <see cref="Apply"/> writes the after, <see cref="Revert"/>
/// writes the before. Defined here in Wave 4a; 4b wires the gizmo to construct + push it through the
/// coalescing API so a whole drag is one undo step.
///
/// <para>The transform is a reference type, so the command mutates the live component's fields rather
/// than replacing the instance — this preserves the <c>TransformComponent.Parent</c> matrix link and
/// any other state, and marks the transform dirty (each setter calls <c>SetDirty</c>) so
/// <c>HierarchySystem</c> re-propagates to children. A no-longer-alive target is a safe no-op.</para>
/// </summary>
public sealed class TransformEditCommand : IEditorCommand
{
    private readonly Entity _entity;
    private readonly Vector2 _beforePosition, _afterPosition;
    private readonly float _beforeRotation, _afterRotation;
    private readonly Vector2 _beforeScale, _afterScale;
    private readonly Vector2 _beforeOrigin, _afterOrigin;

    public TransformEditCommand(
        Entity entity,
        Vector2 beforePosition, Vector2 afterPosition,
        float beforeRotation, float afterRotation,
        Vector2 beforeScale, Vector2 afterScale,
        Vector2 beforeOrigin, Vector2 afterOrigin)
    {
        _entity = entity;
        _beforePosition = beforePosition; _afterPosition = afterPosition;
        _beforeRotation = beforeRotation; _afterRotation = afterRotation;
        _beforeScale = beforeScale; _afterScale = afterScale;
        _beforeOrigin = beforeOrigin; _afterOrigin = afterOrigin;
    }

    /// <summary>Builds a command from the entity's <b>current</b> transform as the "before" and the
    /// supplied target as the "after". Convenience for the gizmo drag-end path.</summary>
    public static TransformEditCommand FromCurrent(
        Entity entity, Vector2 afterPosition, float afterRotation, Vector2 afterScale, Vector2 afterOrigin)
    {
        var t = entity.Get<TransformComponent>();
        return new TransformEditCommand(
            entity,
            t.Position, afterPosition,
            t.Rotation, afterRotation,
            t.Scale, afterScale,
            t.Origin, afterOrigin);
    }

    public void Apply(World world) => Write(_afterPosition, _afterRotation, _afterScale, _afterOrigin);
    public void Revert(World world) => Write(_beforePosition, _beforeRotation, _beforeScale, _beforeOrigin);

    private void Write(Vector2 position, float rotation, Vector2 scale, Vector2 origin)
    {
        if (!_entity.IsAlive || !_entity.Has<TransformComponent>()) return;
        var t = _entity.Get<TransformComponent>();
        t.Position = position;     // each setter marks dirty so HierarchySystem re-propagates to children
        t.Rotation = rotation;
        t.Scale = scale;
        t.Origin = origin;
    }
}
