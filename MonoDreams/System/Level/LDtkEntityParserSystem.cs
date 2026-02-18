using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DefaultEcs;
using DefaultEcs.System;
using LDtk;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using MonoDreams.Component.Level;
using MonoDreams.Message;
using MonoDreams.State;
using Logger = MonoDreams.State.Logger;

namespace MonoDreams.System.Level
{
    /// <summary>
    /// Unified system that handles parsing of all LDtk entities, including both 
    /// regular entities (Player, NPCs) and tile entities with batching support.
    /// </summary>
    public sealed class LDtkEntityParserSystem : ISystem<GameState>
    {
        private readonly World _world;
        private readonly IDisposable _levelLoadedSubscription;
        
        public bool IsEnabled { get; set; } = true;

        public LDtkEntityParserSystem(World world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _levelLoadedSubscription = _world.SubscribeWorldComponentAdded<CurrentLevelComponent>(HandleLevelLoaded);

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
                Logger.Warning("No layer instances found in level data.");
                return;
            }

            Logger.Info($"Parsing entities for level '{levelData.Identifier}'...");
            int publishedCount = 0;

            foreach (var layer in levelData.LayerInstances)
            {
                // if (layer._Type != LayerType.Entities) continue;

                var entities = layer.EntityInstances.ToList();
                foreach (var spawnRequest in entities.Select(entityInstance => CreateEntitySpawnRequest(entityInstance, layer)))
                {
                    _world.Publish(spawnRequest);
                    publishedCount++;
                }
            }

            Logger.Info($"Finished parsing entities for level '{levelData.Identifier}'. Published {publishedCount} spawn requests.");
        }

        private static EntitySpawnRequest CreateEntitySpawnRequest(EntityInstance entityInstance, LayerInstance layer)
        {
            return new EntitySpawnRequest(
                entityInstance._Identifier,
                entityInstance.Iid.ToString(),
                new Vector2(entityInstance.Px.X, entityInstance.Px.Y),
                new Vector2(entityInstance.Width, entityInstance.Height),
                new Vector2(entityInstance._Pivot.X, entityInstance._Pivot.Y),
                new Vector2(entityInstance._Tile.X, entityInstance._Tile.Y),
                layer,
                ParseFieldInstances(entityInstance.FieldInstances)
            );
        }

        /// <summary>
        /// Parses the LDtk FieldInstance array into a more usable dictionary.
        /// </summary>
        private static Dictionary<string, object> ParseFieldInstances(FieldInstance[] fieldInstances)
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
                    Logger.Warning($"Could not parse field '{field._Identifier}' of type '{field._Type}'. Storing raw value.");
                    fields[field._Identifier] = field._Value;
                }
            }
            return fields;
        }

        /// <summary>
        /// Parses a single LDtk field value based on its type string.
        /// Handles common types. Extend as needed for more complex types (Arrays, Tiles, etc.).
        /// </summary>
        private static object ParseFieldValue(FieldInstance field)
        {
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
                    return field._Value is JsonElement jeStr ? jeStr.GetString() : field._Value.ToString();

                // Point type handling
                if (fieldType == "Point")
                {
                    if (field._Value is JsonElement jePoint)
                    {
                        if (jePoint.TryGetProperty("cx", out var cxElem) && jePoint.TryGetProperty("cy", out var cyElem))
                        {
                            return new Point(cxElem.GetInt32(), cyElem.GetInt32());
                        }
                        if (jePoint.ValueKind == JsonValueKind.Array && jePoint.GetArrayLength() == 2)
                        {
                            return new Vector2(jePoint[0].GetSingle(), jePoint[1].GetSingle());
                        }
                    }
                    Logger.Warning($"Could not parse LDtk Point field '{field._Identifier}'. Value: {field._Value}");
                    return null;
                }

                // Color type handling
                if (fieldType == "Color")
                {
                    string colorHex = field._Value.ToString();
                    if (colorHex.StartsWith("#") && colorHex.Length == 7)
                    {
                        int r = Convert.ToInt32(colorHex.Substring(1, 2), 16);
                        int g = Convert.ToInt32(colorHex.Substring(3, 2), 16);
                        int b = Convert.ToInt32(colorHex.Substring(5, 2), 16);
                        return new Color(r, g, b);
                    }
                    Logger.Warning($"Could not parse LDtk Color field '{field._Identifier}'. Value: {field._Value}");
                    return null;
                }

                Logger.Warning($"Unknown LDtk field type '{fieldType}' for field '{field._Identifier}'. Returning raw value.");
                return field._Value;
            }
            catch (Exception ex)
            {
                Logger.Error($"Exception parsing field '{field._Identifier}' of type '{fieldType}' with value '{field._Value}'.");
                return null;
            }
        }

        public void Update(GameState state)
        {
            // Processing happens on component addition event.
        }

        public void Dispose()
        {
            _levelLoadedSubscription?.Dispose();
            GC.SuppressFinalize(this);
        }

        private class TileBatch
        {
            public string TileType { get; set; }
            public List<EntityInstance> Entities { get; set; } = new();
        }
    }
}