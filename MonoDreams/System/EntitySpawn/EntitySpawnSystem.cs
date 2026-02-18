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
using Logger = MonoDreams.State.Logger;

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

    public EntitySpawnSystem(World world, ContentManager content, IReadOnlyDictionary<RenderTargetID, RenderTarget2D> renderTargets)
    {
        _world = world;
        _content = content;
        _renderTargets = renderTargets;
        _entityFactories = new Dictionary<string, IEntityFactory>();
        
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
        }
        else
        {
            Logger.Warning($"No factory registered for entity type '{request.Identifier}'");
        }
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
