using System;
using System.Collections.Generic;
using Arch.Core;
using ArchEntity = Arch.Core.Entity;
using ArchWorld = Arch.Core.World;

namespace MonoDreams.ArchSpike.FacadeEventsProof;

// ===================================================================================================
// MiniFacade — the smallest honest implementation of decision D1 (hazard H7) over Arch 2.1.0.
//
// D1 says: the facade owns Set/Remove/Dispose/NotifyChanged/SetSingleton, so IT raises the engine's
// reactive events; Arch's own EVENTS-flag events stay off. This file is that design, cut down to the
// surface the proof needs (issue #119 contract items 3 and 41):
//
//   * entity components   — Set (add-or-update), Get (ref), Has, Remove, NotifyChanged, Dispose,
//                           with Added / Changed(old,new) / Removed / EntityDisposed dispatch;
//   * world singletons    — Set / Get / Has / Remove with Added / Changed(old,new) / Removed;
//   * queries             — With<T> / Without<T> / value-predicate With<T>(pred), membership
//                           maintained by PUBLICATION and enumerated as a snapshot;
//   * a message bus       — Publish / Subscribe<T>, synchronously re-entrant.
//
// It is NOT the wave-1 facade: no ISystem/SequentialSystem, no [Subscribe] scan, no parallel runner,
// no world-generation stamp (D11), no per-component-type query index. Everything here exists to
// answer ONE question ahead of wave 1 — does facade-fired eventing hold up on an ARCHETYPE backend
// when a handler mutates structure while the event is in flight (M10), and when a singleton handler
// disposes and creates entities en masse (item 41)?
//
// Three implementation rules carry the whole design, and wave 2 inherits all three:
//
//   R1. The structural operation completes in Arch BEFORE the event dispatches. A handler therefore
//       never observes a half-applied archetype move, and its own Set/Dispose is just another
//       complete operation — nesting, not re-entering.
//   R2. Query membership is applied BEFORE the event dispatches. A handler (and everything after it
//       in the same frame) sees the query it just changed — no deferral, no command buffer.
//   R3. The facade never holds a `ref`/span across a dispatch. Archetype moves relocate component
//       storage; a ref captured before a handler ran points at the old chunk afterwards. Values are
//       copied out (`old`) before dispatch and re-read after.
// ===================================================================================================

// ------------------------------------------------------------------ handler shapes (M2/M6 exact)
internal delegate void EntityComponentAddedHandler<T>(in Entity entity, in T value);

/// <summary>Carries BOTH values — M6: <c>BoundaryBakeSystem</c>/<c>AudioSystem</c> read <c>old</c>.</summary>
internal delegate void EntityComponentChangedHandler<T>(in Entity entity, in T oldValue, in T newValue);

internal delegate void EntityComponentRemovedHandler<T>(in Entity entity, in T value);

internal delegate void EntityDisposedHandler(in Entity entity);

internal delegate void WorldComponentAddedHandler<T>(EcsWorld world, in T value);

internal delegate void WorldComponentChangedHandler<T>(EcsWorld world, in T oldValue, in T newValue);

internal delegate void WorldComponentRemovedHandler<T>(EcsWorld world, in T value);

/// <summary>The M1 shape: <c>.With((in TRigidBodyComponent b) =&gt; b.Gravity.active)</c>.</summary>
internal delegate bool ComponentPredicate<T>(in T value);

