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

    /// <summary>Component types whose presence on an entity is known but intentionally not written as a
    /// component body — they are captured as the entity entry's dedicated STRUCTURAL fields instead:
    /// <c>ChildOfComponent</c> as <see cref="SceneEntityData.Parent"/>, <c>SceneEntityIdComponent</c> as
    /// <see cref="SceneEntityData.Id"/>. Keeping them out of the unregistered-warning path avoids a
    /// spurious warning for every parented / id-stamped entity.</summary>
    private readonly HashSet<Type> _structurallyCapturedTypes = new();

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
    public void RegisterStructuralParentLink<T>() => MarkStructurallyCaptured<T>();

    /// <summary>
    /// Marks <typeparamref name="T"/> as a component captured by the writer as a dedicated structural
    /// field on the entity entry (e.g. <c>SceneEntityIdComponent</c> → <see cref="SceneEntityData.Id"/>),
    /// not as a body in <c>components{}</c>: known to the registry (so it never triggers the
    /// unregistered-component warning) and silently skipped by <see cref="SerializeEntity"/>.
    /// </summary>
    public void MarkStructurallyCaptured<T>() => _structurallyCapturedTypes.Add(typeof(T));

    /// <summary>True if a serializer is registered for <paramref name="componentType"/>.</summary>
    public bool IsRegistered(Type componentType) => _byType.ContainsKey(componentType);

    /// <summary>
    /// Whether <paramref name="componentType"/> is captured as a dedicated STRUCTURAL field on the entity
    /// entry (the parent link / the stable id / prefab markers) rather than a component body — the set
    /// marked via <see cref="RegisterStructuralParentLink{T}"/> / <see cref="MarkStructurallyCaptured{T}"/>.
    /// The editable Inspector uses this to exclude structural types from the "+ Add component" candidates
    /// (they are never designer-editable data).
    /// </summary>
    public bool IsStructural(Type componentType) => _structurallyCapturedTypes.Contains(componentType);

    /// <summary>
    /// Every registered component as a <c>(stable key, CLR type)</c> pair (engine + game) — the honest
    /// "what can this scene persist" set the editable Inspector's "+ Add component" candidate list is
    /// derived from (registered MINUS present-on-entity MINUS structural/never-addable; see
    /// <c>InspectorAddCandidates</c>). A read snapshot; the registry stays the sole owner.
    /// </summary>
    public IReadOnlyList<(string Key, Type Type)> RegisteredComponents()
    {
        var list = new List<(string, Type)>(_byKey.Count);
        foreach (var (key, serializer) in _byKey)
            list.Add((key, serializer.ComponentType));
        return list;
    }

    /// <summary>Looks up a serializer by its stable key, or <c>null</c> if none.</summary>
    public ComponentSerializer? GetByKey(string key) => _byKey.GetValueOrDefault(key);

    /// <summary>The CLR component type registered under <paramref name="key"/>, or <c>null</c> if none
    /// (the editable Inspector resolves an <c>inspector:add &lt;key&gt;</c> op / a candidate menu path
    /// back to its type).</summary>
    public Type? TypeForKey(string key) => _byKey.GetValueOrDefault(key)?.ComponentType;

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

            if (registry._structurallyCapturedTypes.Contains(type))
                return; // captured as a structural field (Parent / Id), not as a component body

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
