using DefaultEcs;
using DefaultEcs.System;
using LDtk;
using Microsoft.Xna.Framework.Content;
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
    private readonly ContentManager _content;
    private readonly LDtkWorld _ldtkWorld;

    /// <summary>
    /// Listens for LoadLevelRequest messages,
    /// finds the requested level, and sets the CurrentLevelComponent singleton in the World.
    /// </summary>
    public LevelLoadRequestSystem(World world, ContentManager content)
    {
        _world = world;
        _content = content;
        _ldtkWorld = _content.Load<LDtkFile>("World").LoadSingleWorld();
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
            // var levelData = _ldtkWorld.Levels?.FirstOrDefault(l => l.Identifier == levelIdentifier);
            var levelData = _content.Load<LDtkLevel>($"World/{levelIdentifier}");
            
            if (levelData != null)
            {
                Console.WriteLine($"Found level data for '{levelIdentifier}'. Setting CurrentLevelComponent.");

                _world.Set(new CurrentLevelComponent(levelData));
                _world.Set(new CurrentBackgroundColorComponent(levelData._BgColor));

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
            Console.WriteLine(
                $"Error during level activation request handling for level '{levelIdentifier}':" +
                $"\n-----\n{ex.Message}\n{ex.StackTrace}\n-----");
             // Potentially publish a LevelLoadFailed message?
              _world.Remove<CurrentLevelComponent>();
              _world.Remove<CurrentBackgroundColorComponent>();
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