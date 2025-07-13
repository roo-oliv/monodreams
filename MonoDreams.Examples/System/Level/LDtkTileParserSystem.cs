using DefaultEcs;
using DefaultEcs.System;
using LDtk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Examples.Component.Level;
using MonoDreams.Examples.Message.Level;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Level;

/// <summary>
/// Processes LDtk tile layers when a new level is loaded and creates individual entities for each tile.
/// This system replaces the old TilemapRenderPrepSystem approach of bundling all tiles into single entities.
/// </summary>
public sealed class LDtkTileParserSystem : ISystem<GameState>
{
    private const float TILEMAP_BASE_LAYER_DEPTH = 0.09f;
    private const float TILEMAP_LAYER_DEPTH_STEP = 0.001f;

    private readonly World _world;
    private readonly ContentManager _content;
    private readonly IDisposable _levelLoadedSubscription;
    private readonly IDisposable _levelUnloadedSubscription;
    private readonly Dictionary<string, Texture2D> _loadedTextures = new();
    
    // Track tile entities for cleanup
    private readonly HashSet<Entity> _tileEntities = new();

    public bool IsEnabled { get; set; } = true;

    public LDtkTileParserSystem(World world, ContentManager content)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _content = content ?? throw new ArgumentNullException(nameof(content));

        _levelLoadedSubscription = _world.SubscribeWorldComponentAdded<CurrentLevelComponent>(HandleLevelLoaded);
        _levelUnloadedSubscription = _world.SubscribeWorldComponentRemoved<CurrentLevelComponent>(HandleLevelUnloaded);

        if (_world.Has<CurrentLevelComponent>())
        {
            HandleLevelLoaded(_world, _world.Get<CurrentLevelComponent>());
        }
    }

    private void HandleLevelLoaded(World _, in CurrentLevelComponent currentLevelComp)
    {
        if (!IsEnabled) return;

        Console.WriteLine($"Parsing tiles for level '{currentLevelComp.LevelData?.Identifier}'...");
        
        // Clean up existing tiles
        CleanupTileEntities();
        
        var levelData = currentLevelComp.LevelData;
        if (levelData?.LayerInstances == null)
        {
            Console.WriteLine("No layer instances found in level data.");
            return;
        }

        var currentLayerDepth = TILEMAP_BASE_LAYER_DEPTH;
        int tilesProcessed = 0;

        foreach (var layer in levelData.LayerInstances)
        {
            // Process only Tile and Auto layers
            if (layer._Type != LayerType.Tiles && layer._Type != LayerType.AutoLayer) continue;
            if (!layer.Visible) continue;

            if (string.IsNullOrEmpty(layer._TilesetRelPath))
            {
                Console.WriteLine($"Skipping layer {layer._Identifier} - no tileset path.");
                continue;
            }

            // Load tileset texture
            if (!_loadedTextures.TryGetValue(layer._TilesetRelPath, out var tilesetTexture))
            {
                try
                {
                    string assetPath = layer._TilesetRelPath.Replace(".png", "").Replace(".aseprite", "");
                    tilesetTexture = _content.Load<Texture2D>(assetPath);
                    _loadedTextures.Add(layer._TilesetRelPath, tilesetTexture);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load tileset '{layer._TilesetRelPath}': {ex.Message}");
                    continue;
                }
            }

            // Process tiles in this layer
            var tiles = layer._Type == LayerType.Tiles ? layer.GridTiles : layer.AutoLayerTiles;
            
            foreach (var tile in tiles)
            {
                var identifier = layer._Identifier == "Wall_AutoLayer" ? "Wall" : "Tile";
                // Create tile spawn request
                var tileRequest = new EntitySpawnRequest(
                    identifier: identifier,
                    instanceIid: $"tile_{layer.Iid}_{tile.Px.X}_{tile.Px.Y}",
                    position: new Vector2(tile.Px.X, tile.Px.Y),
                    size: new Vector2(layer._GridSize, layer._GridSize),
                    pivot: Vector2.Zero,
                    tilesetPosition: new Vector2(tile.Src.X, tile.Src.Y),
                    layer: layer,
                    customFields: new Dictionary<string, object>
                    {
                        ["layerDepth"] = currentLayerDepth,
                        ["tilesetTexture"] = tilesetTexture,
                        ["tileId"] = tile.T
                    }
                );

                _world.Publish(tileRequest);
                tilesProcessed++;
            }

            currentLayerDepth -= TILEMAP_LAYER_DEPTH_STEP;
        }

        Console.WriteLine($"Published {tilesProcessed} tile spawn requests for level '{levelData.Identifier}'.");
    }

    private void HandleLevelUnloaded(World _, in CurrentLevelComponent currentLevelComp)
    {
        if (!IsEnabled) return;
        Console.WriteLine($"Cleaning up tiles for level '{currentLevelComp.LevelData?.Identifier}'...");
        CleanupTileEntities();
    }

    private void CleanupTileEntities()
    {
        Console.WriteLine($"Disposing {_tileEntities.Count} tile entities...");
        foreach (var entity in _tileEntities.ToArray())
        {
            if (entity.IsAlive)
            {
                entity.Dispose();
            }
        }
        _tileEntities.Clear();
    }

    public void Update(GameState state)
    {
        // Processing happens on component addition/removal events
    }

    public void Dispose()
    {
        _levelLoadedSubscription?.Dispose();
        _levelUnloadedSubscription?.Dispose();
        CleanupTileEntities();
        _loadedTextures.Clear();
        GC.SuppressFinalize(this);
    }
}
