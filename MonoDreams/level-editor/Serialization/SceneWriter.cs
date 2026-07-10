#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// gizmo / overlay entities are untagged and excluded.</para>
///
/// <para>It is infrastructure, not a component (ECS purity): it holds the write behaviour and runs
/// at save time only — never per frame.</para>
/// </summary>
public sealed class SceneWriter
{
    /// <summary>The native scene file extension. A scene id <c>"island"</c> writes to
    /// <c>&lt;LevelsPath&gt;/island.mdscene</c>.</summary>
    public const string SceneFileExtension = ".mdscene";

    private readonly SceneSerializer serializer;
    private readonly Func<string, PrefabData?>? _prefabSource;

    /// <summary>How many duplicate stable ids the LAST <see cref="BuildScene"/> self-healed (PF-F) — the
    /// caller (the overlay's Save path) reads it to raise a status notification. Zero on a clean build.</summary>
    public int LastBuildDuplicateIdRestamps { get; private set; }

    /// <param name="serializer">The in-memory component round-trip seam.</param>
    /// <param name="prefabSource">Optional prefab resolver (<c>id → <see cref="PrefabData"/></c>), needed
    /// only to <b>compact linked prefab instances</b>: the writer diffs an instance root's live components
    /// against the prefab root's bytes to emit the compact <c>prefab</c> + Transform + overrides entry. A
    /// scene with NO prefab instances never touches it, so a null source keeps the existing byte-stable
    /// behaviour verbatim; a scene WITH an instance but no source fails loud (it cannot compact correctly).</param>
    public SceneWriter(SceneSerializer serializer, Func<string, PrefabData?>? prefabSource = null)
    {
        this.serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _prefabSource = prefabSource;
    }

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
        LastBuildDuplicateIdRestamps = 0;
        var members = CollectOrderedMembership(world);
        var scene = serializer.Serialize(members);

        // Stamp each root's persisted stable id onto its entry (a top-level, parent-null entry). A
        // ChildOf descendant carries no id of its own (it is ordered within its ancestor's closure).
        // SELF-HEAL colliding ids (PF-F): a corrupt world (a double-load that restored two roots with the
        // SAME id) would write two entries with equal ids and stay corrupt across re-saves. Re-stamp the
        // LATER duplicates to the next free id — mutating the live SceneEntityIdComponent too, so the heal
        // sticks — and record the count so the caller can notify. The save proceeds (self-healing).
        var maxId = -1;
        for (var i = 0; i < members.Count && i < scene.Entities.Count; i++)
            if (scene.Entities[i].Parent == null && members[i].Has<SceneEntityIdComponent>())
                maxId = Math.Max(maxId, members[i].Get<SceneEntityIdComponent>().Id);
        var nextFree = maxId + 1;
        var usedIds = new HashSet<int>();
        for (var i = 0; i < members.Count && i < scene.Entities.Count; i++)
        {
            if (scene.Entities[i].Parent != null || !members[i].Has<SceneEntityIdComponent>()) continue;
            var id = members[i].Get<SceneEntityIdComponent>().Id;
            if (!usedIds.Add(id))
            {
                var healed = nextFree++;
                usedIds.Add(healed);
                members[i].Set(new SceneEntityIdComponent(healed)); // self-heal the live world too
                Logger.Warning(
                    $"[level-editor] Save self-healed a duplicate stable id {id} → {healed} (a corrupt or " +
                    "double-loaded scene had two roots sharing an id). The save proceeds with distinct ids.");
                id = healed;
                LastBuildDuplicateIdRestamps++;
            }
            scene.Entities[i].Id = id;
        }

        // Compact each linked prefab-instance root into its {prefab + core.Transform + overrides} entry.
        // (Its prefab-owned children were already excluded from the membership closure below.)
        CompactPrefabInstances(scene, members);

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

        return Save(BuildScene(world, camera, layers), filePath);
    }

    /// <summary>
    /// Canonical-serializes an <b>already-built</b> <see cref="SceneData"/> and writes it to
    /// <paramref name="filePath"/> in the project source tree (same guard + directory-create + write as
    /// the world overload). This is the seam the editor uses so it can <b>lint the scene</b>
    /// (<see cref="SceneLint"/>) before writing it, without building the scene twice. Returns the path
    /// written, or <c>null</c> if it refused a null/empty path.
    /// </summary>
    public string? Save(SceneData scene, string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            Logger.Warning(
                "[level-editor] Save refused: no destination path (the project root is unresolved, so " +
                "there is nowhere versioned to write). This should have been blocked upstream by the " +
                "save-guard; set MONODREAMS_PROJECT_ROOT in the run configuration.");
            return null;
        }

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

        // A linked prefab instance's children are PREFAB-OWNED — they come from the .mdprefab on every
        // expansion, never from the scene (pre-mortem #3: serializing them = silent bloat + double
        // expansion on load). Add the instance ROOT (it IS a scene member) but STOP the descent here, so
        // no instance-child ever enters the membership closure. (The root itself is compacted to
        // {prefab + Transform + overrides} in BuildScene; a designer-added child to an instance is v1
        // terrain — Unpack first.)
        if (entity.Has<PrefabInstanceComponent>()) return;

        if (!childrenByParent.TryGetValue(entity, out var children)) return;
        foreach (var child in children)
            AddWithDescendants(child, childrenByParent, result, seen);
    }

    /// <summary>
    /// Rewrites every linked prefab-instance root's entry (a member carrying
    /// <see cref="PrefabInstanceComponent"/>) into the <b>compact</b> form: its <see cref="SceneEntityData.Prefab"/>
    /// id, plus a <c>components{}</c> holding ONLY <c>core.Transform</c> (always instance-owned) and the
    /// <b>overrides</b> — components whose canonical bytes differ from, or that are absent in, the prefab
    /// root's (diff-based, via <see cref="PrefabDiff.ComputeOverrides"/>; the full component set the
    /// serializer already produced is the input). The prefab root's bytes come from the injected prefab
    /// source, cached per build. A scene with instances but no source, or a referenced prefab that cannot
    /// be resolved, <b>fails loud</b> — compacting against a phantom prefab would corrupt the diff.
    /// </summary>
    private void CompactPrefabInstances(SceneData scene, List<Entity> members)
    {
        PrefabCache? cache = null;
        for (var i = 0; i < members.Count && i < scene.Entities.Count; i++)
        {
            if (!members[i].Has<PrefabInstanceComponent>()) continue;
            var prefabId = members[i].Get<PrefabInstanceComponent>().PrefabId;

            if (_prefabSource == null)
                throw new InvalidOperationException(
                    $"[level-editor] Cannot serialize the prefab instance of '{prefabId}': the scene writer has " +
                    "no prefab source to diff overrides against. Construct SceneWriter with a prefab source.");

            cache ??= new PrefabCache(_prefabSource);
            var prefab = cache.Resolve(prefabId)
                ?? throw new InvalidOperationException(
                    $"[level-editor] Cannot serialize the prefab instance of '{prefabId}': no .mdprefab resolved " +
                    "for that id (the prefab was deleted or moved). Aborting the save rather than writing a corrupt " +
                    "instance entry.");

            var entry = scene.Entities[i];
            entry.Prefab = prefabId;
            entry.Components = PrefabDiff.ComputeOverrides(entry.Components, prefab.Root.Components);
        }
    }
}
