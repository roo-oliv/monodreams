using Flecs.NET.Core;
using MonoDreams.Examples.Message.Level;
using DefaultEcsWorld = DefaultEcs.World;
using DefaultEcsEntity = DefaultEcs.Entity;

namespace MonoDreams.Examples.EntityFactory;

/// <summary>
/// Factory interface for creating entities from spawn requests.
/// Implement this interface to create custom entity types.
/// </summary>
public interface IEntityFactory
{
    /// <summary>
    /// Creates an entity in the given world based on the spawn request
    /// </summary>
    DefaultEcsEntity CreateEntity(World world, DefaultEcsWorld defaultEcsWorld, in EntitySpawnRequest request);
}
