#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// from a <see cref="DrawLayerMap"/>, canonical-serializes it (<see cref="CanonicalJson"/>), and
/// writes it <b>into the project source tree</b> via <see cref="IPlatformServices.WriteAllText"/>
/// (a desktop file write that git sees immediately — see <see cref="Save"/>).
///
/// <para><b>Where it writes (PS3).</b> The editor resolves the versioned project root
/// (<c>EditorProjectContext</c>) and hands this writer the absolute destination
/// <c>ProjectRoot/LevelsDir/&lt;sceneId&gt;.mdscene</c>. This replaces the pre-PS3 path that exported
/// to the ephemeral build-output dir (<c>BaseDirectory</c>) through
/// <see cref="IPlatformServices.ExportScene"/> — that seam is now reserved for the (deferred) web
/// out-of-band download and is no longer the editor's scene-save path, so no scene lands in
/// <c>bin/…</c> on desktop. The <b>shipped game</b> reads bundled scenes read-only via
/// <c>TitleContainer</c> (console-portable, PS4); only the desktop editor writes.</para>
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
    /// <summary>The native scene file extension. A scene id <c>"island"</c> writes to
    /// <c>&lt;LevelsPath&gt;/island.mdscene</c>.</summary>
    public const string SceneFileExtension = ".mdscene";

    /// <summary>
    /// Builds the <see cref="SceneData"/> for the current contents of <paramref name="world"/>:
    /// the membership closure of every <see cref="SceneObjectComponent"/> root — <b>ordered by each
    /// root's persisted stable scene-local id</b> (see <see cref="CollectOrderedMembership"/>) so a
    /// re-save keeps <c>entities[]</c> in a stable order — plus <paramref name="camera"/> state and the
    /// <paramref name="layers"/> banding (both optional).
    ///
    /// <para><b>Side effect (by design).</b> Assigns a stable id to any root lacking one — the
    /// "assigned at first serialization" step (§9); the id is stamped onto a live
    /// <see cref="SceneEntityIdComponent"/> so it sticks and is written to the file. Serializing the
    /// same world twice is therefore byte-identical (the second call reuses the ids the first stamped).</para>
    /// </summary>
    public SceneData BuildScene(World world, Camera? camera = null, DrawLayerMap? layers = null)
    {
        var members = CollectOrderedMembership(world);
        var scene = serializer.Serialize(members);

        // Stamp each root's persisted stable id onto its entry (a top-level, parent-null entry). A
        // ChildOf descendant carries no id of its own (it is ordered within its ancestor's closure).
        for (var i = 0; i < members.Count && i < scene.Entities.Count; i++)
            if (scene.Entities[i].Parent == null && members[i].Has<SceneEntityIdComponent>())
                scene.Entities[i].Id = members[i].Get<SceneEntityIdComponent>().Id;

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
    /// Serializes the current scene to canonical JSON and <b>writes it to <paramref name="filePath"/>
    /// in the project source tree</b> via <see cref="IPlatformServices.WriteAllText"/> (a desktop file
    /// write git sees), creating the containing directory if it is missing. Returns the path written,
    /// or <c>null</c> if it refused.
    ///
    /// <para><b>Defense-in-depth guard.</b> A null / empty <paramref name="filePath"/> — the shape the
    /// caller produces when the project root is unresolved (<c>EditorProjectContext.LevelsPath</c> is
    /// null) — is <b>refused loudly with no write</b>. The overlay's save-guard
    /// (<c>EditorOverlay.SaveBlock</c> → <c>NoProjectRoot</c>) already blocks the dispatch before it
    /// reaches here; this second guard ensures the writer never writes to nowhere on any path.</para>
    /// </summary>
    /// <param name="filePath">Absolute destination, e.g.
    /// <c>ProjectRoot/LevelsDir/&lt;sceneId&gt;.mdscene</c>. The overlay combines
    /// <c>EditorProjectContext.LevelsPath</c> with the scene id + <see cref="SceneFileExtension"/>.</param>
    public string? Save(World world, string? filePath, Camera? camera = null, DrawLayerMap? layers = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            Logger.Warning(
                "[level-editor] Save refused: no destination path (the project root is unresolved, so " +
                "there is nowhere versioned to write). This should have been blocked upstream by the " +
                "save-guard; set MONODREAMS_PROJECT_ROOT in the run configuration.");
            return null;
        }

        var scene = BuildScene(world, camera, layers);
        var json = CanonicalJson.Serialize(scene);

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            PlatformServices.Current.CreateDirectory(directory);
        PlatformServices.Current.WriteAllText(filePath!, json);

        Logger.Info($"[level-editor] Saved scene ({scene.Entities.Count} entities) to '{filePath}'.");
        return filePath;
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

    /// <summary>
    /// The membership set (see <see cref="CollectMembership"/>) <b>ordered deterministically</b> for a
    /// byte-stable file: each root's <see cref="SceneEntityIdComponent"/> is assigned (if missing) and
    /// the flat closure is ordered by owning-root id. Because the ordering is a <b>stable</b> sort on
    /// the owning root's id, each root's DFS closure (parent-before-children) stays intact and grouped,
    /// and roots appear in id order — so two worlds whose roots carry the same ids serialize identically
    /// regardless of the live entity-creation order, and a re-save never reshuffles <c>entities[]</c>.
    ///
    /// <para><b>Side effect:</b> stamps a stable id onto any root lacking one (the "assigned at first
    /// serialization" step). New ids are handed out from <c>max present + 1</c> in flat-enumeration
    /// order, which on a reloaded scene equals the file order (entities are created in <c>entities[]</c>
    /// order), so ids stay stable across <c>load → save</c>.</para>
    /// </summary>
    public static List<Entity> CollectOrderedMembership(World world)
    {
        var flat = CollectMembership(world);
        if (flat.Count == 0) return flat;

        var memberSet = new HashSet<Entity>(flat);

        // Map each member to its owning root (the topmost ancestor still inside the member set) —
        // mirrors SceneSerializer's "parent in scope" rule, so a top-level entry is its own root.
        var rootOf = new Dictionary<Entity, Entity>(flat.Count);
        foreach (var e in flat) rootOf[e] = FindRoot(e, memberSet);

        // Roots in flat (enumeration) order; hand out any missing stable ids.
        var roots = flat.Where(e => rootOf[e].Equals(e)).ToList();
        AssignStableIds(roots);

        // Stable sort by owning-root id keeps each closure contiguous and in DFS order.
        return flat.OrderBy(e => rootOf[e].Get<SceneEntityIdComponent>().Id).ToList();
    }

    /// <summary>Walks <c>ChildOf</c> up from <paramref name="e"/> while the parent stays inside
    /// <paramref name="memberSet"/>, returning the owning root (bounded to defend against a malformed cycle).</summary>
    private static Entity FindRoot(Entity e, HashSet<Entity> memberSet)
    {
        var cur = e;
        for (var i = 0; i < 4096 && cur.Has<ChildOfComponent>(); i++)
        {
            var parent = cur.Get<ChildOfComponent>().Parent;
            if (!parent.IsAlive || !memberSet.Contains(parent)) break;
            cur = parent;
        }
        return cur;
    }

    /// <summary>Assigns a monotonic <see cref="SceneEntityIdComponent"/> to each root in
    /// <paramref name="roots"/> that lacks one; the next free id is <c>max present + 1</c>. Roots that
    /// already carry an id (e.g. restored from a loaded file) are left untouched, so ids are preserved
    /// across <c>load → save</c>.</summary>
    private static void AssignStableIds(List<Entity> roots)
    {
        var next = 0;
        foreach (var r in roots)
            if (r.Has<SceneEntityIdComponent>())
                next = Math.Max(next, r.Get<SceneEntityIdComponent>().Id + 1);

        foreach (var r in roots)
            if (!r.Has<SceneEntityIdComponent>())
                r.Set(new SceneEntityIdComponent(next++));
    }

    private static void AddWithDescendants(
        Entity entity,
        Dictionary<Entity, List<Entity>> childrenByParent,
        List<Entity> result,
        HashSet<Entity> seen)
    {
        // Bake products (e.g. a boundary's segment colliders) are NEVER serialized — even inside a
        // tagged root's ChildOf closure. The durable truth is the authoring source (the polyline),
        // and the products regenerate on load; re-serializing them would double-count on the next
        // load and bake stale run state into the file. (The 'bake products never scene-serialize'
        // invariant, first applied here — Slice 3.) Skipping the subtree is correct because a bake
        // product is always a LEAF or a sub-tree of derived entities, never an authored ancestor.
        if (entity.Has<BakedProductComponent>()) return;

        if (!seen.Add(entity)) return; // already included (e.g. a child that is also tagged)
        result.Add(entity);

        if (!childrenByParent.TryGetValue(entity, out var children)) return;
        foreach (var child in children)
            AddWithDescendants(child, childrenByParent, result, seen);
    }
}