/// <summary>
/// The facade's entity handle. Keeps DefaultEcs' shape (D4/D12): instance generics, an
/// <see cref="IsAlive"/> property, <see cref="Dispose"/>, and value equality.
/// <para>
/// The version is owned by the FACADE, not by Arch: <see cref="EcsWorld"/> bumps it on dispose, so a
/// stale handle can never read the entity that later recycles the same Arch id (contract items 17,
/// 56, 76). The real facade adds a world-generation stamp on top (D11) — out of scope for the proof,
/// which holds a direct world reference instead.
/// </para>
/// </summary>
internal readonly struct Entity : IEquatable<Entity>
{
    internal readonly EcsWorld Owner;
    internal readonly ArchEntity Handle;
    internal readonly int Version;

    internal Entity(EcsWorld owner, ArchEntity handle, int version)
    {
        Owner = owner;
        Handle = handle;
        Version = version;
    }

    public static readonly Entity Null = default;

    public bool IsAlive => Owner != null && Owner.IsAlive(this);

    /// <summary>Add-or-update (H1/D4) — never Arch's update-only <c>Set</c>.</summary>
    public void Set<T>(in T value) => Owner.Set(this, value);

    /// <summary>The <c>entity.Set&lt;ColliderTagComponent&gt;()</c> form (M10 site).</summary>
    public void Set<T>() => Owner.Set(this, default(T));

    public ref T Get<T>() => ref Owner.Get<T>(this);

    public bool Has<T>() => Owner != null && Owner.Has<T>(this);

    public void Remove<T>() => Owner.Remove<T>(this);

    /// <summary>The engine's publication verb (M2): fires Changed with <c>old == new</c>.</summary>
    public void NotifyChanged<T>() => Owner.NotifyChanged<T>(this);

    public void Dispose() => Owner?.DisposeEntity(this);

    public bool Equals(Entity other) =>
        ReferenceEquals(Owner, other.Owner) && Handle.Id == other.Handle.Id && Version == other.Version;

    public override bool Equals(object obj) => obj is Entity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Handle.Id, Version);

    public static bool operator ==(Entity left, Entity right) => left.Equals(right);

    public static bool operator !=(Entity left, Entity right) => !left.Equals(right);

    public override string ToString() => Owner == null ? "Entity.Null" : $"Entity({Handle.Id}v{Version})";
}

/// <summary>
/// The facade world: owns the Arch world, the singleton store, the subscriptions, the message bus and
/// the live queries. Every mutating verb follows the same three steps — apply to Arch, update query
/// membership, then dispatch (R1/R2).
/// </summary>
internal sealed class EcsWorld : IDisposable
{
    private readonly ArchWorld _arch;

    /// <summary>Arch id → facade version. Bumped on dispose so a recycled slot never answers to an old handle.</summary>
    private readonly Dictionary<int, int> _versionById = new();

    private readonly Dictionary<Type, ComponentChannel> _componentChannels = new();
    private readonly Dictionary<Type, WorldComponentChannel> _worldComponentChannels = new();
    private readonly Dictionary<Type, object> _singletons = new();
    private readonly Dictionary<Type, object> _messageChannels = new();
    private readonly List<EntityDisposedHandler> _entityDisposedHandlers = new();
    private readonly List<EntityQuery> _queries = new();
    private readonly HashSet<int> _disposing = new();

    private bool _disposed;

    private EcsWorld(ArchWorld arch) => _arch = arch;

    /// <summary>H10: worlds come from the facade's factory, never from a backend constructor.</summary>
    public static EcsWorld Create() => new(ArchWorld.Create());

    /// <summary>Live query registrations — the leak check for transient <c>using</c> queries (item 10).</summary>
    public int QueryCount => _queries.Count;

    /// <summary>Every live handler across every channel — the other half of the leak check.</summary>
    public int SubscriberCount
    {
        get
        {
            var total = _entityDisposedHandlers.Count;
            foreach (var channel in _componentChannels.Values) total += channel.HandlerCount;
            foreach (var channel in _worldComponentChannels.Values) total += channel.HandlerCount;
            foreach (var channel in _messageChannels.Values) total += ((IMessageChannel)channel).HandlerCount;
            return total;
        }
    }

    // ------------------------------------------------------------------------------- entity life

    public Entity CreateEntity()
    {
        var handle = _arch.Create();
        if (!_versionById.TryGetValue(handle.Id, out var version))
        {
            version = 0;
            _versionById[handle.Id] = 0;
        }

        var entity = new Entity(this, handle, version);

        // A componentless entity can already satisfy a Without<T>-only query, so every query gets a
        // look rather than only the ones interested in some component type.
        foreach (var query in _queries.ToArray()) query.Reevaluate(entity);
        return entity;
    }

    public bool IsAlive(in Entity entity) =>
        !_disposed
        && ReferenceEquals(entity.Owner, this)
        && _versionById.TryGetValue(entity.Handle.Id, out var version)
        && version == entity.Version
        && _arch.IsAlive(entity.Handle);

