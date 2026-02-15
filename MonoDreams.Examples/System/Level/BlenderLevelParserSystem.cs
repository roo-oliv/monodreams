using System.Text.Json;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Input;
using MonoDreams.Component.Physics;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Camera;
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
                        ProcessCamera(obj, levelData.ScaleFactor);
                        break;
                    case "MESH":
                    case "GREASEPENCIL":
                        // GREASEPENCIL objects are rendered to PNG and exported as planes
                        // They are processed the same way as MESH objects
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

    private void ProcessCamera(BlenderObject cameraObj, float scaleFactor)
    {
        if (cameraObj.Position == null) return;

        var cameraPosition = new Vector2(cameraObj.Position.X, cameraObj.Position.Y);
        _camera.Position = cameraPosition;

        // Read ortho_scale from custom properties and convert to game zoom
        // Blender ortho_scale = visible width in Blender units
        // With scaleFactor, visible world pixels = ortho_scale * scaleFactor
        // Game zoom = virtualWidth / visible_world_pixels
        if (cameraObj.CustomProperties?.TryGetValue("zoom", out var zoomValue) == true)
        {
            if (zoomValue is JsonElement jsonElement && jsonElement.TryGetDouble(out var orthoScale))
            {
                // Convert Blender ortho_scale to game zoom
                var visibleWorldWidth = orthoScale * scaleFactor;
                var gameZoom = _camera.VirtualWidth / visibleWorldWidth;
                _camera.Zoom = (float)gameZoom;
                Console.WriteLine($"Set camera zoom to {gameZoom} (from Blender ortho_scale {orthoScale}, scaleFactor {scaleFactor})");
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

        // Calculate origin from Blender's origin offset
        // Blender origin offset is normalized (0-1) where (0,0) is bottom-left and (0.5,0.5) is center
        // SpriteBatch.Draw expects origin in SOURCE TEXTURE coordinates (not destination size)
        var originOffsetX = meshObj.OriginOffset?.X ?? 0.5f;  // Default to center
        var originOffsetY = meshObj.OriginOffset?.Y ?? 0.5f;
        // Y is inverted: Blender Y=0 is bottom, engine Y=0 is top
        // Use sourceRect dimensions since SpriteBatch.Draw origin is in source coordinates
        var origin = new Vector2(sourceRect.Width * originOffsetX, sourceRect.Height * (1 - originOffsetY));

        entity.Set(new SpriteInfo
        {
            SpriteSheet = texture,
            Source = sourceRect,
            Size = size,
            Color = Color.White,
            Target = RenderTargetID.Main,
            LayerDepth = DEFAULT_LAYER_DEPTH,
            Origin = origin
        });

        // DrawComponent
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = RenderTargetID.Main
        });

        // Make visible
        entity.Set(new Visible());

        // Process collection-based components
        ProcessCollections(entity, meshObj, size);

        Console.WriteLine($"Created entity for mesh '{meshObj.Name}' at ({position.X}, {position.Y}) with size ({size.X}, {size.Y}), origin ({origin.X}, {origin.Y}), and source rect ({sourceRect.X}, {sourceRect.Y}, {sourceRect.Width}, {sourceRect.Height})");
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

    /// <summary>
    /// Processes Blender collection membership and adds appropriate components.
    /// Collection names determine entity behavior (e.g., "Collision" adds BoxCollider).
    /// Collection custom properties can configure component settings.
    /// </summary>
    private void ProcessCollections(Entity entity, BlenderObject meshObj, Vector2 size)
    {
        if (meshObj.Collections == null || meshObj.Collections.Count == 0)
            return;

        // Check for Collision collection
        if (meshObj.Collections.Contains("Collision"))
        {
            entity.Set(new EntityInfo(EntityType.Tile));
                
            // Get collision layer from collection properties (default to -1 for all layers)
            int layer = -1;
            bool passive = false;

            if (meshObj.CollectionProperties?.TryGetValue("Collision", out var collisionProps) == true)
            {
                layer = GetIntProperty(collisionProps, "layer", -1);
                passive = GetBoolProperty(collisionProps, "passive", false);
            }

            var bounds = new Rectangle(0, 0, (int)size.X, (int)size.Y);
            var activeLayers = new HashSet<int> { layer };
            entity.Set(new BoxCollider(bounds, activeLayers, passive));

            Console.WriteLine($"Added BoxCollider to '{meshObj.Name}' (layer: {layer}, passive: {passive})");
        }

        // Check for Player collection
        if (meshObj.Collections.Contains("Player"))
        {
            entity.Set(new EntityInfo(EntityType.Player));
            entity.Set(new PlayerState());
            entity.Set(new InputControlled());
            // entity.Set(new BoxCollider(new Rectangle(Constants.PlayerOffset.ToPoint(), Constants.PlayerSize)));
            entity.Set(new BoxCollider(new Rectangle(Point.Zero, new Point(1, 1))));
            entity.Set(new RigidBody());
            entity.Set(new Velocity());

            // Add camera follow target component
            entity.Set(new CameraFollowTarget
            {
                DampingX = 5.0f,  // Adjust for desired smoothness
                DampingY = 5.0f,
                MaxDistanceX = 150.0f,  // Maximum distance camera can lag behind
                MaxDistanceY = 100.0f,
                IsActive = true
            });
            Console.WriteLine($"Set EntityType.Player for '{meshObj.Name}'");
        }

        // Check for Enemy collection
        if (meshObj.Collections.Contains("Enemy"))
        {
            entity.Set(new EntityInfo(EntityType.Enemy));
            Console.WriteLine($"Set EntityType.Enemy for '{meshObj.Name}'");
        }

        // Check for Trigger collection (collision but no physics push)
        if (meshObj.Collections.Contains("Trigger"))
        {
            if (!entity.Has<BoxCollider>())
            {
                var bounds = new Rectangle(0, 0, (int)size.X, (int)size.Y);
                entity.Set(new BoxCollider(bounds, passive: true));
            }
            entity.Set(new EntityInfo(EntityType.Zone));
            Console.WriteLine($"Set as Trigger zone for '{meshObj.Name}'");
        }
    }

    /// <summary>
    /// Gets an integer property from collection properties dictionary.
    /// </summary>
    private static int GetIntProperty(Dictionary<string, object> props, string key, int defaultValue)
    {
        if (props == null || !props.TryGetValue(key, out var value))
            return defaultValue;

        if (value is JsonElement jsonElement)
        {
            if (jsonElement.TryGetInt32(out var intVal))
                return intVal;
            if (jsonElement.TryGetDouble(out var doubleVal))
                return (int)doubleVal;
        }

        if (value is int intValue)
            return intValue;
        if (value is double doubleValue)
            return (int)doubleValue;
        if (value is float floatValue)
            return (int)floatValue;

        return defaultValue;
    }

    /// <summary>
    /// Gets a boolean property from collection properties dictionary.
    /// </summary>
    private static bool GetBoolProperty(Dictionary<string, object> props, string key, bool defaultValue)
    {
        if (props == null || !props.TryGetValue(key, out var value))
            return defaultValue;

        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.True)
                return true;
            if (jsonElement.ValueKind == JsonValueKind.False)
                return false;
        }

        if (value is bool boolValue)
            return boolValue;

        return defaultValue;
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
