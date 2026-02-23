using System.Text.Json;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Physics;
using MonoDreams.Component.Draw;
using MonoDreams.Level;
using MonoDreams.Message.Level;
using MonoDreams.Draw;
using MonoDreams.Extension;
using MonoDreams.State;
using Logger = MonoDreams.State.Logger;

namespace MonoDreams.System.Level;

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
    private readonly Dictionary<string, Entity> _nameToEntity = new();
    private readonly Dictionary<string, Action<Entity, BlenderObject>> _collectionHandlers = new();
    private DrawLayerMap _drawLayerMap;

    public bool IsEnabled { get; set; } = true;

    public BlenderLevelParserSystem(World world, ContentManager content, MonoDreams.Component.Camera camera)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));

        _world.Subscribe<LoadLevelRequest>(OnLoadLevelRequest);
    }

    /// <summary>
    /// Registers a handler that will be called for entities belonging to the specified Blender collection.
    /// This allows game code to add game-specific components to entities created by the core parser.
    /// </summary>
    public void RegisterCollectionHandler(string collectionName, Action<Entity, BlenderObject> handler)
    {
        _collectionHandlers[collectionName] = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>
    /// Sets the draw layer map used for depth resolution. When set, mesh depth is resolved by:
    /// 1. Object's "drawLayer" custom property (string matching a layer name)
    /// 2. Blender collection names matched against layer names
    /// 3. Falls back to DEFAULT_LAYER_DEPTH
    /// </summary>
    public void SetDrawLayerMap(DrawLayerMap drawLayerMap)
    {
        _drawLayerMap = drawLayerMap ?? throw new ArgumentNullException(nameof(drawLayerMap));
    }

    [Subscribe]
    private void OnLoadLevelRequest(in LoadLevelRequest request)
    {
        if (!IsEnabled) return;

        // Only handle Blender levels
        if (!request.LevelIdentifier.StartsWith("Blender_"))
            return;

        Logger.Info($"Loading Blender level: {request.LevelIdentifier}");

        // Clean up previous entities
        CleanupEntities();

        try
        {
            // Load JSON file from Content directory
            // The Content.RootDirectory gives us the path to the Content folder
            var jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _content.RootDirectory, "blender_level.json");
            Logger.Debug($"Loading Blender level from: {jsonPath}");
            var jsonContent = File.ReadAllText(jsonPath);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var levelData = JsonSerializer.Deserialize<BlenderLevelData>(jsonContent, options);

            if (levelData?.Objects == null)
            {
                Logger.Warning("No objects found in Blender level data.");
                return;
            }

            Logger.Info($"Loaded {levelData.Objects.Count} objects from Blender level.");

            // Pre-scan: identify collider children (names ending with "-collider" that have a parent)
            var colliderChildMap = new Dictionary<string, BlenderObject>();   // parent name → collider data
            var colliderChildNames = new HashSet<string>();
            foreach (var obj in levelData.Objects)
            {
                if (obj.Name.EndsWith("-collider") && !string.IsNullOrEmpty(obj.Parent))
                {
                    colliderChildMap[obj.Parent] = obj;
                    colliderChildNames.Add(obj.Name);
                    Logger.Debug($"Found collider child '{obj.Name}' for parent '{obj.Parent}'.");
                }
            }

            // Pass 1: Create all entities (skip collider children)
            foreach (var obj in levelData.Objects)
            {
                if (colliderChildNames.Contains(obj.Name))
                {
                    Logger.Debug($"Skipping collider child '{obj.Name}' (will be applied to parent).");
                    continue;
                }

                switch (obj.Type)
                {
                    case "CAMERA":
                        ProcessCamera(obj, levelData.ScaleFactor);
                        break;
                    case "MESH":
                    case "GREASEPENCIL":
                        ProcessMesh(obj, levelData);
                        break;
                    case "EMPTY":
                        ProcessEmpty(obj);
                        break;
                    default:
                        Logger.Warning($"Skipping unknown object type: {obj.Type}");
                        break;
                }
            }

            // Post-Pass 1: Apply collider children to parent entities
            foreach (var (parentName, colliderObj) in colliderChildMap)
            {
                ApplyColliderChild(parentName, colliderObj);
            }

            // Pass 2: Set up parent-child relationships
            foreach (var obj in levelData.Objects)
            {
                if (string.IsNullOrEmpty(obj.Parent)) continue;
                if (colliderChildNames.Contains(obj.Name)) continue;

                if (!_nameToEntity.TryGetValue(obj.Name, out var childEntity))
                {
                    Logger.Warning($"Parent linkage: child '{obj.Name}' not found in entity map.");
                    continue;
                }
                if (!_nameToEntity.TryGetValue(obj.Parent, out var parentEntity))
                {
                    Logger.Warning($"Parent linkage: parent '{obj.Parent}' not found for child '{obj.Name}'.");
                    continue;
                }

                childEntity.SetParent(parentEntity);
                Logger.Debug($"Set parent of '{obj.Name}' to '{obj.Parent}'.");
            }

            // Post-Pass 2: Propagate YSortOffset from parents to sprite children
            foreach (var obj in levelData.Objects)
            {
                if (string.IsNullOrEmpty(obj.Parent)) continue;
                if (colliderChildNames.Contains(obj.Name)) continue;

                if (!_nameToEntity.TryGetValue(obj.Name, out var childEntity)) continue;
                if (!_nameToEntity.TryGetValue(obj.Parent, out var parentEntity)) continue;

                if (childEntity.Has<SpriteInfo>() && parentEntity.Has<SpriteInfo>())
                {
                    ref readonly var parentSprite = ref parentEntity.Get<SpriteInfo>();
                    if (parentSprite.YSortOffset != 0f)
                    {
                        ref var childSprite = ref childEntity.Get<SpriteInfo>();
                        childSprite.YSortOffset = parentSprite.YSortOffset;
                        Logger.Debug($"Propagated YSortOffset={parentSprite.YSortOffset} from '{obj.Parent}' to '{obj.Name}'.");
                    }
                }
            }

            Logger.Info($"Created {_blenderEntities.Count} entities from Blender level.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Error loading Blender level: {ex.Message}\n{ex.StackTrace}");
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
                Logger.Debug($"Set camera zoom to {gameZoom} (from Blender ortho_scale {orthoScale}, scaleFactor {scaleFactor})");
            }
        }

        Logger.Debug($"Set camera position to ({cameraPosition.X}, {cameraPosition.Y})");
    }

    private void ProcessEmpty(BlenderObject emptyObj)
    {
        var entity = _world.CreateEntity();
        _blenderEntities.Add(entity);

        var position = new Vector2(
            emptyObj.Position?.X ?? 0,
            emptyObj.Position?.Y ?? 0
        );

        entity.Set(new Transform(position));
        entity.Set(new EntityInfo("Empty", emptyObj.Name));

        _nameToEntity[emptyObj.Name] = entity;

        // Invoke registered game-specific collection handlers
        if (emptyObj.Collections != null)
        {
            foreach (var collection in emptyObj.Collections)
            {
                if (_collectionHandlers.TryGetValue(collection, out var handler))
                {
                    handler(entity, emptyObj);
                }
            }
        }

        Logger.Debug($"Created empty entity '{emptyObj.Name}' at ({position.X}, {position.Y})");
    }

    private void ProcessMesh(BlenderObject meshObj, BlenderLevelData levelData)
    {
        if (meshObj.UvMapping == null)
        {
            Logger.Warning($"Skipping mesh '{meshObj.Name}' - no UV mapping.");
            return;
        }

        // Extract content path from texture path
        var contentPath = ExtractContentPath(meshObj.UvMapping.TexturePath);
        if (string.IsNullOrEmpty(contentPath))
        {
            Logger.Warning($"Skipping mesh '{meshObj.Name}' - invalid texture path: {meshObj.UvMapping.TexturePath}");
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
                Logger.Debug($"Loaded texture: {contentPath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load texture '{contentPath}': {ex.Message}");
                return;
            }
        }

        // Get UV coordinates from first layer
        var uvLayer = meshObj.UvMapping.UvLayers?.FirstOrDefault();
        if (uvLayer?.UvCoordinates == null || uvLayer.UvCoordinates.Count == 0)
        {
            Logger.Warning($"Skipping mesh '{meshObj.Name}' - no UV coordinates.");
            return;
        }

        // Calculate source rectangle from UV coordinates
        var sourceRect = CalculateSourceRect(uvLayer.UvCoordinates, texture);

        // Create entity
        var entity = _world.CreateEntity();
        _blenderEntities.Add(entity);
        _nameToEntity[meshObj.Name] = entity;

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
            LayerDepth = ResolveLayerDepth(meshObj, levelData),
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

        Logger.Debug($"Created entity for mesh '{meshObj.Name}' at ({position.X}, {position.Y}) with size ({size.X}, {size.Y}), origin ({origin.X}, {origin.Y}), and source rect ({sourceRect.X}, {sourceRect.Y}, {sourceRect.Width}, {sourceRect.Height})");
    }

    /// <summary>
    /// Resolves the layer depth for a mesh object using the DrawLayerMap (if set).
    /// Priority: object "drawLayer" custom property > collection name match (with hierarchy walk) > DEFAULT_LAYER_DEPTH.
    /// </summary>
    private float ResolveLayerDepth(BlenderObject meshObj, BlenderLevelData levelData)
    {
        if (_drawLayerMap == null)
            return DEFAULT_LAYER_DEPTH;

        // 1. Check object's "drawLayer" custom property
        if (meshObj.CustomProperties?.TryGetValue("drawLayer", out var drawLayerValue) == true)
        {
            var layerName = drawLayerValue is JsonElement jsonEl
                ? jsonEl.GetString()
                : drawLayerValue?.ToString();

            if (layerName != null && _drawLayerMap.TryGetDepth(layerName, out var depth))
                return depth;
        }

        // 2. Try each Blender collection name, then walk up the collection hierarchy
        if (meshObj.Collections != null)
        {
            foreach (var collection in meshObj.Collections)
            {
                var current = collection;
                while (current != null)
                {
                    if (_drawLayerMap.TryGetDepth(current, out var depth))
                        return depth;

                    // Walk up the hierarchy if available
                    if (levelData.CollectionHierarchy == null ||
                        !levelData.CollectionHierarchy.TryGetValue(current, out current))
                        break;
                }
            }
        }

        // 3. Fall back to default
        return DEFAULT_LAYER_DEPTH;
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

        // Compute visual offset from origin so collision boxes align with rendered sprites.
        // SpriteBatch.Draw renders at: position - origin * scale
        // In destination space: offset = -(size * originOffset)
        var originOffsetX = meshObj.OriginOffset?.X ?? 0.5f;
        var originOffsetY = meshObj.OriginOffset?.Y ?? 0.5f;
        // Compute float edges of visual bounds relative to entity position
        float left = -size.X * originOffsetX;
        float top = -size.Y * (1 - originOffsetY);  // Y inverted: Blender Y=0 is bottom
        // Snap to smallest enclosing integer rectangle (floor expands left/top, ceiling expands right/bottom)
        int intLeft = (int)Math.Floor(left);
        int intTop = (int)Math.Floor(top);
        int intRight = (int)Math.Ceiling(left + size.X);
        int intBottom = (int)Math.Ceiling(top + size.Y);

        var boundsOffset = new Point(intLeft, intTop);
        var boundsSize = new Point(intRight - intLeft, intBottom - intTop);

        // Check for Collision collection
        if (meshObj.Collections.Contains("Collision"))
        {
            entity.Set(new EntityInfo("Collision", meshObj.Name));

            // Get collision layer from collection properties (default to -1 for all layers)
            int layer = -1;
            bool passive = false;

            if (meshObj.CollectionProperties?.TryGetValue("Collision", out var collisionProps) == true)
            {
                layer = GetIntProperty(collisionProps, "layer", -1);
                passive = GetBoolProperty(collisionProps, "passive", false);
            }

            var bounds = new Rectangle(boundsOffset, boundsSize);
            var activeLayers = new HashSet<int> { layer };
            entity.Set(new BoxCollider(bounds, activeLayers, passive));

            Logger.Debug($"Added BoxCollider to '{meshObj.Name}' (layer: {layer}, passive: {passive})");
        }

        // Check for Player collection
        if (meshObj.Collections.Contains("Player"))
        {
            entity.Set(new EntityInfo("Player", meshObj.Name));
            entity.Set(new BoxCollider(new Rectangle(boundsOffset, boundsSize)));
            entity.Set(new RigidBody());
            entity.Set(new Velocity());

            // Add camera follow target component
            entity.Set(new CameraFollowTarget
            {
                DampingX = 5.0f,
                DampingY = 5.0f,
                MaxDistanceX = 150.0f,
                MaxDistanceY = 100.0f,
                IsActive = true
            });
            Logger.Debug($"Set Player for '{meshObj.Name}'");
        }

        // Check for Enemy collection
        if (meshObj.Collections.Contains("Enemy"))
        {
            entity.Set(new EntityInfo("Enemy", meshObj.Name));
            Logger.Debug($"Set Enemy for '{meshObj.Name}'");
        }

        // Check for Trigger collection (collision but no physics push)
        if (meshObj.Collections.Contains("Trigger"))
        {
            if (!entity.Has<BoxCollider>())
            {
                var bounds = new Rectangle(boundsOffset, boundsSize);
                entity.Set(new BoxCollider(bounds, passive: true));
            }
            entity.Set(new EntityInfo("Trigger", meshObj.Name));
            Logger.Debug($"Set as Trigger zone for '{meshObj.Name}'");
        }

        // Invoke registered game-specific collection handlers
        foreach (var collection in meshObj.Collections)
        {
            if (_collectionHandlers.TryGetValue(collection, out var handler))
            {
                handler(entity, meshObj);
            }
        }
    }

    /// <summary>
    /// Applies a collider child's mesh vertices as a ConvexCollider on the parent entity.
    /// Replaces any existing BoxCollider (preserving its layer/passive settings).
    /// Sets YSortOffset on the parent's SpriteInfo and adds ColliderTag.
    /// </summary>
    private void ApplyColliderChild(string parentName, BlenderObject colliderObj)
    {
        if (!_nameToEntity.TryGetValue(parentName, out var parentEntity))
        {
            Logger.Warning($"Collider child '{colliderObj.Name}': parent entity '{parentName}' not found.");
            return;
        }

        if (colliderObj.Vertices == null || colliderObj.Vertices.Count < 3)
        {
            Logger.Warning($"Collider child '{colliderObj.Name}': needs at least 3 vertices, got {colliderObj.Vertices?.Count ?? 0}.");
            return;
        }

        // Build model vertices from exported data
        var modelVertices = new Vector2[colliderObj.Vertices.Count];
        for (var i = 0; i < colliderObj.Vertices.Count; i++)
        {
            modelVertices[i] = new Vector2(colliderObj.Vertices[i].X, colliderObj.Vertices[i].Y);
        }

        // Guard against degenerate shapes (e.g. all vertices collinear or on the same axis)
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        foreach (var v in modelVertices)
        {
            if (v.X < minX) minX = v.X;
            if (v.X > maxX) maxX = v.X;
            if (v.Y < minY) minY = v.Y;
            if (v.Y > maxY) maxY = v.Y;
        }
        if (maxX - minX < 0.01f || maxY - minY < 0.01f)
        {
            Logger.Warning($"Collider child '{colliderObj.Name}': degenerate shape (width={maxX - minX}, height={maxY - minY}), skipping.");
            return;
        }

        // Determine collision layer and passive flag
        HashSet<int> activeLayers = null;
        bool passive = false;

        // If parent already has a BoxCollider (from ProcessCollections), inherit its settings and remove it
        if (parentEntity.Has<BoxCollider>())
        {
            ref readonly var existingBox = ref parentEntity.Get<BoxCollider>();
            activeLayers = existingBox.ActiveLayers;
            passive = existingBox.Passive;
            parentEntity.Remove<BoxCollider>();
            Logger.Debug($"Replaced BoxCollider on '{parentName}' with ConvexCollider from '{colliderObj.Name}'.");
        }
        else
        {
            // Check collider child's own collection properties for collision settings
            if (colliderObj.CollectionProperties != null)
            {
                foreach (var (_, props) in colliderObj.CollectionProperties)
                {
                    var layer = GetIntProperty(props, "layer", -1);
                    passive = GetBoolProperty(props, "passive", false);
                    activeLayers = new HashSet<int> { layer };
                    break;
                }
            }
        }

        // Create ConvexCollider with rotation ignored (baked into model vertices)
        parentEntity.Set(new ConvexCollider(modelVertices, activeLayers, passive, ignoreTransformRotation: true));

        // Set YSortOffset = max Y of model vertices (bottom of collider in Y-down coords)
        // maxY was already computed in the degenerate-shape guard above
        if (parentEntity.Has<SpriteInfo>())
        {
            ref var spriteInfo = ref parentEntity.Get<SpriteInfo>();
            spriteInfo.YSortOffset = maxY;
        }

        // Add ColliderTag
        parentEntity.Set(new ColliderTag());

        Logger.Debug($"Applied ConvexCollider from '{colliderObj.Name}' to '{parentName}' ({modelVertices.Length} vertices, YSortOffset={maxY}).");
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
        Logger.Info($"Cleaning up {_blenderEntities.Count} Blender entities...");
        foreach (var entity in _blenderEntities.ToArray())
        {
            if (entity.IsAlive)
            {
                entity.Dispose();
            }
        }
        _blenderEntities.Clear();
        _nameToEntity.Clear();
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
