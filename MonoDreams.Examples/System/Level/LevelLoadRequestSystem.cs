using DefaultEcs;
using DefaultEcs.System;
using LDtk;
using MonoDreams.Examples.Component.Level;
using MonoDreams.Examples.Message.Level;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Level;

/// <summary>
/// Listens for LoadLevelRequest messages,
/// finds the requested level, and sets the CurrentLevelComponent singleton in the World.
/// </summary>
public sealed class LevelLoadRequestSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly LDtkWorld _ldtkWorld;

    /// <summary>
    /// Listens for LoadLevelRequest messages,
    /// finds the requested level, and sets the CurrentLevelComponent singleton in the World.
    /// </summary>
    public LevelLoadRequestSystem(World world, LDtkWorld ldtkWorld)
    {
        _world = world;
        _ldtkWorld = ldtkWorld;
        _world.Subscribe<LoadLevelRequest>(On);
    }

    public bool IsEnabled { get; set; } = true;

    [Subscribe]
    public void On(in LoadLevelRequest message)
    {
        if (!IsEnabled) return;

        var levelIdentifier = message.LevelIdentifier;
        Console.WriteLine($"Received request to activate level '{levelIdentifier}'.");

        try
        {
            // Get the specific level data from the loaded world
            var levelData = _ldtkWorld.Levels?.FirstOrDefault(l => l.Identifier == levelIdentifier);

            if (levelData != null)
            {
                Console.WriteLine($"Found level data for '{levelIdentifier}'. Setting CurrentLevelComponent.");

                // Set the singleton component in the world, overwriting any previous level.
                _world.Set(new CurrentLevelComponent(levelData));

                // Optional: Publish a success message if other systems need to react immediately AFTER activation
                // _world.Publish(new LevelLoadSuccessMessage(levelData));
            }
            else
            {
                Console.WriteLine($"Failed to find level data for '{levelIdentifier}' in the loaded LDtkWorld.");
                // Potentially publish a LevelLoadFailed message?
                // Maybe clear the CurrentLevelComponent if loading fails?
                 _world.Remove<CurrentLevelComponent>();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during level activation request handling for level '{levelIdentifier}': {ex.Message}");
             // Potentially publish a LevelLoadFailed message?
             // Maybe clear the CurrentLevelComponent on error?
              _world.Remove<CurrentLevelComponent>();
        }
    }

    public void Update(GameState state)
    {
        // Message handling is done via subscription.
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}