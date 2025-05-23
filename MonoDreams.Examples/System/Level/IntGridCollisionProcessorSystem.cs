using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.State; // For GameState
using LDtk; // From LDtkMonogame
using MonoDreams.Examples.Component.Level; // For CurrentLevelComponent and LevelCollisionGrid
using System; // For ArgumentNullException
using MonoDreams.Examples.Message.Level; // For CollisionGridReadyMessage

namespace MonoDreams.Examples.System.Level;

/// <summary>
/// Processes IntGrid layers from an LDtk level (e.g., "Collision" layer)
/// and creates a LevelCollisionGrid resource, storing it as a world component.
/// </summary>
public sealed class IntGridCollisionProcessorSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly IDisposable _levelLoadedSubscription;
    private readonly string _targetIntGridLayerIdentifier;

    public bool IsEnabled { get; set; } = true;

    public IntGridCollisionProcessorSystem(World world, string targetIntGridLayerIdentifier = "Collision")
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _targetIntGridLayerIdentifier = targetIntGridLayerIdentifier;

        // Subscribe to CurrentLevelComponent being added
        _levelLoadedSubscription = _world.SubscribeWorldComponentAdded<CurrentLevelComponent>(HandleLevelLoaded);

        // Initial check in case a level is already loaded
        if (_world.Has<CurrentLevelComponent>())
        {
            HandleLevelLoaded(_world, _world.Get<CurrentLevelComponent>());
        }
        Console.WriteLine($"IntGridCollisionProcessorSystem initialized. Will process IntGrid layer: '{_targetIntGridLayerIdentifier}'.");
    }

    private void HandleLevelLoaded(World world, in CurrentLevelComponent currentLevelComp)
    {
        if (!IsEnabled) return;

        var levelData = currentLevelComp.LevelData;
        if (levelData?.LayerInstances == null)
        {
            Console.WriteLine("IntGridCollisionProcessorSystem: Level data or layer instances are null.");
            return;
        }

        Console.WriteLine($"IntGridCollisionProcessorSystem: Processing level '{levelData.Identifier}' for IntGrid layer '{_targetIntGridLayerIdentifier}'.");

        foreach (var layer in levelData.LayerInstances)
        {
            if (layer._Type == LayerType.IntGrid && layer._Identifier == _targetIntGridLayerIdentifier)
            {
                Console.WriteLine($"Found target IntGrid layer: '{layer._Identifier}'");

                // Assuming IntGrid values are directly available as IntGridValueInstance[] on the layer.
                // Common properties: layer._CWid, layer._CHei, layer._GridSize
                // And layer.IntGrid (or IntGridValues) for IntGridValueInstance[]
                // Each IntGridValueInstance has Cx, Cy, V
                
                // LDtkMonogame might store all IntGrid values (including zeros) in IntGridCsv (a long[] or int[])
                // or provide non-zero values in an array like `layer.IntGrid`.
                // We'll prefer `IntGridValueInstance` if available, assuming it's `layer.IntGrid`.
                // If `layer.IntGrid` (or similar for IntGridValueInstance[]) is null,
                // one might need to parse `layer.IntGridCsv` if that's how LDtkMonogame provides it.

                int gridWidth = layer._CWid;
                int gridHeight = layer._CHei;
                int cellSize = layer._GridSize;
                Vector2 worldOffset = new Vector2(layer.PxTotalOffsetX, layer.PxTotalOffsetY);

                var collisionGrid = new LevelCollisionGrid(gridWidth, gridHeight, cellSize, worldOffset);

                // If LDtkMonogame provides IntGridValueInstance directly (preferred)
                if (layer.IntGrid != null) // Assuming 'IntGrid' is the property for IntGridValueInstance[]
                {
                    foreach (var cellInfo in layer.IntGrid) // LDtk.IntGridValueInstance
                    {
                        // cellInfo.Cx, cellInfo.Cy are cell coordinates
                        // cellInfo.V is the integer value
                        collisionGrid.SetValue(cellInfo.Cx, cellInfo.Cy, (int)cellInfo.V);
                    }
                    Console.WriteLine($"Populated LevelCollisionGrid from layer.IntGrid ({layer.IntGrid.Length} values).");
                }
                // Fallback or alternative: if data is in IntGridCsv (long[] where index = x + y * width)
                // else if (layer.IntGridCsv != null && layer.IntGridCsv.Length == gridWidth * gridHeight)
                // {
                //     for (int y = 0; y < gridHeight; y++)
                //     {
                //         for (int x = 0; x < gridWidth; x++)
                //         {
                //             long value = layer.IntGridCsv[x + y * gridWidth];
                //             if (value != 0) // Only set non-zero values, or set all
                //             {
                //                 collisionGrid.SetValue(x, y, (int)value);
                //             }
                //         }
                //     }
                //     Console.WriteLine("Populated LevelCollisionGrid from layer.IntGridCsv.");
                // }
                else
                {
                    // This case might occur if `layer.IntGrid` is not the correct property name
                    // or if the IntGrid layer is empty / has no values set in LDtk.
                    // An empty grid is fine if the layer is meant to be empty.
                    Console.WriteLine($"Warning: IntGrid layer '{layer._Identifier}' found, but no direct IntGridValueInstance data (e.g., layer.IntGrid) was available or it was empty. The collision grid might be empty unless IntGridCsv parsing is implemented and used.");
                }


                // Store the populated grid as a world component
                _world.Set(collisionGrid); // This replaces any existing LevelCollisionGrid
                Console.WriteLine($"LevelCollisionGrid resource created and set in the world for layer '{_targetIntGridLayerIdentifier}'. Dimensions: {gridWidth}x{gridHeight}");

                // Publish a message
                _world.Publish(new CollisionGridReadyMessage());
                Console.WriteLine("Published CollisionGridReadyMessage.");

                // Assuming only one IntGrid layer with this identifier is processed per level
                return; 
            }
        }
        Console.WriteLine($"IntGridCollisionProcessorSystem: Target IntGrid layer '{_targetIntGridLayerIdentifier}' not found in level '{levelData.Identifier}'.");
    }

    public void Update(GameState state)
    {
        // Processing happens on component addition (level load)
    }

    public void Dispose()
    {
        _levelLoadedSubscription?.Dispose();
        GC.SuppressFinalize(this);
        Console.WriteLine("IntGridCollisionProcessorSystem disposed.");
    }
}