    /// <summary>
    /// Item 40 + the measured DefaultEcs order: <c>EntityDisposed</c> first, then one
    /// <c>ComponentRemoved</c> per present component, with the entity still reading
    /// <c>IsAlive == true</c> inside the handlers. Values are read from the LIVE entity, before
    /// Arch destroys it. Membership is gone by the time this returns (item 67).
    /// </summary>
    internal void DisposeEntity(in Entity entity)
    {
        // Double-dispose of a dead handle is a silent no-op (item 67); the _disposing guard makes a
        // handler that re-enters Dispose on the SAME entity a no-op too, rather than a stack overflow.
        if (!IsAlive(entity) || !_disposing.Add(entity.Handle.Id)) return;

        try
        {
            foreach (var handler in _entityDisposedHandlers.ToArray()) handler(entity);
            FireComponentRemovedForAll(entity);

            _versionById[entity.Handle.Id] = entity.Version + 1;
            _arch.Destroy(entity.Handle);
            foreach (var query in _queries.ToArray()) query.Drop(entity);
        }
        finally
        {
            _disposing.Remove(entity.Handle.Id);
        }
    }

    private void FireComponentRemovedForAll(in Entity entity)
    {
        if (_componentChannels.Count == 0) return;

        // Snapshot the archetype's component list: a Removed handler is allowed to mutate the dying
        // entity's archetype (R1), which would invalidate a live span mid-loop (R3).
        var components = _arch.GetArchetype(entity.Handle).Signature.Components;
        var runtimeTypes = new ComponentType[components.Length];
        components.CopyTo(runtimeTypes);

        foreach (var componentType in runtimeTypes)
        {
            if (_componentChannels.TryGetValue(componentType.Type, out var channel)
                && _arch.Has(entity.Handle, componentType))
            {
                channel.FireRemovedFromLive(this, entity);
            }
        }
    }

    /// <summary>
    /// The UNFILTERED enumeration surface (item 43): <c>world.GetEntities()</c> with no filter. It
    /// reads Arch, so a hidden singleton carrier — if the facade had one — would show up here. This
    /// facade stores singletons off-world (see <see cref="Set{T}(in T)"/>), so it cannot.
    /// <para>
    /// The result is sorted by entity id because Arch enumerates a chunk in <b>descending</b> index
    /// order (measured — see scenario S0). Inheriting that would silently reverse every
    /// first-match-and-break pick and every membership sweep the engine writes to disk (items 22, 48,
    /// 58, 70, 74). Order is the facade's to define, so it defines one.
    /// </para>
    /// </summary>
    public Entity[] GetAllEntities()
    {
        var all = new List<Entity>();
        var everything = new QueryDescription();
        foreach (ref var chunk in _arch.Query(in everything))
        {
            foreach (var index in chunk)
            {
                var handle = chunk.Entity(index);
                all.Add(new Entity(this, handle, _versionById.TryGetValue(handle.Id, out var v) ? v : 0));
            }
        }

        all.Sort(static (left, right) => left.Handle.Id.CompareTo(right.Handle.Id));
        return all.ToArray();
    }

    // -------------------------------------------------------------------------- entity components

    /// <summary>Add-or-update (H1): absent → Arch <c>Add</c> + Added; present → Arch <c>Set</c> + Changed(old,new).</summary>
    internal void Set<T>(in Entity entity, in T value)
    {
        EnsureAlive(entity, $"Set<{typeof(T).Name}>");

        if (_arch.Has<T>(entity.Handle))
        {
            var oldValue = _arch.Get<T>(entity.Handle);   // copied out BEFORE anything moves (R3)
            _arch.Set(entity.Handle, value);
            Publish(entity, typeof(T));
            Channel<T>(create: false)?.FireChanged(entity, oldValue, value);
            return;
        }

        _arch.Add(entity.Handle, value);                  // structural move completes FIRST (R1)
        Publish(entity, typeof(T));                       // membership applied SECOND (R2)
        Channel<T>(create: false)?.FireAdded(entity, value);
    }

    internal ref T Get<T>(Entity entity)
    {
        EnsureAlive(entity, $"Get<{typeof(T).Name}>");
        return ref _arch.Get<T>(entity.Handle);
    }

    internal bool Has<T>(in Entity entity) => IsAlive(entity) && _arch.Has<T>(entity.Handle);

