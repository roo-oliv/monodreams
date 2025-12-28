using System.Text.Json;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Examples.Level;
using MonoDreams.Examples.Message.Level;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Level;

/// <summary>
/// Parses Blender-exported JSON level files and creates entities for meshes.
/// Handles LoadLevelRequest messages where the identifier starts with "Blender_".
/// </summary>
public sealed class BlenderLevelParserSystem : ISystem<GameState>
{
    private const float DEFAULT_LAYER_DEPTH = 0.5f;

    private readonly World _world;
    private readonly ContentManager _content;
    private readonly MonoDreams.Component.Camera _camera;
    private readonly Dictionary<string, Texture2D> _loadedTextures = new();
    private readonly HashSet<Entity> _blenderEntities = new();

    public bool IsEnabled { get; set; } = true;

    public BlenderLevelParserSystem(World world, ContentManager content, MonoDreams.Component.Camera camera)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));

        _world.Subscribe<LoadLevelRequest>(OnLoadLevelRequest);
    }

    [Subscribe]
    private void OnLoadLevelRequest(in LoadLevelRequest request)
    {
        if (!IsEnabled) return;

        // Only handle Blender levels
        if (!request.LevelIdentifier.StartsWith("Blender_"))
            return;

        Console.WriteLine($"Loading Blender level: {request.LevelIdentifier}");

        // Clean up previous entities
        CleanupEntities();

        try
        {
            // Load JSON file from Content directory
            // The Content.RootDirectory gives us the path to the Content folder
            var jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _content.RootDirectory, "blender_level.json");
            Console.WriteLine($"Loading Blender level from: {jsonPath}");
            var jsonContent = File.ReadAllText(jsonPath);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var levelData = JsonSerializer.Deserialize<BlenderLevelData>(jsonContent, options);

            if (levelData?.Objects == null)
            {
                Console.WriteLine("No objects found in Blender level data.");
                return;
            }

            Console.WriteLine($"Loaded {levelData.Objects.Count} objects from Blender level.");

            foreach (var obj in levelData.Objects)
            {
                switch (obj.Type)
                {
                    case "CAMERA":
                        ProcessCamera(obj);
                        break;
                    case "MESH":
                        ProcessMesh(obj);
                        break;
                    case "EMPTY":
                        // Could be used for spawn points, triggers, etc.
                        Console.WriteLine($"Empty object: {obj.Name} at ({obj.Position?.X}, {obj.Position?.Y})");
                        break;
                    default:
                        Console.WriteLine($"Skipping object type: {obj.Type}");
                        break;
                }
            }

            Console.WriteLine($"Created {_blenderEntities.Count} entities from Blender level.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading Blender level: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void ProcessCamera(BlenderObject cameraObj)
    {
        if (cameraObj.Position == null) return;

        var cameraPosition = new Vector2(cameraObj.Position.X, cameraObj.Position.Y);
        _camera.Position = cameraPosition;

        // Read zoom from custom properties, default to current zoom if not specified
        if (cameraObj.CustomProperties?.TryGetValue("zoom", out var zoomValue) == true)
        {
            if (zoomValue is JsonElement jsonElement && jsonElement.TryGetDouble(out var zoom))
            {
                _camera.Zoom = (float)zoom;
                Console.WriteLine($"Set camera zoom to {zoom}");
            }
        }

        Console.WriteLine($"Set camera position to ({cameraPosition.X}, {cameraPosition.Y})");
    }

    private void ProcessMesh(BlenderObject meshObj)
    {
        if (meshObj.UvMapping == null)
        {
            Console.WriteLine($"Skipping mesh '{meshObj.Name}' - no UV mapping.");
            return;
        }

        // Extract content path from texture path
        var contentPath = ExtractContentPath(meshObj.UvMapping.TexturePath);
        if (string.IsNullOrEmpty(contentPath))
        {
            Console.WriteLine($"Skipping mesh '{meshObj.Name}' - invalid texture path: {meshObj.UvMapping.TexturePath}");
            return;
        }

        // Load texture
        Texture2D texture;
        if (!_loadedTextures.TryGetValue(contentPath, out texture))
        {
            try
            {
                texture = _content.Load<Texture2D>(contentPath);
                _loadedTextures[contentPath] = texture;
                Console.WriteLine($"Loaded texture: {contentPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load texture '{contentPath}': {ex.Message}");
                return;
            }
        }

        // Get UV coordinates from first layer
        var uvLayer = meshObj.UvMapping.UvLayers?.FirstOrDefault();
        if (uvLayer?.UvCoordinates == null || uvLayer.UvCoordinates.Count == 0)
        {
            Console.WriteLine($"Skipping mesh '{meshObj.Name}' - no UV coordinates.");
            return;
        }

        // Calculate source rectangle from UV coordinates
        var sourceRect = CalculateSourceRect(uvLayer.UvCoordinates, texture);

        // Create entity
        var entity = _world.CreateEntity();
        _blenderEntities.Add(entity);

        // Position from Blender
        var position = new Vector2(
            meshObj.Position?.X ?? 0,
            meshObj.Position?.Y ?? 0
        );

        // Transform
        entity.Set(new Transform(position));

        // SpriteInfo with calculated source rectangle
        // Use mesh dimensions from Blender for Size (already scaled), fallback to source rect if not available
        var size = new Vector2(
            meshObj.Dimensions?.X ?? sourceRect.Width,
            meshObj.Dimensions?.Y ?? sourceRect.Height
        );

        entity.Set(new SpriteInfo
        {
            SpriteSheet = texture,
            Source = sourceRect,
            Size = size,
            Color = Color.White,
            Target = RenderTargetID.Main,
            LayerDepth = DEFAULT_LAYER_DEPTH
        });

        // DrawComponent
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.Main
        });

        // Make visible
        entity.Set(new Visible());

        Console.WriteLine($"Created entity for mesh '{meshObj.Name}' at ({position.X}, {position.Y}) with size ({size.X}, {size.Y}) and source rect ({sourceRect.X}, {sourceRect.Y}, {sourceRect.Width}, {sourceRect.Height})");
    }

    /// <summary>
    /// Extracts the MonoGame Content path from a full file system path.
    /// Finds "/Content/" and returns the substring after it, without file extension.
    /// </summary>
    private static string ExtractContentPath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
            return null;

        var contentIndex = fullPath.IndexOf("/Content/", StringComparison.OrdinalIgnoreCase);
        if (contentIndex < 0)
        {
            // Try Windows-style path
            contentIndex = fullPath.IndexOf("\\Content\\", StringComparison.OrdinalIgnoreCase);
        }

        if (contentIndex < 0)
            return null;

        var separatorLength = "/Content/".Length;
        var relativePath = fullPath.Substring(contentIndex + separatorLength);

        // Remove file extension
        var extensionIndex = relativePath.LastIndexOf('.');
        if (extensionIndex > 0)
        {
            relativePath = relativePath.Substring(0, extensionIndex);
        }

        return relativePath;
    }

    /// <summary>
    /// Calculates a source rectangle from UV coordinates.
    /// Assumes a plane mesh with 4 vertices.
    /// </summary>
    private static Rectangle CalculateSourceRect(List<UvCoordinate> uvCoords, Texture2D texture)
    {
        if (uvCoords == null || uvCoords.Count == 0)
            return Rectangle.Empty;

        float minU = uvCoords.Min(uv => uv.U);
        float maxU = uvCoords.Max(uv => uv.U);
        float minV = uvCoords.Min(uv => uv.V);
        float maxV = uvCoords.Max(uv => uv.V);

        // Convert UV to pixel coordinates with rounding for better precision
        // UV origin is bottom-left, texture origin is top-left, so flip V
        int x = (int)Math.Round(minU * texture.Width);
        int y = (int)Math.Round((1 - maxV) * texture.Height);
        int width = (int)Math.Round((maxU - minU) * texture.Width);
        int height = (int)Math.Round((maxV - minV) * texture.Height);

        return new Rectangle(x, y, width, height);
    }

    private void CleanupEntities()
    {
        Console.WriteLine($"Cleaning up {_blenderEntities.Count} Blender entities...");
        foreach (var entity in _blenderEntities.ToArray())
        {
            if (entity.IsAlive)
            {
                entity.Dispose();
            }
        }
        _blenderEntities.Clear();
    }

    public void Update(GameState state)
    {
        // Processing happens via message subscription
    }

    public void Dispose()
    {
        CleanupEntities();
        _loadedTextures.Clear();
        GC.SuppressFinalize(this);
    }
}
