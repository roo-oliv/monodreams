using System;
using System.Collections.Generic;

namespace MonoDreams.Draw;

/// <summary>
/// Maps named draw layers to evenly-spaced depth values.
/// Created from any enum via <see cref="FromEnum{TEnum}"/>: the first member maps to 1.0 (front),
/// the last to 0.0 (back), with even spacing in between.
/// Supports type-safe lookup for game code and string-based lookup for core systems (e.g. Blender parser).
/// </summary>
public sealed class DrawLayerMap
{
    private readonly Dictionary<string, float> _layers;
    private readonly float _step;
    private readonly HashSet<float> _ySortedDepths = new();

    private DrawLayerMap(Dictionary<string, float> layers, float step)
    {
        _layers = layers;
        _step = step;
    }

    /// <summary>
    /// Creates a DrawLayerMap from an enum type. First member = 1.0 (front), last = 0.0 (back).
    /// </summary>
    public static DrawLayerMap FromEnum<TEnum>() where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        var length = values.Length;
        var layers = new Dictionary<string, float>(length, StringComparer.OrdinalIgnoreCase);

        float step = length <= 1 ? 1f : 1f / (length - 1);

        for (int i = 0; i < length; i++)
        {
            float depth = length == 1 ? 0.5f : (length - 1 - i) / (float)(length - 1);
            layers[values[i].ToString()] = depth;
        }

        return new DrawLayerMap(layers, step);
    }

    /// <summary>
    /// Gets the depth for a typed enum layer.
    /// </summary>
    public float GetDepth<TEnum>(TEnum layer) where TEnum : struct, Enum
    {
        return _layers[layer.ToString()];
    }

    /// <summary>
    /// Gets the depth for a typed enum layer with a sub-layer offset.
    /// </summary>
    public float GetDepth<TEnum>(TEnum layer, float subLayerOffset) where TEnum : struct, Enum
    {
        return _layers[layer.ToString()] + subLayerOffset;
    }

    /// <summary>
    /// Gets the depth for a layer by name (case-insensitive). For use by core systems.
    /// </summary>
    public float GetDepth(string layerName)
    {
        return _layers[layerName];
    }

    /// <summary>
    /// Gets the depth for a layer by name with a sub-layer offset.
    /// </summary>
    public float GetDepth(string layerName, float subLayerOffset)
    {
        return _layers[layerName] + subLayerOffset;
    }

    /// <summary>
    /// Tries to get the depth for a layer by name (case-insensitive).
    /// Returns false if the layer name is not found.
    /// </summary>
    public bool TryGetDepth(string layerName, out float depth)
    {
        return _layers.TryGetValue(layerName, out depth);
    }

    /// <summary>
    /// Registers an alias that maps to the same depth as an existing enum layer.
    /// All string-based lookups (TryGetDepth, GetDepth) work transparently with aliases.
    /// </summary>
    public DrawLayerMap WithAlias<TEnum>(string alias, TEnum layer) where TEnum : struct, Enum
    {
        var layerName = layer.ToString();
        if (!_layers.TryGetValue(layerName, out var depth))
            throw new ArgumentException($"Layer '{layerName}' not found in map.", nameof(layer));
        _layers[alias] = depth;
        return this;
    }

    /// <summary>
    /// Marks a layer as Y-sorted. Entities on this layer will have their depth dynamically
    /// adjusted based on Y position (lower on screen = rendered in front).
    /// </summary>
    public DrawLayerMap WithYSort<TEnum>(TEnum layer) where TEnum : struct, Enum
    {
        _ySortedDepths.Add(_layers[layer.ToString()]);
        return this;
    }

    /// <summary>
    /// Enumerates every registered layer (including aliases) as (name, depth, ySorted) — used by the
    /// scene writer to persist the <c>layers[]</c> block so a loaded scene can reconstruct the depth
    /// banding. Read-only: the map's internal storage is not exposed, only a snapshot of its entries.
    /// </summary>
    public IEnumerable<(string Name, float Depth, bool YSorted)> EnumerateLayers()
    {
        foreach (var (name, depth) in _layers)
            yield return (name, depth, _ySortedDepths.Contains(depth));
    }

    /// <summary>
    /// Finds the layer band that CONTAINS <paramref name="depth"/> — the nearest registered layer
    /// depth within half a layer step — and returns that band's depth, its inset [min, max] range
    /// (the same margin math as <see cref="TryGetYSortRange"/>), and whether it is Y-sorted.
    /// Unlike <see cref="TryGetYSortRange"/> (an exact-match lookup on the registered value), this
    /// answers for depths that have been nudged off the band centre — the level editor's
    /// within-band ordering (bring forward / send back) adjusts SOURCE
    /// <c>SpriteInfoComponent.LayerDepth</c> in small steps and needs the band edges to clamp at.
    /// False when <paramref name="depth"/> falls in no band (outside every layer's half-step).
    /// </summary>
    public bool TryGetBandRange(float depth, out float bandDepth, out float minDepth, out float maxDepth, out bool ySorted)
    {
        const float margin = 0.001f;
        float halfStep = _step / 2f;

        var found = false;
        var bestDistance = float.MaxValue;
        bandDepth = 0f;
        foreach (var layerDepth in _layers.Values)
        {
            var distance = Math.Abs(depth - layerDepth);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bandDepth = layerDepth;
                found = distance <= halfStep;
            }
        }

        if (!found)
        {
            bandDepth = 0f;
            minDepth = 0f;
            maxDepth = 0f;
            ySorted = false;
            return false;
        }

        minDepth = bandDepth - halfStep + margin;
        maxDepth = bandDepth + halfStep - margin;
        ySorted = _ySortedDepths.Contains(bandDepth);
        return true;
    }

    /// <summary>
    /// Returns the Y-sort interpolation range for a depth value, if it belongs to a Y-sorted layer.
    /// The range is slightly inset from the full layer range to prevent overlap with adjacent layers.
    /// </summary>
    public bool TryGetYSortRange(float depth, out float minDepth, out float maxDepth)
    {
        if (_ySortedDepths.Contains(depth))
        {
            const float margin = 0.001f;
            float halfStep = _step / 2f;
            minDepth = depth - halfStep + margin;
            maxDepth = depth + halfStep - margin;
            return true;
        }

        minDepth = 0f;
        maxDepth = 0f;
        return false;
    }
}
