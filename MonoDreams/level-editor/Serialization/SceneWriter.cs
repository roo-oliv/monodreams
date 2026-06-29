#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using DefaultEcs;
using MonoDreams.Component;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.Platform;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// Writes a native MonoDreams scene from the live world: it computes the <b>membership closure</b>
/// (every <see cref="SceneObjectComponent"/> root plus each one's <c>ChildOfComponent</c>
/// descendants), serializes that set through the Wave-2 <see cref="SceneSerializer"/> into a
/// <see cref="SceneData"/>, attaches the active <see cref="Camera"/> state and the layer banding
/// from a <see cref="DrawLayerMap"/>, JSON-serializes it (System.Text.Json), and exports it through
/// <see cref="IPlatformServices.ExportScene"/> (desktop file / web download).
///
/// <para>Only tagged roots and their child closure are written: transient cursor / UI / HUD /
/// gizmo / overlay entities are untagged and excluded, and Blender-origin entities are untagged in
/// this wave (their save is deferred), so the Blender world stays view-only.</para>
///
/// <para>It is infrastructure, not a component (ECS purity): it holds the write behaviour and runs
/// at save time only — never per frame.</para>
/// </summary>
public sealed class SceneWriter(SceneSerializer serializer)
{
    /// <summary>Shared, indented JSON options so an exported scene is human-diffable.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Builds the <see cref="SceneData"/> for the current contents of <paramref name="world"/>:
    /// the membership closure of every <see cref="SceneObjectComponent"/> root, plus
    /// <paramref name="camera"/> state and the <paramref name="layers"/> banding (both optional).
    /// </summary>
    public SceneData BuildScene(World world, Camera? camera = null, DrawLayerMap? layers = null)
    {
        var members = CollectMembership(world);
        var scene = serializer.Serialize(members);

        if (camera != null)
            scene.Camera = new SceneCameraData
            {
                Position = new[] { camera.Position.X, camera.Position.Y },
                Zoom = camera.Zoom,
                Rotation = camera.Rotation,
            };

        if (layers != null)
            foreach (var (name, depth, ySorted) in layers.EnumerateLayers())
                scene.Layers.Add(new SceneLayerData { Name = name, Depth = new[] { depth, depth }, YSorted = ySorted });

        return scene;
    }

    /// <summary>
    /// Serializes the current scene to JSON and exports it through <see cref="IPlatformServices.ExportScene"/>.
    /// Returns the host-meaningful locator the platform reported (a file path on desktop), or
    /// <c>null</c> when delivered out-of-band (e.g. a browser download / console echo on web).
    /// </summary>
    public string? Save(World world, string suggestedFileName, Camera? camera = null, DrawLayerMap? layers = null)
    {
        var scene = BuildScene(world, camera, layers);
        var json = JsonSerializer.Serialize(scene, JsonOptions);
        var locator = PlatformServices.Current.ExportScene(suggestedFileName, json);
        Logger.Info($"[level-editor] Saved scene '{suggestedFileName}' " +
                    $"({scene.Entities.Count} entities) to {(locator ?? "out-of-band (web)")}.");
        return locator;
    }

    /// <summary>
    /// Collects the membership set: every <see cref="SceneObjectComponent"/>-tagged entity, plus the
    /// transitive <c>ChildOfComponent</c> descendants of each tagged entity. The result is
    /// deduplicated and ordered roots-first so the <see cref="SceneSerializer"/>'s parent indices
    /// stay valid (a child's parent always precedes it only when the parent is itself in scope; the
    /// serializer tolerates an out-of-scope parent by making the child a root on load).
    /// </summary>
    public static List<Entity> CollectMembership(World world)
    {
        var result = new List<Entity>();
        var seen = new HashSet<Entity>();

        // Index children by parent once, so the descendant walk is O(n) rather than O(n^2).
        var childrenByParent = new Dictionary<Entity, List<Entity>>();
        using var childSet = world.GetEntities().With<ChildOfComponent>().AsSet();
        foreach (var entity in childSet.GetEntities())
        {
            var parent = entity.Get<ChildOfComponent>().Parent;
            if (!parent.IsAlive) continue;
            if (!childrenByParent.TryGetValue(parent, out var list))
                childrenByParent[parent] = list = new List<Entity>();
            list.Add(entity);
        }

        // Each tagged root, then its descendant closure (depth-first, parent before children).
        using var roots = world.GetEntities().With<SceneObjectComponent>().AsSet();
        foreach (var entity in roots.GetEntities())
            AddWithDescendants(entity, childrenByParent, result, seen);

        return result;
    }

    private static void AddWithDescendants(
        Entity entity,
        Dictionary<Entity, List<Entity>> childrenByParent,
        List<Entity> result,
        HashSet<Entity> seen)
    {
        if (!seen.Add(entity)) return; // already included (e.g. a child that is also tagged)
        result.Add(entity);

        if (!childrenByParent.TryGetValue(entity, out var children)) return;
        foreach (var child in children)
            AddWithDescendants(child, childrenByParent, result, seen);
    }
}
