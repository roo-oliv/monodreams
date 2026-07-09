using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component.Draw;
using MonoDreams.EntityFactory;
using MonoDreams.Message;
using MonoDreams.State;

namespace MonoDreams.System.EntitySpawn;

/// <summary>
/// Modular entity spawning system that delegates entity creation to registered factories.
/// This allows for extensible entity creation without modifying the core system.
/// </summary>
public class EntitySpawnSystem : ISystem<GameState>
{
    public bool IsEnabled { get; set; } = true;
    
    private readonly World _world;
    private readonly ContentManager _content;
    private readonly IReadOnlyDictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    private readonly Dictionary<string, IEntityFactory> _entityFactories;
    // Prefix-dispatched factories: one factory serves every identifier sharing a prefix (e.g. the
    // "prefab:" convention — "prefab:npc-boldo", "prefab:door" all route to the one PrefabFactory, which
    // parses the id off the identifier). Exact-match factories always win; among prefixes the LONGEST
    // match wins (deterministic).
    private readonly Dictionary<string, IEntityFactory> _prefixFactories;

    public EntitySpawnSystem(World world, ContentManager content, IReadOnlyDictionary<RenderTargetID, RenderTarget2D> renderTargets)
    {
        _world = world;
        _content = content;
        _renderTargets = renderTargets;
        _entityFactories = new Dictionary<string, IEntityFactory>();
        _prefixFactories = new Dictionary<string, IEntityFactory>();

        _world.Subscribe<EntitySpawnRequest>(OnEntitySpawnRequest);
    }

    /// <summary>
    /// Register a custom entity factory for a specific identifier
    /// </summary>
    public void RegisterEntityFactory(string identifier, IEntityFactory factory)
    {
        _entityFactories[identifier] = factory;
    }

    /// <summary>
    /// Register a factory for every identifier beginning with <paramref name="prefix"/> — the
    /// convention channel (e.g. <c>"prefab:"</c> → the one <c>PrefabFactory</c> that parses the id from
    /// the request identifier). Exact-match registrations take precedence; the longest matching prefix
    /// wins when several apply.
    /// </summary>
    public void RegisterEntityFactoryPrefix(string prefix, IEntityFactory factory)
    {
        _prefixFactories[prefix] = factory;
    }

    /// <summary>
    /// Remove an entity factory
    /// </summary>
    public void UnregisterEntityFactory(string identifier)
    {
        _entityFactories.Remove(identifier);
    }

    [Subscribe]
    private void OnEntitySpawnRequest(in EntitySpawnRequest request)
    {
        if (_entityFactories.TryGetValue(request.Identifier, out var factory))
        {
            factory.CreateEntity(_world, request);
            return;
        }

        // No exact match — try the prefix channels (longest matching prefix wins).
        var bestPrefix = ResolvePrefixFactory(request.Identifier);
        if (bestPrefix != null)
        {
            bestPrefix.CreateEntity(_world, request);
            return;
        }

        Logger.Warning($"No factory registered for entity type '{request.Identifier}'");
    }

    /// <summary>The registered prefix factory whose prefix <paramref name="identifier"/> begins with,
    /// preferring the LONGEST match, or null if none applies.</summary>
    private IEntityFactory ResolvePrefixFactory(string identifier)
    {
        if (string.IsNullOrEmpty(identifier) || _prefixFactories.Count == 0) return null;

        IEntityFactory best = null;
        var bestLength = -1;
        foreach (var (prefix, factory) in _prefixFactories)
        {
            if (prefix.Length > bestLength && identifier.StartsWith(prefix, StringComparison.Ordinal))
            {
                best = factory;
                bestLength = prefix.Length;
            }
        }
        return best;
    }

    public void Update(GameState state)
    {
        // This system is event-driven, no per-frame updates needed
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
