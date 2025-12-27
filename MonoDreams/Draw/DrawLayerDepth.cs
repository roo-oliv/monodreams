using System;
using System.Collections.Generic;

namespace MonoDreams.Draw;

public static class DrawLayerDepth
{
    private static readonly Dictionary<Type, Dictionary<Enum, float>> Cache = new();

    public static float GetLayerDepth(Enum layer)
    {
        if (layer == null)
        {
            return 0;
        }
        
        var type = layer.GetType();
        if (!Cache.ContainsKey(type))
        {
            CacheEnum(type);
        }

        return Cache[type][layer];
    }

    private static void CacheEnum(Type enumType)
    {
        var values = Enum.GetValues(enumType);
        int length = values.Length;
        var mapping = new Dictionary<Enum, float>();

        for (int i = 0; i < length; i++)
        {
            // Invert so first enum value = 1.0 (front), last = 0.0 (back)
            mapping[(Enum)values.GetValue(i)] = (length - 1 - i) / (float)(length - 1);
        }

        Cache[enumType] = mapping;
    }
}
