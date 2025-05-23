using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.State; // Assuming GameState is here, adjust if not
using MonoDreams.Examples.Message.Level; // For EntitySpawnRequest
using System;
using System.Collections.Generic; // For List<DrawElement>
using Microsoft.Xna.Framework; // For Vector2, Color, Rectangle etc.
using MonoDreams.Examples.Component; // For PlayerTagComponent, CoinTagComponent
using MonoDreams.Examples.Component.Level; // For LDtkInstanceComponent
using MonoDreams.Examples.Component.Draw; // For DrawComponent, SpriteInfo
using MonoDreams.Component; // For PositionComponent

namespace MonoDreams.Examples.System.Level;

/// <summary>
/// Listens for EntitySpawnRequest messages and spawns ECS entities
/// with components based on configurable mapping rules.
/// </summary>
public sealed class ConfigurableEntitySpawnerSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly IDisposable _entitySpawnRequestSubscription;

    // Placeholder for the configuration map (will be detailed in the next step)
    private readonly Dictionary<string, Action<World, EntitySpawnRequest, Entity>> _spawnConfigurations;

    public bool IsEnabled { get; set; } = true;

    public ConfigurableEntitySpawnerSystem(World world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));

        // Subscribe to EntitySpawnRequest messages
        _entitySpawnRequestSubscription = _world.Subscribe<EntitySpawnRequest>(HandleEntitySpawnRequest);

        // Initialize configurations (will be done in a subsequent step)
        _spawnConfigurations = new Dictionary<string, Action<World, EntitySpawnRequest, Entity>>();
        DefineConfigurations();

        Console.WriteLine("ConfigurableEntitySpawnerSystem initialized and subscribed to EntitySpawnRequest.");
    }

    private void HandleEntitySpawnRequest(in EntitySpawnRequest request)
    {
        if (!IsEnabled) return;

        // Console.WriteLine($"Received EntitySpawnRequest for Identifier: {request.Identifier}"); // This line can be removed or kept for verbose logging

        if (_spawnConfigurations.TryGetValue(request.Identifier, out var spawnAction))
        {
            var entity = _world.CreateEntity();
            // Attempt to get EntityID using DefaultEcs.Serialization.EntityComponent if available.
            // If DefaultEcs.Serialization is not available or EntityComponent is not used, this part of the log will need adjustment.
            // string entityIdString = "N/A";
            // try
            // {
                // This requires DefaultEcs.Serialization and the EntityComponent to be implicitly added or manually added.
                // If not, this will throw or be unavailable. For now, assuming it might be.
                // entityIdString = entity.Get<DefaultEcs.Serialization.EntityComponent>().Id.ToString();
                // A safer approach if unsure about DefaultEcs.Serialization:
            //     entityIdString = entity.ToString(); // Or some other stable way to identify the entity for logging if available
            // }
            // catch
            // {
                // If Get<EntityComponent> fails or DefaultEcs.Serialization is not set up for this.
            // }
            Console.WriteLine($"Creating entity for '{request.Identifier}' (IID: {request.InstanceIid}, ECS EntityID: {entity.Get<DefaultEcs.Serialization.EntityComponent>().Id})."); // Added EntityID for better tracking

            try
            {
                spawnAction(_world, request, entity); // Execute the configuration action
                Console.WriteLine($"Successfully configured entity '{request.Identifier}' (IID: {request.InstanceIid})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error configuring entity '{request.Identifier}' (IID: {request.InstanceIid}): {ex.Message}{Environment.NewLine}StackTrace: {ex.StackTrace}");
                // Optional: Clean up the partially configured entity on error.
                // It's often better to let it exist in an error state for debugging,
                // or have a specific "ErrorComponent" added.
                // For now, we'll let it be, but if it causes issues, disposal might be needed:
                // entity.Dispose();
                // Console.WriteLine($"Disposed partially configured entity '{request.Identifier}' (IID: {request.InstanceIid}) due to error.");
            }
        }
        else
        {
            Console.WriteLine($"No spawn configuration found for Identifier: '{request.Identifier}' (IID: {request.InstanceIid})");
        }
    }

    private void DefineConfigurations()
    {
        Console.WriteLine("Defining entity spawn configurations...");

        // Configuration for "PlayerStart"
        _spawnConfigurations["PlayerStart"] = (world, request, entity) =>
        {
            Console.WriteLine($"Executing PlayerStart configuration for IID: {request.InstanceIid}");
            entity.Set(new PositionComponent { Current = request.Position });
            entity.Set(new PlayerTagComponent());
            entity.Set(new LDtkInstanceComponent(request.InstanceIid));

            // Example: Use a custom field "SpritePath" for the texture, or a default.
            // string spritePath = "Characters/Basic Charakter Spritesheet"; // Default
            // if (request.CustomFields.TryGetValue("SpritePath", out var pathValue) && pathValue is string pathStr)
            // {
            //     spritePath = pathStr;
            // }

            // Placeholder SpriteInfo - actual texture loading and source rect would be more complex
            // and might involve a ContentManager or AssetLoader service passed into the system.
            // For now, we'll assume a texture can be referred to by path for SpriteInfo.
            // MasterRenderSystem would then handle loading this via ContentManager.
            entity.Set(new SpriteInfo
            {
                TextureName = "Characters/Basic Charakter Spritesheet", // Placeholder texture
                SourceRectangle = new Rectangle(0, 0, 16, 16), // Placeholder source rectangle
                Origin = request.Pivot * new Vector2(request.Size.X, request.Size.Y) // Adjust origin based on pivot and size
            });

            // Add DrawComponent, assuming SpritePrepSystem or similar will populate DrawElements from SpriteInfo
            var drawElements = new List<DrawElement>();
            // Potentially, a system later on (like a SpritePrepSystem) would convert SpriteInfo
            // into a DrawElement and add it to this list.
            // Or, if entities with SpriteInfo are directly handled by MasterRenderSystem,
            // this DrawComponent might just need to exist.
            // For now, let's add an empty one, assuming SpriteInfo is the primary driver for rendering.
            entity.Set(new DrawComponent(drawElements));

            Console.WriteLine($"Added Position, PlayerTag, LDtkInstance, SpriteInfo, DrawComponent to PlayerStart (IID: {request.InstanceIid})");
        };

        // Configuration for "Coin"
        _spawnConfigurations["Coin"] = (world, request, entity) =>
        {
            Console.WriteLine($"Executing Coin configuration for IID: {request.InstanceIid}");
            entity.Set(new PositionComponent { Current = request.Position });
            entity.Set(new CoinTagComponent());
            entity.Set(new LDtkInstanceComponent(request.InstanceIid));

            // string spritePath = "Objects/Coin"; // Default or from custom field
            // if (request.CustomFields.TryGetValue("SpritePath", out var pathValue) && pathValue is string pathStr)
            // {
            //     spritePath = pathStr;
            // }

            entity.Set(new SpriteInfo
            {
                TextureName = "Objects/Coin", // Placeholder texture name for a coin
                SourceRectangle = new Rectangle(0, 0, 16, 16), // Placeholder
                Origin = request.Pivot * new Vector2(request.Size.X, request.Size.Y)
            });
            
            var drawElements = new List<DrawElement>();
            // Similar to PlayerStart, assuming SpriteInfo drives rendering via a prep system or MasterRenderSystem.
            entity.Set(new DrawComponent(drawElements));
            
            Console.WriteLine($"Added Position, CoinTag, LDtkInstance, SpriteInfo, DrawComponent to Coin (IID: {request.InstanceIid})");
        };

        Console.WriteLine($"Defined {_spawnConfigurations.Count} entity spawn configurations.");
    }

    public void Update(GameState state)
    {
        // Most logic is event-driven via HandleEntitySpawnRequest.
        // This update method could be used for other periodic tasks if needed.
    }

    public void Dispose()
    {
        _entitySpawnRequestSubscription?.Dispose();
        GC.SuppressFinalize(this);
        Console.WriteLine("ConfigurableEntitySpawnerSystem disposed.");
    }
}
