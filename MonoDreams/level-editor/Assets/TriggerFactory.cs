#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.LevelEditor.Component;

namespace MonoDreams.LevelEditor.Assets;

/// <summary>
/// Builds a trigger-zone entity (island-authoring plan §5.3): a <c>Passive</c> box collider whose
/// identity rides <see cref="EntityInfoComponent"/> — <c>Type</c> = the trigger's category prefix
/// (what a game reaction system pattern-matches on), <c>Name</c> = an auto-numbered instance id
/// unique in the scene (<c>"evidence_01"</c>, <c>"talkzone_01"</c>, …). No new component: the
/// trigger IS a passive collider + an identity string, exactly what the <c>ZoneDialogueTriggerSystem</c>
/// precedent reads off a collision message. The box is <b>centered on the placement point</b> so a
/// designer drops a zone at a spot; the trigger IS a standalone collider ENTITY, so it is then
/// selected (border-pick on its world shape) and moved/scaled by the ordinary gizmo (colliders-as-entities).
///
/// <para>Like <see cref="SpritePropFactory"/>, this is a plain builder — the same
/// <c>Func&lt;World, Entity&gt;</c> shape <c>CreateEntityCommand</c> wraps for one-undo-step
/// placement (the command tags <see cref="SceneObjectComponent"/>). It round-trips through the
/// existing <c>EntityInfo</c> + <c>BoxCollider</c> serializers unchanged (the <c>Passive</c> flag
/// is already serialized).</para>
/// </summary>
public static class TriggerFactory
{
    /// <summary>Builds the trigger stack at <paramref name="position"/> with identity
    /// <c>(type.Prefix, name)</c> and a passive box of the type's size, centered on the point.
    /// The type's <see cref="TriggerType.ActiveLayers"/> scope the box (null = collider default);
    /// its <see cref="TriggerType.Configure"/> hook then attaches any game components.</summary>
    public static Entity Create(World world, TriggerType type, Vector2 position, string name)
    {
        var entity = world.CreateEntity();
        entity.Set(new EntityInfoComponent(type.Prefix, name));
        entity.Set(new TransformComponent(position));
        // A trigger IS a standalone collider entity now (collider == body): the box is centered on
        // the placement point by the shape itself (former CenteredBounds), no offset needed.
        entity.Set(type.ActiveLayers != null
            ? new BoxColliderComponent(type.Size, activeLayers: new HashSet<int>(type.ActiveLayers), passive: true)
            : new BoxColliderComponent(type.Size, passive: true));
        type.Configure?.Invoke(entity);
        // No SceneObjectComponent here — the placement path's CreateEntityCommand tags the root.
        return entity;
    }

    /// <summary>The trigger's Transform-relative bounds: a box of <paramref name="size"/> centered
    /// on the entity's <c>Position</c> (rounded to whole units for the int rectangle).</summary>
    public static Rectangle CenteredBounds(Vector2 size)
    {
        var w = Math.Max(1, (int)MathF.Round(size.X));
        var h = Math.Max(1, (int)MathF.Round(size.Y));
        return new Rectangle(-w / 2, -h / 2, w, h);
    }

    /// <summary>The next unique instance name for <paramref name="prefix"/> in
    /// <paramref name="world"/> — <c>"{prefix}_{NN}"</c> one past the highest existing suffix (so
    /// numbering survives deletes without collision). Scans <see cref="EntityInfoComponent"/> for
    /// entities whose <c>Type</c> equals the prefix.</summary>
    public static string NextName(World world, string prefix)
    {
        var max = 0;
        using var set = world.GetEntities().With<EntityInfoComponent>().AsSet();
        foreach (var e in set.GetEntities())
        {
            if (!e.IsAlive) continue;
            var info = e.Get<EntityInfoComponent>();
            if (!string.Equals(info.Type, prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (TryParseSuffix(info.Name, prefix + "_", out var n) && n > max) max = n;
        }
        return $"{prefix}_{max + 1:00}";
    }

    private static bool TryParseSuffix(string? name, string prefix, out int number)
    {
        number = 0;
        if (name == null || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        return int.TryParse(name.Substring(prefix.Length), out number);
    }
}