    /// <summary>Present → Removed carrying the removed value; absent → silent no-op firing nothing (item 39).</summary>
    internal void Remove<T>(in Entity entity)
    {
        if (!Has<T>(entity)) return;

        var oldValue = _arch.Get<T>(entity.Handle);
        _arch.Remove<T>(entity.Handle);
        Publish(entity, typeof(T));
        Channel<T>(create: false)?.FireRemoved(entity, oldValue);
    }

    /// <summary>
    /// M2: re-runs predicate membership AND fires Changed with <c>old == new</c> — for a class
    /// component that means <c>ReferenceEquals(old, new) == true</c>, the discriminator
    /// <c>AudioSystem.cs:141</c> relies on. D14: absent component throws, never a silent no-op.
    /// </summary>
    internal void NotifyChanged<T>(in Entity entity)
    {
        EnsureAlive(entity, $"NotifyChanged<{typeof(T).Name}>");
        if (!_arch.Has<T>(entity.Handle))
        {
            // Message copied verbatim from the measured DefaultEcs 0.18.0-beta01 throw.
            throw new InvalidOperationException($"Entity does not have a component of type {typeof(T).FullName}");
        }

        var value = _arch.Get<T>(entity.Handle);
        Publish(entity, typeof(T));
        Channel<T>(create: false)?.FireChanged(entity, value, value);
    }

    // -------------------------------------------------------------------------- world singletons

    /// <summary>
    /// Absent → Added; present → <b>Changed</b>, not Added (CORE_TENETS §9, the Restart transport
    /// shape). Stored OFF-world: no carrier entity exists to leak into an unfiltered enumeration.
    /// </summary>
    public void Set<T>(in T value)
    {
        if (_singletons.TryGetValue(typeof(T), out var existing))
        {
            var box = (Box<T>)existing;
            var oldValue = box.Value;
            box.Value = value;
            WorldChannel<T>(create: false)?.FireChanged(this, oldValue, value);
            return;
        }

        _singletons[typeof(T)] = new Box<T> { Value = value };
        WorldChannel<T>(create: false)?.FireAdded(this, value);
    }

    public ref T Get<T>()
    {
        if (!_singletons.TryGetValue(typeof(T), out var box))
        {
            throw new InvalidOperationException($"World does not have a component of type {typeof(T).FullName}");
        }

        return ref ((Box<T>)box).Value;
    }

    public bool Has<T>() => _singletons.ContainsKey(typeof(T));

    /// <summary>Present → Removed; absent → nothing (item 39: the editor transport's always-absent legs).</summary>
    public void Remove<T>()
    {
        if (!_singletons.TryGetValue(typeof(T), out var box)) return;

        var oldValue = ((Box<T>)box).Value;
        _singletons.Remove(typeof(T));
        WorldChannel<T>(create: false)?.FireRemoved(this, oldValue);
    }

    // ------------------------------------------------------------------------------ subscriptions

    public IDisposable SubscribeEntityComponentAdded<T>(EntityComponentAddedHandler<T> handler)
    {
        var channel = Channel<T>(create: true);
        channel.Added.Add(handler);
        return new Subscription(() => channel.Added.Remove(handler));
    }

    public IDisposable SubscribeEntityComponentChanged<T>(EntityComponentChangedHandler<T> handler)
    {
        var channel = Channel<T>(create: true);
        channel.Changed.Add(handler);
        return new Subscription(() => channel.Changed.Remove(handler));
    }

    public IDisposable SubscribeEntityComponentRemoved<T>(EntityComponentRemovedHandler<T> handler)
    {
        var channel = Channel<T>(create: true);
        channel.Removed.Add(handler);
        return new Subscription(() => channel.Removed.Remove(handler));
    }

    public IDisposable SubscribeEntityDisposed(EntityDisposedHandler handler)
    {
        _entityDisposedHandlers.Add(handler);
        return new Subscription(() => _entityDisposedHandlers.Remove(handler));
    }

    public IDisposable SubscribeWorldComponentAdded<T>(WorldComponentAddedHandler<T> handler)
    {
        var channel = WorldChannel<T>(create: true);
        channel.Added.Add(handler);
        return new Subscription(() => channel.Added.Remove(handler));
    }

