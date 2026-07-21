#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// The <b>single canonical JSON policy</b> every native MonoDreams file is written and read through:
/// scenes today (<see cref="SceneWriter"/> / <see cref="EngineComponentSerializers"/> /
/// <c>SceneReaderSystem</c>) and the project manifest later (PS2 reuses this helper). "Canonical"
/// means the produced bytes are <b>deterministic</b>: the same in-memory value serializes to the
/// exact same bytes on every run and on every machine, so a <c>.mdscene</c> diff is meaningful and a
/// git merge is tractable — <c>serialize(world)</c> is byte-identical across runs and
/// <c>load → save</c> equals the source file byte-for-byte.
///
/// <para>What makes it canonical (net8.0 System.Text.Json):</para>
/// <list type="bullet">
///   <item><b>Stable property order.</b> Strongly-typed DTOs (<see cref="SceneData"/>, the component
///   DTOs, later the manifest) serialize their properties in declaration order — deterministic across
///   runs for a given build. Open <c>Dictionary&lt;string,T&gt;</c> maps (the entity
///   <c>components{}</c> map) are NOT ordered by STJ by default (insertion order leaks the live
///   component-storage order), so <see cref="SortedStringKeyDictionaryConverterFactory"/> writes their
///   keys in <see cref="StringComparer.Ordinal"/> order.</item>
///   <item><b>Invariant, round-trippable floats.</b> STJ writes JSON numbers with the shortest
///   round-trippable representation and is culture-invariant by construction — it never consults
///   <c>CultureInfo.CurrentCulture</c>, so a locale with comma decimals still emits <c>0.1</c>, not
///   <c>0,1</c>. (It normalizes <c>1.0f</c> to <c>1</c>; that still round-trips to the same float and
///   re-serializes identically, so the fixed point holds.)</item>
///   <item><b>Indented</b> (2-space, LF newlines — net8.0's <c>Utf8JsonWriter</c> hardcodes the
///   indentation newline to <c>\n</c>, so indentation is platform-independent) so numeric arrays put
///   one value per line and a one-field edit is a one-line diff.</item>
///   <item><b>Null fields are omitted</b> (<see cref="JsonIgnoreCondition.WhenWritingNull"/>) — an
///   absent <c>camera</c>, a root's null <c>parent</c>, a null <c>assetKey</c> disappear rather than
///   emitting <c>"…": null</c> noise.</item>
///   <item><b>Trailing newline.</b> <see cref="Serialize{T}"/> appends a single <c>\n</c> (the POSIX
///   text-file convention) so the file ends cleanly; STJ tolerates the trailing whitespace on read, so
///   the fixed point is preserved.</item>
/// </list>
///
/// <para>All scene serialization MUST flow through here (never a bare
/// <c>new JsonSerializerOptions { WriteIndented = true }</c>), so the whole file — including each
/// component body produced by <see cref="EngineComponentSerializers"/> via
/// <see cref="SerializeToElement{T}"/> — obeys one policy.</para>
/// </summary>
public static class CanonicalJson
{
    /// <summary>The one shared options instance. Immutable after construction (STJ freezes an options
    /// instance on first use); never mutate it — build a new one if a future policy needs to differ.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Respect the DTOs' [JsonPropertyName] attributes verbatim (no camelCase/PascalCase remap).
        PropertyNamingPolicy = null,
        // Omit null-valued fields so absent optionals leave no "…": null churn in the diff.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new SortedStringKeyDictionaryConverterFactory() },
    };

    /// <summary>Serializes <paramref name="value"/> to canonical, indented JSON with a trailing
    /// newline. Deterministic: the same value always yields the same bytes.</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options) + "\n";

    /// <summary>Serializes <paramref name="value"/> to a <see cref="JsonElement"/> under the canonical
    /// policy — the form <see cref="EngineComponentSerializers"/> stores as each component body, so a
    /// component's fields obey the same null-omission / float / key-order rules as the enclosing file.</summary>
    public static JsonElement SerializeToElement<T>(T value) => JsonSerializer.SerializeToElement(value, Options);

    /// <summary>Deserializes canonical JSON. Reads through the same options (tolerating the trailing
    /// newline and the sorted-map converter's read path).</summary>
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

    /// <summary>Re-serializes a <see cref="JsonElement"/> through the canonical policy to a normalized
    /// string. Two <see cref="JsonElement"/>s produced by the canonical writer (each a component body
    /// from <see cref="SerializeToElement{T}"/> or parsed from a canonical file) canonicalize to the
    /// same string iff they carry the same logical value — the reliable equality the diff-based prefab
    /// override detection needs (pre-mortem #1: nondeterministic bytes would turn an inherited component
    /// into a phantom override).</summary>
    public static string Canonicalize(JsonElement element) => JsonSerializer.Serialize(element, Options);

    /// <summary>Whether two component bodies are byte-equal under the canonical policy (see
    /// <see cref="Canonicalize"/>) — the prefab override test: an instance component whose canonical
    /// bytes EQUAL the prefab root's same-key bytes is <b>inherited</b> (omitted), byte-different is an
    /// <b>override</b> (kept).</summary>
    public static bool CanonicalEquals(JsonElement a, JsonElement b) => Canonicalize(a) == Canonicalize(b);
}

/// <summary>
/// Writes any <c>Dictionary&lt;string, TValue&gt;</c> with its keys in <see cref="StringComparer.Ordinal"/>
/// order, so an open string-keyed map (the scene entity <c>components{}</c> map, or any manifest map)
/// is byte-deterministic regardless of the runtime insertion order. Reads are order-agnostic (a plain
/// object read into the dictionary). Registered once on <see cref="CanonicalJson.Options"/>.
/// </summary>
public sealed class SortedStringKeyDictionaryConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() == typeof(Dictionary<,>)
        && typeToConvert.GetGenericArguments()[0] == typeof(string);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[1];
        var converterType = typeof(SortedStringKeyDictionaryConverter<>).MakeGenericType(valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class SortedStringKeyDictionaryConverter<TValue> : JsonConverter<Dictionary<string, TValue>>
    {
        public override Dictionary<string, TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
                throw new JsonException($"Expected StartObject for Dictionary<string,{typeof(TValue).Name}>, got {reader.TokenType}.");

            var result = new Dictionary<string, TValue>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) return result;
                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException($"Expected PropertyName, got {reader.TokenType}.");

                var key = reader.GetString()!;
                reader.Read();
                result[key] = JsonSerializer.Deserialize<TValue>(ref reader, options)!;
            }
            throw new JsonException("Unexpected end of JSON while reading a dictionary.");
        }

        public override void Write(Utf8JsonWriter writer, Dictionary<string, TValue> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var key in value.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                writer.WritePropertyName(key); // literal key — dictionary keys carry no naming policy
                JsonSerializer.Serialize(writer, value[key], options);
            }
            writer.WriteEndObject();
        }
    }
}
