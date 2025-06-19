using System.Text.Json;
using DefaultEcs;
using DefaultEcs.System;
using LDtk;
using Microsoft.Xna.Framework;
using MonoDreams.Examples.Component.Level;
using MonoDreams.Examples.Message.Level;
using MonoDreams.State;
using Color = Microsoft.Xna.Framework.Color;
using Point = Microsoft.Xna.Framework.Point;

namespace MonoDreams.Examples.System.Level;

/// <summary>
/// Listens for the CurrentLevelComponent being set, then iterates through the
/// LDtk level's entity instances, parses their data, and publishes
/// EntitySpawnRequest messages for each one.
/// </summary>
public sealed class LDtkEntityParserSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly IDisposable _levelLoadedSubscription;

    public bool IsEnabled { get; set; } = true;

    public LDtkEntityParserSystem(World world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));

        // Subscribe only to the addition of the level component
        _levelLoadedSubscription = _world.SubscribeWorldComponentAdded<CurrentLevelComponent>(HandleLevelLoaded);

        // Initial check in case a level is already loaded when the system starts
        if (_world.Has<CurrentLevelComponent>())
        {
            HandleLevelLoaded(_world, _world.Get<CurrentLevelComponent>());
        }
    }

    private void HandleLevelLoaded(World _, in CurrentLevelComponent currentLevelComp)
    {
        if (!IsEnabled) return;

        var levelData = currentLevelComp.LevelData;
        if (levelData?.LayerInstances == null)
        {
            Console.WriteLine($"Cannot parse entities: Level data or layer instances are null for level {levelData?.Identifier}.");
            return;
        }

        Console.WriteLine($"Parsing entities for level '{levelData.Identifier}'...");
        int publishedCount = 0;

        foreach (var layer in levelData.LayerInstances)
        {
            // Process only Entity layers
            if (layer._Type != LayerType.Entities || layer.EntityInstances == null) continue;

            foreach (var entityInstance in layer.EntityInstances)
            {
                try
                {
                    // Extract basic properties
                    string identifier = entityInstance._Identifier;
                    string instanceIid = entityInstance.Iid.ToString();
                    Vector2 position = new Vector2(entityInstance.Px.X, entityInstance.Px.Y);
                    Vector2 size = new Vector2(entityInstance.Width, entityInstance.Height);
                    Vector2 pivot = new Vector2(entityInstance._Pivot.X, entityInstance._Pivot.Y);

                    // Parse custom fields
                    var customFields = ParseFieldInstances(entityInstance.FieldInstances);

                    // Create and publish the request message
                    var request = new EntitySpawnRequest(identifier, instanceIid, position, size, pivot, customFields);
                    _world.Publish(request);
                    publishedCount++;

                    Console.WriteLine($"Published EntitySpawnRequest for '{identifier}' (IID: {instanceIid})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to parse or publish spawn request for entity instance {entityInstance?.Iid} ('{entityInstance?._Identifier}') in level {levelData.Identifier}.");
                    // Decide whether to continue parsing other entities or stop
                }
            }
        }
        Console.WriteLine($"Finished parsing entities for level '{levelData.Identifier}'. Published {publishedCount} spawn requests.");
    }

    /// <summary>
    /// Parses the LDtk FieldInstance array into a more usable dictionary.
    /// </summary>
    private Dictionary<string, object> ParseFieldInstances(FieldInstance[] fieldInstances)
    {
        var fields = new Dictionary<string, object>();
        if (fieldInstances == null) return fields;

        foreach (var field in fieldInstances)
        {
            if (field == null || string.IsNullOrEmpty(field._Identifier)) continue;

            try
            {
                object parsedValue = ParseFieldValue(field);
                fields[field._Identifier] = parsedValue;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not parse field '{field._Identifier}' of type '{field._Type}'. Storing raw value.");
                // Store raw value as fallback? Or null? Depends on requirements.
                fields[field._Identifier] = field._Value;
            }
        }
        return fields;
    }

    /// <summary>
    /// Parses a single LDtk field value based on its type string.
    /// Handles common types. Extend as needed for more complex types (Arrays, Tiles, etc.).
    /// </summary>
    private object ParseFieldValue(FieldInstance field)
    {
        // LDtk type strings look like "Int", "Float", "Bool", "String", "Point", "Enum(...)", "FilePath", "Color", etc.
        // The Value property holds the data, often requiring casting or parsing.

        // if (field._Value is null) return null;

        // Use pattern matching or StartsWith for clarity
        string fieldType = field._Type;

        try
        {
            // Basic Types (handle potential JsonElement boxing)
            if (fieldType == "Int")
                return field._Value is JsonElement jeInt ? jeInt.GetInt32() : Convert.ToInt32(field._Value);
            if (fieldType == "Float")
                return field._Value is JsonElement jeFloat ? jeFloat.GetSingle() : Convert.ToSingle(field._Value);
            if (fieldType == "Bool")
                return field._Value is JsonElement jeBool ? jeBool.GetBoolean() : Convert.ToBoolean(field._Value);
            if (fieldType == "String" || fieldType == "Multiline" || fieldType == "FilePath" || fieldType.StartsWith("Enum("))
                return field._Value is JsonElement jeStr ? jeStr.GetString() : field._Value.ToString(); // Enums are stored as string values

            // Point type (stored as an object/dictionary or array in LDtk JSON, LDtkMonogame might parse it)
            // LDtkMonogame often parses Point fields directly into a Point object if possible.
            // Check if it's already parsed, otherwise parse manually if structure is known.
            if (fieldType == "Point")
            {
                // If LDtkMonogame already parsed it to a Point struct/class:
                // if (field._Value is Point ldtkPoint) return ldtkPoint;
                // if (field._Value is System.Drawing.Point sysPoint) return new Vector2(sysPoint.X, sysPoint.Y); // Adapt if using System.Drawing

                // Manual parsing if it's still JsonElement or similar
                if (field._Value is JsonElement jePoint)
                {
                    // Check if it's an object { "cx": int, "cy": int } used by LDtk grid points
                    if (jePoint.TryGetProperty("cx", out var cxElem) && jePoint.TryGetProperty("cy", out var cyElem))
                    {
                        // These are CELL coordinates, might need multiplying by grid size depending on use case.
                        // Returning cell coords for now.
                        return new Point(cxElem.GetInt32(), cyElem.GetInt32());
                    }
                    // Check if it's an array [x, y] - less common for Point fields now
                    if (jePoint.ValueKind == JsonValueKind.Array && jePoint.GetArrayLength() == 2)
                    {
                        return new Vector2(jePoint[0].GetSingle(), jePoint[1].GetSingle());
                    }
                }
                Console.WriteLine($"Could not parse LDtk Point field '{field._Identifier}'. Value: {field._Value}");
                return null; // Or return the raw value
            }

            // Color type (string like "#RRGGBB")
            if (fieldType == "Color")
            {
                string colorHex = field._Value.ToString();
                // Basic Hex Color Parsing (assumes #RRGGBB)
                if (colorHex.StartsWith("#") && colorHex.Length == 7)
                {
                    int r = Convert.ToInt32(colorHex.Substring(1, 2), 16);
                    int g = Convert.ToInt32(colorHex.Substring(3, 2), 16);
                    int b = Convert.ToInt32(colorHex.Substring(5, 2), 16);
                    return new Color(r, g, b);
                }
                Console.WriteLine($"Could not parse LDtk Color field '{field._Identifier}'. Value: {field._Value}");
                return null; // Or default color / raw value
            }

            // Add parsing for other types like Arrays, Tile references if needed

            // Fallback for unknown types
            Console.WriteLine($"Unknown LDtk field type '{fieldType}' for field '{field._Identifier}'. Returning raw value.");
            return field._Value;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception parsing field '{field._Identifier}' of type '{fieldType}' with value '{field._Value}'.");
            return null; // Return null on parsing error
        }
    }


    public void Update(GameState state)
    {
        // Processing happens on component addition event.
    }

    public void Dispose()
    {
        // Dispose subscription to prevent leaks
        _levelLoadedSubscription?.Dispose();
        GC.SuppressFinalize(this);
    }
}