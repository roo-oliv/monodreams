#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using DefaultEcs;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// By-<see cref="Type"/> access to DefaultEcs's generic <see cref="Entity"/> component API
/// (<c>Get&lt;T&gt;</c> / <c>Set&lt;T&gt;</c> / <c>Has&lt;T&gt;</c> / <c>Remove&lt;T&gt;</c>) for the
/// editable Inspector, which operates on component types known only at runtime (PF-A). The generic
/// methods are resolved once by reflection and memoized per component type; each call boxes the
/// <see cref="Entity"/> (a value type whose methods route through the <c>World</c>, never mutating the
/// struct's own fields — so the boxed copy addresses the same storage). Not a hot path: one call per
/// designer edit / add / remove.
///
/// <para><b>Struct write-back (pre-mortem #5).</b> <see cref="Get"/> boxes a <b>copy</b> of a struct
/// component; a caller that mutates a member of that box <b>must</b> <see cref="Set"/> it back or the
/// edit silently vanishes. For a class component the box is the live reference, so the mutation lands
/// directly, but a <see cref="Set"/> is still correct (it re-fires the component-changed notification
/// so prep systems re-derive). Every command therefore does get → mutate → <see cref="Set"/>.</para>
/// </summary>
internal static class EntityComponentReflection
{
    private static readonly MethodInfo GetDef = FindGeneric("Get", parameters: 0);
    private static readonly MethodInfo SetDef = FindGeneric("Set", parameters: 1);
    private static readonly MethodInfo HasDef = FindGeneric("Has", parameters: 0);
    private static readonly MethodInfo RemoveDef = FindGeneric("Remove", parameters: 0);

    private static readonly Dictionary<Type, MethodInfo> Getters = new();
    private static readonly Dictionary<Type, MethodInfo> Setters = new();
    private static readonly Dictionary<Type, MethodInfo> Hassers = new();
    private static readonly Dictionary<Type, MethodInfo> Removers = new();

    /// <summary>Whether <paramref name="entity"/> currently carries a component of <paramref name="type"/>.</summary>
    public static bool Has(Entity entity, Type type) =>
        (bool)For(Hassers, HasDef, type).Invoke(entity, null)!;

    /// <summary>The component of <paramref name="type"/> on <paramref name="entity"/>, boxed (a struct
    /// yields a copy; a class yields the live reference). Assumes <see cref="Has"/> is true.</summary>
    public static object? Get(Entity entity, Type type) =>
        For(Getters, GetDef, type).Invoke(entity, null);

    /// <summary>Sets/updates the component of <paramref name="type"/> on <paramref name="entity"/> from
    /// the boxed <paramref name="value"/> — the write-back half of a struct edit + the add path.</summary>
    public static void Set(Entity entity, Type type, object value) =>
        For(Setters, SetDef, type).Invoke(entity, new[] { value });

    /// <summary>Removes the component of <paramref name="type"/> from <paramref name="entity"/> (a no-op
    /// on the DefaultEcs side when absent — callers guard with <see cref="Has"/> anyway).</summary>
    public static void Remove(Entity entity, Type type) =>
        For(Removers, RemoveDef, type).Invoke(entity, null);

    private static MethodInfo For(Dictionary<Type, MethodInfo> cache, MethodInfo def, Type type)
    {
        if (cache.TryGetValue(type, out var m)) return m;
        m = def.MakeGenericMethod(type);
        cache[type] = m;
        return m;
    }

    /// <summary>Finds the single-generic-argument <see cref="Entity"/> method named <paramref name="name"/>
    /// with <paramref name="parameters"/> parameters (robust to <c>in</c>/<c>ref</c> modifiers on the
    /// argument — matched by count, not by exact signature).</summary>
    private static MethodInfo FindGeneric(string name, int parameters)
    {
        foreach (var m in typeof(Entity).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            if (m.Name == name && m.IsGenericMethodDefinition
                && m.GetGenericArguments().Length == 1
                && m.GetParameters().Length == parameters)
                return m;
        throw new InvalidOperationException(
            $"DefaultEcs Entity.{name}<T> with {parameters} parameter(s) was not found by reflection.");
    }
}
