#nullable enable
using System.Collections.Generic;
using DefaultEcs;
using MonoDreams.Component;
using MonoDreams.LevelEditor.Component;

namespace MonoDreams.LevelEditor.UI;

/// <summary>One row of the editor's SCENE tree: a world entity, its indentation depth (0 for a
/// root, +1 per <c>ChildOfComponent</c> ancestor that is itself visible), and its display label.</summary>
public readonly struct SceneTreeRow
{
    public readonly Entity Entity;
    public readonly int Depth;
    public readonly string Label;

    public SceneTreeRow(Entity entity, int depth, string label)
    {
        Entity = entity;
        Depth = depth;
        Label = label;
    }
}

/// <summary>
/// Pure, world-only (no GraphicsDevice) builder for the editor's SCENE tree: it turns the world's
/// entities into a flat, depth-indented row list — <b>roots first, each root's
/// <c>ChildOfComponent</c> descendants nested underneath, indented one level per hop</b>. It is the
/// SCENE section's model, unit-testable without any rendering.
///
/// <para><b>Editor infrastructure is hidden.</b> Every entity tagged
/// <see cref="EditorInfrastructureComponent"/> — the chrome, panel rows, gizmo overlays / collider
/// proxies, the gizmo-state entity, the panel-state entity, an overlay-provided cursor — is
/// filtered out, so the tree shows game/scene entities only (the same marker the transport's
/// Restart uses to decide what survives). A visible entity whose <c>ChildOf</c> parent is filtered
/// out (or dead, or missing) becomes a root.</para>
///
/// <para><b>Ordering.</b> Roots and each parent's children preserve the source enumeration order
/// (DefaultEcs enumerates in creation order), so the tree is stable across frames for an
/// undisturbed world. A <c>ChildOf</c> cycle cannot loop the walk (a visited set guards it).</para>
///
/// <para><b>Labels.</b> An entity's <see cref="EntityInfoComponent"/> name (else its type) is the
/// label; failing that, its stable <see cref="EditorIdComponent"/> id; failing that, a stable
/// per-entity hash (<c>Entity.GetHashCode()</c>, the same fallback the game-side inspector uses).</para>
/// </summary>
public static class SceneTreeBuilder
{
    /// <summary>Builds the depth-indented row list for <paramref name="entities"/> (typically
    /// <c>world.GetEntities().AsSet()</c> / <c>.AsEnumerable()</c>). Editor-infrastructure and dead
    /// entities are excluded; roots come first with their descendant closure nested underneath.</summary>
    public static List<SceneTreeRow> Build(IEnumerable<Entity> entities)
    {
        var visible = new List<Entity>();
        var inSet = new HashSet<Entity>();
        foreach (var e in entities)
        {
            if (!e.IsAlive) continue;
            if (e.Has<EditorInfrastructureComponent>()) continue;
            if (inSet.Add(e)) visible.Add(e);
        }

        // Parent → ordered children, and the root list, both preserving enumeration order.
        var childrenOf = new Dictionary<Entity, List<Entity>>();
        var roots = new List<Entity>();
        foreach (var e in visible)
        {
            if (TryVisibleParent(e, inSet, out var parent))
                (childrenOf.TryGetValue(parent, out var kids)
                    ? kids
                    : childrenOf[parent] = new List<Entity>()).Add(e);
            else
                roots.Add(e);
        }

        var rows = new List<SceneTreeRow>();
        var visited = new HashSet<Entity>();
        foreach (var root in roots)
            Visit(root, 0, rows, childrenOf, visited);
        return rows;
    }

    /// <summary>The label for an entity: its <c>EntityInfoComponent</c> name (else type), else its
    /// stable <c>EditorId</c>, else a stable per-entity hash.</summary>
    public static string LabelFor(Entity entity)
    {
        if (entity.Has<EntityInfoComponent>())
        {
            var info = entity.Get<EntityInfoComponent>();
            if (!string.IsNullOrWhiteSpace(info.Name)) return info.Name!;
            if (!string.IsNullOrWhiteSpace(info.Type)) return info.Type!;
        }
        if (entity.Has<EditorIdComponent>())
            return "#" + entity.Get<EditorIdComponent>().Id;
        return "Entity #" + entity.GetHashCode().ToString("X");
    }

    private static void Visit(Entity entity, int depth, List<SceneTreeRow> rows,
        Dictionary<Entity, List<Entity>> childrenOf, HashSet<Entity> visited)
    {
        if (!visited.Add(entity)) return; // guard against a ChildOf cycle
        rows.Add(new SceneTreeRow(entity, depth, LabelFor(entity)));
        if (childrenOf.TryGetValue(entity, out var kids))
            foreach (var child in kids)
                Visit(child, depth + 1, rows, childrenOf, visited);
    }

    /// <summary>Whether the entity has a <c>ChildOf</c> parent that is itself in the visible set
    /// (alive, not editor-infrastructure). A missing / dead / filtered parent ⇒ the entity is a root.</summary>
    private static bool TryVisibleParent(Entity entity, HashSet<Entity> inSet, out Entity parent)
    {
        parent = default;
        if (!entity.Has<ChildOfComponent>()) return false;
        var candidate = entity.Get<ChildOfComponent>().Parent;
        if (candidate == entity || !candidate.IsAlive || !inSet.Contains(candidate)) return false;
        parent = candidate;
        return true;
    }
}
