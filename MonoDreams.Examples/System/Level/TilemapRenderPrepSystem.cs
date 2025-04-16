using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.State;
using LDtk;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.Component.Level;

namespace MonoDreams.Examples.System.Level;

/// <summary>
/// Processes LDtk tile layers when a new level is loaded (via CurrentLevelComponent).
/// Creates dedicated entities for each tile layer and populates their DrawComponent
/// with DrawElements for each tile. Cleans up when the level is unloaded.
/// </summary>
public sealed class TilemapRenderPrepSystem : ISystem<GameState>
{
    private const float TILEMAP_BASE_LAYER_DEPTH = 0.9f; // Furthest back layer depth for tiles
    private const float TILEMAP_LAYER_DEPTH_STEP = 0.001f; // Small increment per layer

    private readonly World _world;
    private readonly ContentManager _content;
    private readonly IDisposable _levelLoadedSubscription;
    private readonly IDisposable _levelUnloadedSubscription;

    private EntitySet _tilemapEntitySet; // To find existing tilemap entities for cleanup

    public bool IsEnabled { get; set; } = true;

    public TilemapRenderPrepSystem(World world, ContentManager content)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _content = content ?? throw new ArgumentNullException(nameof(content));

        // Subscribe to changes in the CurrentLevelComponent singleton
        _levelLoadedSubscription = _world.SubscribeWorldComponentAdded<CurrentLevelComponent>(HandleLevelLoaded);
        _levelUnloadedSubscription = _world.SubscribeWorldComponentRemoved<CurrentLevelComponent>(HandleLevelUnloaded);

        // Set to query for existing tilemap entities
        _tilemapEntitySet = _world.GetEntities().With<TilemapLayerComponent>().AsSet();