    public IDisposable SubscribeWorldComponentChanged<T>(WorldComponentChangedHandler<T> handler)
    {
        var channel = WorldChannel<T>(create: true);
        channel.Changed.Add(handler);
        return new Subscription(() => channel.Changed.Remove(handler));
    }

    public IDisposable SubscribeWorldComponentRemoved<T>(WorldComponentRemovedHandler<T> handler)
    {
        var channel = WorldChannel<T>(create: true);
        channel.Removed.Add(handler);
        return new Subscription(() => channel.Removed.Remove(handler));
    }

    /// <summary>
    /// Subscribing does NOT replay an already-present value — measured on DefaultEcs 0.18.0-beta01
    /// (items 42/66), which is why the LDtk parsers keep their manual <c>Has</c>+<c>Get</c> replay.
    /// </summary>
    public IDisposable Subscribe<TMessage>(Action<TMessage> handler)
    {
        var channel = GetOrCreateMessageChannel<TMessage>();
        channel.Handlers.Add(handler);
        return new Subscription(() => channel.Handlers.Remove(handler));
    }

    /// <summary>Synchronous and re-entrant (item 64): a nested Publish runs to completion inside its caller.</summary>
    public void Publish<TMessage>(in TMessage message)
    {
        if (!_messageChannels.TryGetValue(typeof(TMessage), out var boxed)) return;

        var channel = (MessageChannel<TMessage>)boxed;
        if (channel.Handlers.Count == 0) return;

        foreach (var handler in channel.Handlers.ToArray()) handler(message);
    }

    // ------------------------------------------------------------------------------------ queries

    public EntityQueryBuilder GetEntities() => new(this);

    internal EntityQuery RegisterQuery(EntityQuery query)
    {
        _queries.Add(query);
        return query;
    }

    internal void UnregisterQuery(EntityQuery query) => _queries.Remove(query);

    /// <summary>
    /// The publication hook (M1/D3/D9): membership is recomputed for THIS entity, for every query
    /// interested in THIS component type, synchronously, before any handler runs. An in-place write
    /// through <c>ref</c> reaches none of this — which is exactly the engine's documented premise.
    /// </summary>
    private void Publish(in Entity entity, Type componentType)
    {
        if (_queries.Count == 0) return;
        foreach (var query in _queries.ToArray()) query.OnPublication(entity, componentType);
    }

    // ------------------------------------------------------------------------------------ teardown

    /// <summary>
    /// ⚠ Reproduces the MEASURED DefaultEcs 0.18.0-beta01 teardown, which contract item 50 gets
    /// wrong: <c>world.Dispose</c> is NOT event-silent. Measured order — every live entity's
    /// <c>EntityDisposed</c> first, then <c>ComponentRemoved</c>, then world components. Wave 1 has
    /// to either restate item 50 this way (what C7 byte-identity will demand, since
    /// <c>AudioSystem.OnAudioSourceRemoved</c> runs here today) or declare a deliberate change.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        var live = GetAllEntities();
        foreach (var entity in live)
        {
            foreach (var handler in _entityDisposedHandlers.ToArray()) handler(entity);
        }

        foreach (var entity in live) FireComponentRemovedForAll(entity);

        foreach (var channel in new List<WorldComponentChannel>(_worldComponentChannels.Values))
        {
            channel.FireRemovedIfPresent(this);
        }

        foreach (var entity in live) _versionById[entity.Handle.Id] = entity.Version + 1;

