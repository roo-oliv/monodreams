using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.EntityFactory;
using MonoDreams.Examples.Message.Level;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Level;

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
        
        // Register default entity factories
        RegisterDefaultFactories();
        
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

    private void RegisterDefaultFactories()
    {
        // Register built-in entity factories
        RegisterEntityFactory("Player", new PlayerEntityFactory(_content));
        RegisterEntityFactory("Enemy", new NPCEntityFactory(_content));
        // Add more default factories as needed
    }

    [Subscribe]
    private void OnEntitySpawnRequest(in EntitySpawnRequest request)
    {
        if (_entityFactories.TryGetValue(request.Identifier, out var factory))
        {
            var entity = factory.CreateEntity(_world, request);
            
            // Optional: Add common components that all spawned entities might need
            if (!entity.Has<EntityInfo>())
            {
                entity.Set(new EntityInfo(DetermineEntityType(request.Identifier)));
            }
        }
        else
        {
            // Log warning or throw exception for unknown entity types
            Console.WriteLine($"Warning: No factory registered for entity type '{request.Identifier}'");
        }
    }

    private EntityType DetermineEntityType(string identifier)
    {
        return identifier switch
        {
            "Player" => EntityType.Player,
            "Enemy" => EntityType.Enemy,
            _ => EntityType.Enemy // Default fallback
        };
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