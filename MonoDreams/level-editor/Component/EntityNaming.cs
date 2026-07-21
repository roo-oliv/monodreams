#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using MonoDreams.Component;

namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// The ONE shared uniquifier for editor-assigned entity names (PF-F): when a placed prefab instance
/// (or any editor-created entity) needs a tree name, it takes the desired base and appends the next
/// free numeric suffix so the Entities tree reads <c>House</c>, <c>House 2</c>, <c>House 3</c> — an
/// exact-name scan of the live world's <see cref="EntityInfoComponent.Name"/>s. The base's first
/// occurrence is un-suffixed; collisions get <c>" 2"</c>, <c>" 3"</c>, … (space + number).
///
/// <para>Pure query over the world (ECS purity — a static helper, not a component). Case-sensitive
/// ordinal match (names are identifiers). An empty base falls back to <c>"Entity"</c>.</para>
/// </summary>
public static class EntityNaming
{
    /// <summary>
    /// The next free unique name for <paramref name="baseName"/> against every live
    /// <see cref="EntityInfoComponent.Name"/> in <paramref name="world"/> (excluding
    /// <paramref name="exclude"/> — the entity being (re)named, whose own current name must not block
    /// itself). Returns <paramref name="baseName"/> when free, else <c>"&lt;base&gt; N"</c> for the
    /// smallest free <c>N ≥ 2</c>.
    /// </summary>
    public static string UniqueName(World world, string baseName, Entity exclude = default)
    {
        if (string.IsNullOrEmpty(baseName)) baseName = "Entity";

        var taken = new HashSet<string>(StringComparer.Ordinal);
        using var set = world.GetEntities().With<EntityInfoComponent>().AsSet();
        foreach (var e in set.GetEntities())
        {
            if (!e.IsAlive || e.Equals(exclude)) continue;
            var name = e.Get<EntityInfoComponent>().Name;
            if (!string.IsNullOrEmpty(name)) taken.Add(name);
        }

        if (!taken.Contains(baseName)) return baseName;
        for (var n = 2; ; n++)
        {
            var candidate = $"{baseName} {n}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }
}
