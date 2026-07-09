#nullable enable
using System;
using DefaultEcs;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// A reversible <b>add</b> of a default-constructed component to an entity — the DevTools-grade
/// Inspector's "+ Add component" (PF-A §3). Pure data: the target entity, the component
/// <see cref="Type"/>, and the boxed default instance to attach (built once at construction from the
/// per-type default-initializer table — see <c>InspectorComponentDefaults</c> — so <see cref="Apply"/>
/// and a later redo attach the same value). <see cref="Apply"/> sets the component (if absent);
/// <see cref="Revert"/> removes it. A dead target is a safe no-op; adding an already-present component
/// is a no-op (its later edits are separate commands).
///
/// <para><b>Component pairing (pre-mortem #6).</b> Adding a <c>SpriteInfoComponent</c> also adds the
/// paired transient <c>DrawComponent</c> (else the sprite never enters the prep query and renders
/// blank); the undo removes both. Enforced through <see cref="ComponentPairing"/>.</para>
/// </summary>
public sealed class AddComponentCommand : IEditorCommand
{
    private readonly Entity _entity;
    private readonly Type _componentType;
    private readonly object _value;
    private readonly bool _pairsDraw;

    public AddComponentCommand(Entity entity, Type componentType, object value)
    {
        _entity = entity;
        _componentType = componentType ?? throw new ArgumentNullException(nameof(componentType));
        _value = value ?? throw new ArgumentNullException(nameof(value));
        _pairsDraw = ComponentPairing.PairsDrawComponent(componentType);
    }

    public void Apply(World world)
    {
        if (!_entity.IsAlive || EntityComponentReflection.Has(_entity, _componentType)) return;
        EntityComponentReflection.Set(_entity, _componentType, _value);
        if (_pairsDraw) ComponentPairing.EnsureSpriteDraw(_entity);
    }

    public void Revert(World world)
    {
        if (!_entity.IsAlive) return;
        if (_pairsDraw) ComponentPairing.RemoveSpriteDraw(_entity);
        if (EntityComponentReflection.Has(_entity, _componentType))
            EntityComponentReflection.Remove(_entity, _componentType);
    }
}
