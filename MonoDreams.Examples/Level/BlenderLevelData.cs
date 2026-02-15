using System.Text.Json.Serialization;

namespace MonoDreams.Examples.Level;

/// <summary>
/// Root data structure for Blender-exported level JSON files.
/// </summary>
public class BlenderLevelData
{
    [JsonPropertyName("version")]
    public string Version { get; set; }

    [JsonPropertyName("exportedFrom")]
    public string ExportedFrom { get; set; }

    [JsonPropertyName("scaleFactor")]
    public float ScaleFactor { get; set; }

    [JsonPropertyName("objects")]
    public List<BlenderObject> Objects { get; set; }
}

/// <summary>
/// Represents an object exported from Blender (mesh, camera, light, empty).
/// </summary>
public class BlenderObject
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } // MESH, CAMERA, EMPTY, LIGHT, GREASEPENCIL

    [JsonPropertyName("collections")]
    public List<string> Collections { get; set; }

    [JsonPropertyName("collectionProperties")]
    public Dictionary<string, Dictionary<string, object>> CollectionProperties { get; set; }

    [JsonPropertyName("meshType")]
    public string MeshType { get; set; } // plane, cube, or custom mesh name

    [JsonPropertyName("position")]
    public Vector2Json Position { get; set; }

    [JsonPropertyName("dimensions")]
    public Vector2Json Dimensions { get; set; }

    [JsonPropertyName("scale")]
    public Vector2Json Scale { get; set; }

    [JsonPropertyName("rotation")]
    public float Rotation { get; set; }

    [JsonPropertyName("originOffset")]
    public Vector2Json OriginOffset { get; set; }

    [JsonPropertyName("customProperties")]
    public Dictionary<string, object> CustomProperties { get; set; }

    [JsonPropertyName("uvMapping")]
    public UvMapping UvMapping { get; set; }
}

/// <summary>
/// UV mapping data for a mesh object.
/// </summary>
public class UvMapping
{
    [JsonPropertyName("texturePath")]
    public string TexturePath { get; set; }

    [JsonPropertyName("uvLayers")]
    public List<UvLayer> UvLayers { get; set; }
}

/// <summary>
/// A single UV layer containing vertex UV coordinates.
/// </summary>
public class UvLayer
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("uvCoordinates")]
    public List<UvCoordinate> UvCoordinates { get; set; }
}

/// <summary>
/// UV coordinate for a single vertex.
/// </summary>
public class UvCoordinate
{
    [JsonPropertyName("vertexIndex")]
    public int VertexIndex { get; set; }

    [JsonPropertyName("u")]
    public float U { get; set; }

    [JsonPropertyName("v")]
    public float V { get; set; }
}

/// <summary>
/// Simple 2D vector for JSON deserialization.
/// </summary>
public class Vector2Json
{
    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }
}
