#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using DefaultEcs;
using DefaultEcs.Serialization;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// The component-serializer registry: maps a component <see cref="Type"/> to a
/// <see cref="ComponentSerializer"/> (write/read pair) keyed by a stable string. It is the
/// service that turns a live entity's <b>registered</b> components into a
/// <see cref="SceneEntityData"/> and back, so a scene round-trips by serializing components —
/// never by re-running factories.
///
/// <para><b>Opt-in per type.</b> Only registered component types are serialized. Engine tags and
/// transient state (<c>VisibleComponent</c>, cursor state, <c>DrawComponent</c>) are deliberately
/// NOT registered. The engine ships serializers for its serializable components via
/// <see cref="EngineComponentSerializers.RegisterEngineComponents"/>; game code registers
/// serializers for its own components (e.g. <c>PlayerState</c>) through <c>Register</c>.</para>
///
/// <para><b>Loud on the unexpected.</b> Serializing an entity that carries a component type with
/// no registered serializer skips that component and logs a <see cref="Logger.Warning"/> — the
/// component is silently dropped from the file otherwise, which is the missing-entity class of bug.
/// (Registered structural tags like <c>ChildOfComponent</c> are an exception: the parent link is
/// captured as <see cref="SceneEntityData.Parent"/> by the caller, so its serializer is registered
/// to mark it "known" without writing a component body.)</para>
///
/// This is infrastructure, not a component (ECS purity): the registry holds the read/write
/// behaviour and never lives on an entity. Registration happens once at module/screen init, so the
/// in-scope component set is explicit and greppable.
/// </summary>
public sealed class ComponentSerializerRegistry
{
    private readonly Dictionary<Type, ComponentSerializer> _byType = new();
    private readonly Dictionary<string, ComponentSerializer> _byKey = new();

    /// <summary>Component types whose presence on an entity is known but intentionally not written
    /// as a component body (the structural parent link is captured as <see cref="SceneEntityData.Parent"/>).
    /// Keeping them out of the unregistered-warning path avoids a spurious warning for every parented entity.</summary>
    private readonly HashSet<Type> _structuralParentTypes = new();

    /// <summary>Registers a serializer for <paramref name="componentType"/> under <paramref name="key"/>.</summary>
    /// <exception cref="ArgumentException">A serializer is already registered for the same type or key.</exception>
    public void Register(string key, Type componentType, Func<Entity, JsonElement> write, Action<Entity, JsonElement> read)
        => Register(new ComponentSerializer(key, componentType, write, read));

    /// <summary>Registers a pre-built <see cref="ComponentSerializer"/>.</summary>
    /// <exception cref="ArgumentException">A serializer is already registered for the same type or key.</exception>
    public void Register(ComponentSerializer serializer)
    {
        if (serializer == null) throw new ArgumentNullException(nameof(serializer));
        if (_byType.ContainsKey(serializer.ComponentType))
            throw new ArgumentException($"A serializer is already registered for component type '{serializer.ComponentType.FullName}'.");
        if (_byKey.ContainsKey(serializer.Key))
            throw new ArgumentException($"A serializer is already registered for key '{serializer.Key}'.");

        _byType[serializer.ComponentType] = serializer;
        _byKey[serializer.Key] = serializer;
    }

    /// <summary>
    /// Marks <typeparamref name="T"/> as the structural parent-link component: known to the registry
    /// (so it never triggers the unregistered-component warning) but not written as a component body —
    /// the link is captured as <see cref="SceneEntityData.Parent"/> by <see cref="SerializeEntity"/>.
    /// </summary>
    public void RegisterStructuralParentLink<T>() => _structuralParentTypes.Add(typeof(T));

    /// <summary>True if a serializer is registered for <paramref name="componentType"/>.</summary>
    public bool IsRegistered(Type componentType) => _byType.ContainsKey(componentType);

    /// <summary>Looks up a serializer by its stable key, or <c>null</c> if none.</summary>
    public ComponentSerializer? GetByKey(string key) => _byKey.GetValueOrDefault(key);

    /// <summary>
    /// Serializes every registered component on <paramref name="entity"/> into a
    /// <see cref="SceneEntityData"/>. Components with no registered serializer are skipped with a
    /// <see cref="Logger.Warning"/>. The structural parent link is NOT captured here (the caller
    /// owns the entity→index mapping); the caller sets <see cref="SceneEntityData.Parent"/> after
    /// all entities are serialized.
    /// </summary>
    public SceneEntityData SerializeEntity(Entity entity)
    {
        var data = new SceneEntityData();
        // ReadAllComponents enumerates every component actually on the entity — the only way to
        // detect a component type with no registered serializer and warn about it.
        var discoverer = new ComponentDiscoverer(this, entity, data);
        entity.ReadAllComponents(discoverer);
        return data;
    }

    /// <summary>
    /// Reconstructs the components in <paramref name="data"/> onto <paramref name="entity"/>.
    /// A component key with no registered serializer throws (loud failure on load: the file
    /// references a component the runtime can't reconstruct, which would silently lose data).
    /// The structural parent link in <see cref="SceneEntityData.Parent"/> is NOT wired here — the
    /// caller wires it after all entities exist (two-pass create-then-link).
    /// </summary>
    public void DeserializeEntity(Entity entity, SceneEntityData data)
    {
        foreach (var (key, element) in data.Components)
        {
            var serializer = GetByKey(key);
            if (serializer == null)
                throw new InvalidOperationException(
                    $"Scene references component key '{key}' but no serializer is registered for it. " +
                    "Register it before loading the scene (engine components register via " +
                    "EngineComponentSerializers.RegisterEngineComponents; game components via registry.Register).");
            serializer.Read(entity, element);
        }
    }

    /// <summary>
    /// The <see cref="IComponentReader"/> that walks an entity's components: writes each registered
    /// one through its serializer, warns on each unregistered one, and ignores the structural
    /// parent-link type (captured separately).
    /// </summary>
    private sealed class ComponentDiscoverer(ComponentSerializerRegistry registry, Entity owner, SceneEntityData data)
        : IComponentReader
    {
        public void OnRead<T>(in T component, in Entity componentOwner)
        {
            var type = typeof(T);

            if (registry._structuralParentTypes.Contains(type))
                return; // parent link captured as SceneEntityData.Parent, not as a component body

            if (registry._byType.TryGetValue(type, out var serializer))
            {
                data.Components[serializer.Key] = serializer.Write(owner);
                return;
            }

            // Unregistered component: skip it, but loudly — a silently dropped component is the
            // missing-entity class of bug (see level-loading "Unregistered factory identifiers …").
            Logger.Warning(
                $"[level-editor] No serializer registered for component '{type.FullName}'; " +
                "skipping it when writing the scene. Register one via registry.Register if it must persist.");
        }
    }
}
