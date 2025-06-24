using DefaultEcs;
using MonoDreams.Examples.Message.Level;

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
    Entity CreateEntity(World world, in EntitySpawnRequest request);
}