        // Initial check in case a level is already loaded when the system starts
        if (_world.Has<CurrentLevelComponent>())
        {
            HandleLevelLoaded(_world, _world.Get<CurrentLevelComponent>());
        }
    }

    private void HandleLevelLoaded(World _, in CurrentLevelComponent currentLevelComp)
    {
        if (!IsEnabled) return;

        Console.WriteLine($"New level loaded ({currentLevelComp.LevelData?.Identifier}). Preparing tilemap rendering...");

        // 1. Clean up any existing tilemap entities from a previous level
        CleanupTilemapEntities();

        // 2. Process layers from the new level data
        var levelData = currentLevelComp.LevelData;
        if (levelData?.LayerInstances == null)
        {
            Console.WriteLine($"Level data or layer instances are null for level {levelData?.Identifier}.");
            return;
        }

        // Keep track of loaded textures to avoid reloading per layer if tilesets are reused
        var loadedTextures = new Dictionary<string, Texture2D>();
        float currentLayerDepth = TILEMAP_BASE_LAYER_DEPTH;

        // Iterate layers in reverse order because LDtk draws layers from bottom up (first = back)
        // We want the first layer to have the lowest depth value (drawn first by SpriteBatch FrontToBack).
        for (int i = 0; i < levelData.LayerInstances.Length; i++)
        {
            var layer = levelData.LayerInstances[i];

            // Process only Tile and Auto layers
            if (layer._Type != LayerType.Tiles && layer._Type != LayerType.IntGrid) continue;
            if (!layer.Visible) continue; // Skip hidden layers

            // Ensure we have tileset info
            if (string.IsNullOrEmpty(layer._TilesetRelPath))
            {
                Console.WriteLine($"Skipping layer {layer._Identifier} (IID: {layer.Iid}) because it has no tileset path.");
                continue;
            }

            // Load tileset texture (reuse if already loaded)
            Texture2D tilesetTexture;
            if (!loadedTextures.TryGetValue(layer._TilesetRelPath, out tilesetTexture))
            {
                try
                {
                    // Construct full asset path relative to Content root
                    // LDtk paths might be like "Tilesets/MySet.png". Content expects "Tilesets/MySet"
                    string assetPath = layer._TilesetRelPath.Replace(".png", "").Replace(".aseprite", ""); // Basic cleanup
                    tilesetTexture = _content.Load<Texture2D>(assetPath);
                    loadedTextures.Add(layer._TilesetRelPath, tilesetTexture);
                    Console.WriteLine($"Loaded texture '{assetPath}' for layer {layer._Identifier}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load tileset texture '{layer._TilesetRelPath}' for layer {layer._Identifier}. Skipping layer.");
                    continue; // Skip this layer if texture fails
                }
            }

            // Create a dedicated entity for this tile layer
            var layerEntity = _world.CreateEntity();
            layerEntity.Set(new TilemapLayerComponent(layer.Iid.ToString()));
            var drawComponent = new DrawComponent(); // Create DrawComponent here
            layerEntity.Set(drawComponent); // Add it to the entity

            Console.WriteLine($"Created tilemap entity for layer {layer._Identifier} (IID: {layer.Iid}) with LayerDepth {currentLayerDepth}");


            // Determine which tile array to use
            var tiles = layer._Type == LayerType.Tiles ? layer.GridTiles : layer.AutoLayerTiles;

            // Populate DrawComponent with DrawElements for each tile
            foreach (var tile in tiles)
            {
                // LDtk tile 't' is the Tile ID within the tileset definition.
                // 'Src' provides the pixel coordinates [x,y] of the tile in the tileset texture.
                // 'Px' provides the pixel coordinates [x,y] of the tile in the level.

                var drawElement = new DrawElement
                {
                    Type = DrawElementType.Sprite,
                    Target = RenderTargetID.Main, // Assume tiles go to main render target
                    Texture = tilesetTexture,
                    Position = new Vector2(tile.Px.X, tile.Px.Y),
                    SourceRectangle = new Rectangle(tile.Src.X, tile.Src.Y, layer._GridSize, layer._GridSize),
                    Color = Color.White * layer._Opacity, // Apply layer opacity
                    Size = new Vector2(layer._GridSize, layer._GridSize), // Use GridSize for destination size
                    LayerDepth = currentLayerDepth,
                    // Rotation, Origin, Scale, Effects usually default for tiles
                };
                drawComponent.Drawables.Add(drawElement);
            }

            // Increment layer depth for the next layer (closer to camera)
            currentLayerDepth += TILEMAP_LAYER_DEPTH_STEP;
        }
        Console.WriteLine($"Finished preparing {levelData.LayerInstances.Count(l => l.Visible && (l._Type == LayerType.Tiles || l._Type == LayerType.AutoLayer))} visible tile layers for level {levelData.Identifier}.");
    }

    private void HandleLevelUnloaded(World _, in CurrentLevelComponent currentLevelComp)
    {
        if (!IsEnabled) return;
        Console.WriteLine($"Current level unloaded ({currentLevelComp.LevelData?.Identifier}). Cleaning up tilemap entities...");
        CleanupTilemapEntities();
    }

    private void CleanupTilemapEntities()
    {
        Console.WriteLine($"Cleaning up {_tilemapEntitySet.Count} existing tilemap entities...");
        // Dispose all entities that represent tilemap layers
        // Using ToArray() to avoid issues with modifying the set while iterating (if Dispose removes from set immediately)
        foreach(var entity in _tilemapEntitySet.GetEntities().ToArray())
        {
            entity.Dispose();
        }
        Console.WriteLine("Tilemap entity cleanup complete.");
    }


    public void Update(GameState state)
    {
        // Processing happens on component addition/removal events.
        // Update is likely empty unless you need per-frame logic related to tilemaps.
    }

    public void Dispose()
    {
        // Dispose subscriptions to prevent leaks
        _levelLoadedSubscription?.Dispose();
        _levelUnloadedSubscription?.Dispose();
        // Dispose the entity set? Check DefaultEcs documentation if needed.
        // _tilemapEntitySet?.Dispose();
        GC.SuppressFinalize(this);
    }
}