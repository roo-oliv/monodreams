#nullable enable
using System;
using System.Reflection;
using DefaultEcs;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// A reversible edit to ONE public field/property of a component on an entity — the DevTools-grade
/// Inspector's value edit (PF-A §3). Pure data: the target entity, the component <see cref="Type"/>,
/// the member name, and the <b>before</b>/<b>after</b> boxed member values; <see cref="Apply"/> writes
/// the after, <see cref="Revert"/> the before, both through reflection.
///
/// <para><b>Struct write-back (pre-mortem #5).</b> Whether the component is a struct
/// (<c>SpriteInfoComponent</c>) or a class (<c>TransformComponent</c>), the command does
/// <b>get → mutate the boxed member → <c>Set</c> the component back</b> via
/// <see cref="EntityComponentReflection"/>: for a struct the box is a copy, so the write-back is what
/// makes the edit stick; for a class it re-fires the component-changed notification so prep/hierarchy
/// systems re-derive. A dead target, a missing component, or a member that vanished is a safe no-op.</para>
/// </summary>
public sealed class MemberEditCommand : IEditorCommand
{
    private readonly Entity _entity;
    private readonly Type _componentType;
    private readonly string _memberName;
    private readonly object? _before;
    private readonly object? _after;

    public MemberEditCommand(Entity entity, Type componentType, string memberName, object? before, object? after)
    {
        _entity = entity;
        _componentType = componentType ?? throw new ArgumentNullException(nameof(componentType));
        _memberName = memberName ?? throw new ArgumentNullException(nameof(memberName));
        _before = before;
        _after = after;
    }

    /// <summary>Builds a command reading the member's <b>current</b> value off the live entity as the
    /// "before" and taking <paramref name="after"/> as the new value. Returns null when the entity is
    /// dead, lacks the component, or has no such writable member.</summary>
    public static MemberEditCommand? FromCurrent(Entity entity, Type componentType, string memberName, object? after)
    {
        if (!entity.IsAlive || !EntityComponentReflection.Has(entity, componentType)) return null;
        var boxed = EntityComponentReflection.Get(entity, componentType);
        if (boxed == null) return null;
        var member = ResolveMember(componentType, memberName);
        if (member == null || !IsWritable(member)) return null;
        var before = ReadMember(member, boxed);
        return new MemberEditCommand(entity, componentType, memberName, before, after);
    }

    public void Apply(World world) => Write(_after);
    public void Revert(World world) => Write(_before);

    private void Write(object? value)
    {
        if (!_entity.IsAlive || !EntityComponentReflection.Has(_entity, _componentType)) return;
        var boxed = EntityComponentReflection.Get(_entity, _componentType);
        if (boxed == null) return;
        var member = ResolveMember(_componentType, _memberName);
        if (member == null || !IsWritable(member)) return;
        switch (member)
        {
            case FieldInfo f: f.SetValue(boxed, value); break;
            case PropertyInfo p: p.SetValue(boxed, value); break;
            default: return;
        }
        // Write the (possibly-copied) box back — the struct-write-back half of pre-mortem #5.
        EntityComponentReflection.Set(_entity, _componentType, boxed);
    }

    /// <summary>Reads the CURRENT boxed value of a component member off a live entity — the current value
    /// a bool toggle / enum cycle computes its "after" from. Returns false when the entity is dead, lacks
    /// the component, or has no such member.</summary>
    public static bool TryReadMember(Entity entity, Type componentType, string memberName, out object? value)
    {
        value = null;
        if (!entity.IsAlive || !EntityComponentReflection.Has(entity, componentType)) return false;
        var boxed = EntityComponentReflection.Get(entity, componentType);
        if (boxed == null) return false;
        var member = ResolveMember(componentType, memberName);
        if (member == null) return false;
        value = ReadMember(member, boxed);
        return true;
    }

    /// <summary>The public instance field or property named <paramref name="name"/> on
    /// <paramref name="type"/> (field wins on the unlikely name clash), or null.</summary>
    public static MemberInfo? ResolveMember(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        return (MemberInfo?)type.GetField(name, flags) ?? type.GetProperty(name, flags);
    }

    /// <summary>Whether <paramref name="member"/> is a settable field or a property with a setter.</summary>
    public static bool IsWritable(MemberInfo member) => member switch
    {
        FieldInfo f => !f.IsInitOnly && !f.IsLiteral,
        PropertyInfo p => p.CanWrite,
        _ => false,
    };

    private static object? ReadMember(MemberInfo member, object boxed) => member switch
    {
        FieldInfo f => f.GetValue(boxed),
        PropertyInfo p => p.GetValue(boxed),
        _ => null,
    };
}