        _disposed = true;
        _singletons.Clear();
        _queries.Clear();
        ArchWorld.Destroy(_arch);
    }

    // ------------------------------------------------------------------------------------ plumbing

    private void EnsureAlive(in Entity entity, string operation)
    {
        if (!IsAlive(entity)) throw new InvalidOperationException($"{operation} on a dead handle {entity}.");
    }

    private ComponentChannel<T> Channel<T>(bool create)
    {
        if (_componentChannels.TryGetValue(typeof(T), out var existing)) return (ComponentChannel<T>)existing;
        if (!create) return null;

        var channel = new ComponentChannel<T>();
        _componentChannels[typeof(T)] = channel;
        return channel;
    }

    private WorldComponentChannel<T> WorldChannel<T>(bool create)
    {
        if (_worldComponentChannels.TryGetValue(typeof(T), out var existing)) return (WorldComponentChannel<T>)existing;
        if (!create) return null;

        var channel = new WorldComponentChannel<T>();
        _worldComponentChannels[typeof(T)] = channel;
        return channel;
    }

    private MessageChannel<TMessage> GetOrCreateMessageChannel<TMessage>()
    {
        if (_messageChannels.TryGetValue(typeof(TMessage), out var existing)) return (MessageChannel<TMessage>)existing;

        var channel = new MessageChannel<TMessage>();
        _messageChannels[typeof(TMessage)] = channel;
        return channel;
    }

    private sealed class Box<T>
    {
        public T Value;
    }

    private sealed class Subscription : IDisposable
    {
        private Action _unsubscribe;

        public Subscription(Action unsubscribe) => _unsubscribe = unsubscribe;

        public void Dispose()
        {
            _unsubscribe?.Invoke();
            _unsubscribe = null;
        }
    }

    /// <summary>
    /// Type-erased handle on a typed handler list. The generic instantiation is created where
    /// <c>T</c> is statically known (Subscribe/Set), so runtime dispatch — the <c>Dispose</c> cascade,
    /// which only knows <see cref="Type"/> — needs no <c>MakeGenericMethod</c> and stays AOT-safe (D12).
    /// </summary>
    private abstract class ComponentChannel
    {
        public abstract int HandlerCount { get; }

        public abstract void FireRemovedFromLive(EcsWorld world, in Entity entity);
    }

    private sealed class ComponentChannel<T> : ComponentChannel
    {
        public readonly List<EntityComponentAddedHandler<T>> Added = new();
        public readonly List<EntityComponentChangedHandler<T>> Changed = new();
        public readonly List<EntityComponentRemovedHandler<T>> Removed = new();

        public override int HandlerCount => Added.Count + Changed.Count + Removed.Count;

        // Every dispatch walks a COPY: a handler is allowed to subscribe, unsubscribe, or destroy
        // entities while the event is in flight (M10, item 41).
        public void FireAdded(in Entity entity, in T value)
        {
            if (Added.Count == 0) return;
            foreach (var handler in Added.ToArray()) handler(entity, value);
        }

        public void FireChanged(in Entity entity, in T oldValue, in T newValue)
        {
            if (Changed.Count == 0) return;
            foreach (var handler in Changed.ToArray()) handler(entity, oldValue, newValue);
        }

        public void FireRemoved(in Entity entity, in T value)
        {
            if (Removed.Count == 0) return;
            foreach (var handler in Removed.ToArray()) handler(entity, value);
        }

        public override void FireRemovedFromLive(EcsWorld world, in Entity entity)
        {
            if (Removed.Count == 0) return;
            var value = world.Get<T>(entity);   // read while the entity is still alive (item 40)
            FireRemoved(entity, value);
        }
    }

    private abstract class WorldComponentChannel
    {
        public abstract int HandlerCount { get; }

        public abstract void FireRemovedIfPresent(EcsWorld world);
    }

    private sealed class WorldComponentChannel<T> : WorldComponentChannel
    {
        public readonly List<WorldComponentAddedHandler<T>> Added = new();
        public readonly List<WorldComponentChangedHandler<T>> Changed = new();
        public readonly List<WorldComponentRemovedHandler<T>> Removed = new();

        public override int HandlerCount => Added.Count + Changed.Count + Removed.Count;

        public void FireAdded(EcsWorld world, in T value)
        {
            if (Added.Count == 0) return;
            foreach (var handler in Added.ToArray()) handler(world, value);
        }

        public void FireChanged(EcsWorld world, in T oldValue, in T newValue)
        {
            if (Changed.Count == 0) return;
            foreach (var handler in Changed.ToArray()) handler(world, oldValue, newValue);
        }

        public void FireRemoved(EcsWorld world, in T value)
        {
            if (Removed.Count == 0) return;
            foreach (var handler in Removed.ToArray()) handler(world, value);
        }

        public override void FireRemovedIfPresent(EcsWorld world)
        {
            if (Removed.Count == 0 || !world.Has<T>()) return;
            FireRemoved(world, world.Get<T>());
        }
    }

    private interface IMessageChannel
    {
        int HandlerCount { get; }
    }

    private sealed class MessageChannel<TMessage> : IMessageChannel
    {
        public readonly List<Action<TMessage>> Handlers = new();

        public int HandlerCount => Handlers.Count;
    }
}

