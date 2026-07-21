#nullable enable
using System;
using DefaultEcs;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// A reversible <b>remove</b> of a component from an entity — the DevTools-grade Inspector's per-row
/// delete (PF-A §3). Pure data: the target entity, the component <see cref="Type"/>, and a boxed
/// <b>snapshot</b> of the component's value taken at construction (so undo restores it field-for-field).
/// <see cref="Apply"/> removes;
/// <see cref="Revert"/> re-sets the snapshot. A dead target is a safe no-op.
///
/// <para><b>Guardrails (pre-mortem #6).</b> Removing a <c>SpriteInfoComponent</c> also removes the
/// transient <c>DrawComponent</c>, and the undo restores BOTH (the pairing is re-derived, not
/// snapshotted — the <c>DrawComponent</c> is transient). <c>TransformComponent</c>-not-removable and
/// the structural-component exclusions are enforced UPSTREAM (the panel refuses to build the command),
/// so this command stays a pure mechanism.</para>
/// </summary>
public sealed class RemoveComponentCommand : IEditorCommand
{
    private readonly Entity _entity;
    private readonly Type _componentType;
    private readonly object? _snapshot;
    private readonly bool _pairsDraw;

    public RemoveComponentCommand(Entity entity, Type componentType, object? snapshot)
    {
        _entity = entity;
        _componentType = componentType ?? throw new ArgumentNullException(nameof(componentType));
        _snapshot = snapshot;
        _pairsDraw = ComponentPairing.PairsDrawComponent(componentType);
    }

    /// <summary>Builds the command, snapshotting the live component's value so undo restores it. Returns
    /// null when the entity is dead or lacks the component.</summary>
    public static RemoveComponentCommand? Create(Entity entity, Type componentType)
    {
        if (!entity.IsAlive || !EntityComponentReflection.Has(entity, componentType)) return null;
        var snapshot = EntityComponentReflection.Get(entity, componentType);
        return new RemoveComponentCommand(entity, componentType, snapshot);
    }

    public void Apply(World world)
    {
        if (!_entity.IsAlive) return;
        if (EntityComponentReflection.Has(_entity, _componentType))
            EntityComponentReflection.Remove(_entity, _componentType);
        if (_pairsDraw) ComponentPairing.RemoveSpriteDraw(_entity); // the transient DrawComponent goes too
    }

    public void Revert(World world)
    {
        if (!_entity.IsAlive || _snapshot == null) return;
        EntityComponentReflection.Set(_entity, _componentType, _snapshot);
        if (_pairsDraw) ComponentPairing.EnsureSpriteDraw(_entity); // restore BOTH (pairing re-derived)
    }
}