/// <summary>
/// DefaultEcs-shaped builder (D4): <c>world.GetEntities().With&lt;A&gt;().Without&lt;B&gt;()
/// .With((in C c) =&gt; …).AsSet()</c>.
/// </summary>
internal sealed class EntityQueryBuilder
{
    private readonly EcsWorld _world;
    private readonly HashSet<Type> _interest = new();
    private readonly List<Func<EcsWorld, Entity, bool>> _tests = new();

    internal EntityQueryBuilder(EcsWorld world) => _world = world;

    public EntityQueryBuilder With<T>()
    {
        _interest.Add(typeof(T));
        _tests.Add((world, entity) => world.Has<T>(entity));
        return this;
    }

    public EntityQueryBuilder Without<T>()
    {
        _interest.Add(typeof(T));
        _tests.Add((world, entity) => !world.Has<T>(entity));
        return this;
    }

    /// <summary>
    /// The value predicate Arch has no equivalent for (M1). It is evaluated ONLY here and on
    /// publication of <typeparamref name="T"/> — never per enumeration — so membership is a cached
    /// answer, exactly as the engine's premise documents.
    /// </summary>
    public EntityQueryBuilder With<T>(ComponentPredicate<T> predicate)
    {
        _interest.Add(typeof(T));
        _tests.Add((world, entity) =>
        {
            if (!world.Has<T>(entity)) return false;
            ref var value = ref world.Get<T>(entity);
            return predicate(in value);
        });
        return this;
    }

    public EntityQuery AsSet() => _world.RegisterQuery(new EntityQuery(_world, _interest, _tests));
}

/// <summary>
/// Publication-driven materialized membership (D3), enumerated as a snapshot taken at EACH
/// enumeration (D9 — the word "frame-stable" is deliberately absent).
/// </summary>
internal sealed class EntityQuery : IDisposable
{
    private readonly EcsWorld _world;
    private readonly HashSet<Type> _interest;
    private readonly List<Func<EcsWorld, Entity, bool>> _tests;

    // Insertion-ordered membership: the snapshot order is deterministic and documented (item 22),
    // rather than whatever order the backend happens to hand out.
    private readonly List<Entity> _order = new();
    private readonly HashSet<Entity> _members = new();

    private bool _disposed;

    internal EntityQuery(EcsWorld world, HashSet<Type> interest, List<Func<EcsWorld, Entity, bool>> tests)
    {
        _world = world;
        _interest = interest;
        _tests = tests;

        // Construction-time backfill (items 11/54): a query built over an already-populated world
        // seeds membership by a live scan, and is publication-driven from then on.
        foreach (var entity in world.GetAllEntities()) Reevaluate(entity);
    }

    /// <summary>Deliberately unimplemented (item 9) — a silent default would hide the gap.</summary>
    public int Count => throw new NotSupportedException(
        "EntityQuery.Count is deliberately unimplemented (contract item 9): enumerate the snapshot instead.");

    /// <summary>A fresh snapshot per call — never a per-frame cache (D9).</summary>
    public Entity[] GetEntities() => _order.ToArray();

    public IEnumerator<Entity> GetEnumerator() => ((IEnumerable<Entity>)GetEntities()).GetEnumerator();

    internal void OnPublication(in Entity entity, Type componentType)
    {
        if (_disposed || !_interest.Contains(componentType)) return;
        Reevaluate(entity);
    }

    internal void Reevaluate(in Entity entity)
    {
        if (_disposed) return;

        var matches = entity.IsAlive;
        if (matches)
        {
            foreach (var test in _tests)
            {
                if (test(_world, entity)) continue;
                matches = false;
                break;
            }
        }

        if (matches)
        {
            if (_members.Add(entity)) _order.Add(entity);
            return;
        }

        Drop(entity);
    }

    internal void Drop(in Entity entity)
    {
        if (_members.Remove(entity)) _order.Remove(entity);
    }

    /// <summary>Unhooks from the world — the transient <c>using</c>-scoped query shape (item 10).</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _world.UnregisterQuery(this);
        _members.Clear();
        _order.Clear();
    }
}
