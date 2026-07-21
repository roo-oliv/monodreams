#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// The <b>collider migrator</b> (CE-B, colliders-as-entities): rewrites a legacy version-1
/// <c>.mdscene</c>/<c>.mdprefab</c> — whose colliders are embedded component bodies on their owning
/// entity — into the version-2 shape where every collider is its own collider ENTITY. It is the engine
/// side of the <c>monodreams migrate-colliders</c> CLI command; it lives beside
/// <see cref="CanonicalJson"/> so its output is <b>byte-canonical</b> (the same policy every native file
/// is written through) and a <c>migrate → load → save</c> is a byte fixed point (CE pre-mortem #3).
///
/// <para><b>The transform (per collider on an owner E).</b></para>
/// <list type="bullet">
///   <item><b>Box</b> — the old body is <c>{ bounds: int[4] = [x,y,w,h] }</c> (a top-left offset from E's
///   position, in E's local frame). The version-2 box is <c>{ size: float[2] = [w,h] }</c> <b>centered</b>
///   on the collider entity's Transform. The collider entity's local position is therefore the box
///   <b>centre</b> relative to E: <c>(x + w/2, y + h/2)</c>. This keeps the world rect identical
///   (top-left <c>E + [x,y]</c>, size <c>[w,h]</c>).</item>
///   <item><b>Convex</b> — the body is unchanged (<c>modelVertices</c> are already collider-entity-local,
///   and CE-A's runtime derives the world shape from the collider entity's <c>WorldMatrix</c>). It moves to
///   a collider entity at local <c>[0,0]</c> with the vertices copied <b>verbatim</b> — so the child's
///   world matrix equals E's and the world shape is unchanged, with NO float re-basing (pre-mortem #3: no
///   drift, because there is no arithmetic on the vertices).</item>
/// </list>
///
/// <para><b>Where the collider entity comes from.</b> If E is a <b>dedicated collider carrier</b> (its only
/// non-structural component is the one collider — a bare collision node a legacy import pipeline
/// produced), E <b>IS</b> the collider entity: the box is reshaped in place (its Transform nudged by the
/// bounds centre); a convex needs no change at all. Otherwise (E carries a sprite / body / other data) the
/// collider is stripped from E and moved onto a NEW child collider entity inserted immediately after E —
/// so E stays the visual/body and the collider rides it via the hierarchy (the CE authoring-model fix,
/// mirroring <c>PlayerEntityFactory</c>). <c>activeLayers</c> / <c>passive</c> / <c>enabled</c> /
/// <c>ignoreTransformRotation</c> are always preserved.</para>
///
/// <para><b>Idempotent + loud.</b> A version-2 (or newer) input is a no-op (reported, bytes untouched).
/// Unparseable JSON throws loud. The migrator only ever runs at dev time (the CLI command), never in the
/// game — so its file IO uses <see cref="File"/> directly, like the importer.</para>
/// </summary>
public static class ColliderMigration
{
    /// <summary>The version this lift stamps. The COLLIDER lift targets <b>version 2</b> — NOT the current
    /// scene version (now 3, which the separate CM camera lift reaches). A v1 file this migrator rewrites
    /// becomes a v2 file, which the version guard then loads (a v2 file with no camera block re-saves as v3)
    /// or, if it carries a camera block, refuses until the umbrella <c>monodreams migrate</c> runs the camera
    /// lift too. A literal (not <see cref="SceneData.CurrentVersion"/>) so this source-linked CLI file stays
    /// pinned to the collider lift's target across future version bumps.</summary>
    public const int TargetVersion = 2;

    // The stable component keys the migrator reads/writes. Kept in sync with EngineComponentSerializers
    // (a unit test asserts equality); duplicated here so this file stays dependency-free (System.Text.Json
    // only) and can be source-linked into the CLI, which cannot reference the engine assembly.
    internal const string BoxColliderKey = "core.BoxCollider";
    internal const string ConvexColliderKey = "core.ConvexCollider";
    internal const string TransformKey = "core.Transform";
    internal const string EntityInfoKey = "core.EntityInfo";
    internal const string SpriteInfoKey = "core.SpriteInfo";
    internal const string RigidBodyKey = "core.RigidBody";
    internal const string VelocityKey = "core.Velocity";

    /// <summary>The native file extensions the directory walk migrates.</summary>
    internal static readonly string[] MigratableExtensions = { ".mdscene", ".mdprefab" };

    /// <summary>Outcome of migrating one file's content.</summary>
    public sealed class Result
    {
        /// <summary>The (possibly rewritten) canonical JSON. Equals the input verbatim on a no-op.</summary>
        public required string Json { get; init; }

        /// <summary>Whether the bytes changed (a real migration happened).</summary>
        public required bool Changed { get; init; }

        /// <summary>Whether the input was already version 2+ (an idempotent no-op).</summary>
        public required bool AlreadyCurrent { get; init; }

        /// <summary>Box colliders reshaped in place on a dedicated collider entity.</summary>
        public int BoxesReshapedInPlace { get; init; }

        /// <summary>Colliders moved onto a newly-created child collider entity.</summary>
        public int CollidersMovedToChild { get; init; }

        /// <summary>New child collider entities added (== <see cref="CollidersMovedToChild"/>).</summary>
        public int ChildEntitiesAdded => CollidersMovedToChild;
    }

    /// <summary>
    /// Migrates one file's JSON content. Returns the (possibly rewritten) canonical bytes plus a summary.
    /// A version-2+ input is returned unchanged (<see cref="Result.AlreadyCurrent"/>). Throws
    /// <see cref="InvalidOperationException"/> on JSON that does not parse as a <see cref="SceneData"/>.
    /// </summary>
    /// <param name="json">The file content.</param>
    /// <param name="sourceName">A display name for the file (used only in the thrown error message).</param>
    public static Result Migrate(string json, string sourceName)
    {
        SceneData? scene;
        try
        {
            scene = CanonicalJson.Deserialize<SceneData>(json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"[migrate-colliders] Could not parse '{sourceName}' as a native scene/prefab: {ex.Message}", ex);
        }

        if (scene == null)
            throw new InvalidOperationException(
                $"[migrate-colliders] '{sourceName}' deserialized to null — not a valid native scene/prefab.");

        if (scene.Version >= TargetVersion)
            return new Result { Json = json, Changed = false, AlreadyCurrent = true };

        var (boxesInPlace, movedToChild) = MigrateEntities(scene);
        scene.Version = TargetVersion;

        var migrated = CanonicalJson.Serialize(scene);
        return new Result
        {
            Json = migrated,
            Changed = migrated != json,
            AlreadyCurrent = false,
            BoxesReshapedInPlace = boxesInPlace,
            CollidersMovedToChild = movedToChild,
        };
    }

    /// <summary>
    /// Rewrites every embedded collider in <paramref name="scene"/> in place: reshapes a dedicated
    /// collider carrier's box, and moves any collider on a non-dedicated owner onto a new child collider
    /// entity (inserted immediately after its owner, preserving the canonical DFS order so the result is a
    /// load→save fixed point). Returns (boxes reshaped in place, colliders moved to a child).
    /// </summary>
    private static (int BoxesInPlace, int MovedToChild) MigrateEntities(SceneData scene)
    {
        var originals = scene.Entities;
        var count = originals.Count;

        // Resolve each entity's parent OBJECT up front (an in-scope index → the entry; else null = root),
        // so parent links survive the reorder without index bookkeeping.
        var parentOf = new Dictionary<SceneEntityData, SceneEntityData?>(count);
        var hasChildren = new bool[count];
        for (var i = 0; i < count; i++)
        {
            var p = originals[i].Parent;
            if (p is { } pi && pi >= 0 && pi < count) { parentOf[originals[i]] = originals[pi]; hasChildren[pi] = true; }
            else parentOf[originals[i]] = null;
        }

        var boxesInPlace = 0;
        var movedToChild = 0;

        // Each original entry maps to itself plus (in order) any new collider children spawned from it.
        var spawnedChildren = new Dictionary<SceneEntityData, List<SceneEntityData>>();

        for (var i = 0; i < count; i++)
        {
            var e = originals[i];
            var comps = e.Components;

            var hasBox = comps.TryGetValue(BoxColliderKey, out var boxEl);
            var hasConvex = comps.TryGetValue(ConvexColliderKey, out var convexEl);
            if (!hasBox && !hasConvex) continue;

            // Does the collider move to a NEW child, or does THIS entity stay the collider entity?
            //
            // Reshape/keep in place when the entity IS the collider/zone entity itself — a bare collision
            // node, a trigger zone (EntityInfo + collider), or a dialogue zone (game.DialogueZone + collider).
            // The zone identity (EntityInfo / DialogueZone) MUST stay on the SAME entity as the collider,
            // because CE-A's CollisionMessage carries that entity as ColliderA/B and consumers
            // (ZoneDialogueTriggerSystem) read the zone component off the collider entity (design pre-mortem
            // #4). Move to a child only when the entity is a VISUAL or PHYSICS-BODY entity (a sprite / a
            // RigidBody / a Velocity): there the collider is auxiliary and rides the body via the hierarchy
            // (the CE authoring-model fix, mirroring PlayerEntityFactory) — and such an entity is never a
            // zone. A multi-collider entity always splits (two shapes on one entity is undefined —
            // collision premise), giving each its own child.
            var colliderCount = (hasBox ? 1 : 0) + (hasConvex ? 1 : 0);
            var isVisualOrBody = comps.ContainsKey(SpriteInfoKey)
                                 || comps.ContainsKey(RigidBodyKey)
                                 || comps.ContainsKey(VelocityKey);
            var keepInPlace = !isVisualOrBody && colliderCount == 1;

            if (keepInPlace)
            {
                if (hasBox)
                {
                    // Reshape the box in place: bounds → centered size. Nudge THIS entity's Transform by the
                    // bounds centre so the centered box lands where the top-left box was, and shift any direct
                    // children back by the same offset so they stay put in world space.
                    var (cx, cy, sizeEl) = ReshapeBox(boxEl);
                    comps[BoxColliderKey] = sizeEl;
                    if (cx != 0f || cy != 0f)
                    {
                        comps[TransformKey] = NudgeTransformPosition(comps, cx, cy);
                        ShiftDirectChildren(originals, i, -cx, -cy);
                    }
                    boxesInPlace++;
                }
                // A convex kept in place is already a valid version-2 collider entity (its body is unchanged
                // and its vertices are entity-local) — nothing to restructure.
                continue;
            }

            // Visual / body / multi-collider entity: move each collider onto a new child collider entity.
            // The owner keeps its other components; the collider rides it via ChildOf.
            var children = new List<SceneEntityData>();
            if (hasBox)
            {
                var (cx, cy, sizeEl) = ReshapeBox(boxEl);
                children.Add(NewColliderChild(TransformElement(cx, cy), BoxColliderKey, sizeEl));
                comps.Remove(BoxColliderKey);
                movedToChild++;
            }
            if (hasConvex)
            {
                // Convex vertices are already collider-entity-local; the child sits at the owner's origin so
                // its world matrix equals the owner's — the shape is unchanged, verbatim (no re-basing).
                children.Add(NewColliderChild(TransformElement(0f, 0f), ConvexColliderKey, convexEl));
                comps.Remove(ConvexColliderKey);
                movedToChild++;
            }
            if (children.Count > 0) spawnedChildren[e] = children;
        }

        if (movedToChild == 0)
        {
            // Only in-place reshapes (or nothing): the entity list is structurally unchanged, so leave it.
            return (boxesInPlace, movedToChild);
        }

        // Rebuild entities[] in canonical order: each original entry, immediately followed by any child
        // colliders it spawned. Because the input is canonical (roots by id, DFS pre-order) and each new
        // child is a leaf inserted right after its owner, the result stays canonical → load→save is a fixed
        // point. Then recompute parent indices from object identity.
        var rebuilt = new List<SceneEntityData>(count + movedToChild);
        var childParent = new Dictionary<SceneEntityData, SceneEntityData>();
        foreach (var e in originals)
        {
            rebuilt.Add(e);
            if (!spawnedChildren.TryGetValue(e, out var kids)) continue;
            foreach (var kid in kids) { rebuilt.Add(kid); childParent[kid] = e; }
        }

        var indexOf = new Dictionary<SceneEntityData, int>(rebuilt.Count);
        for (var i = 0; i < rebuilt.Count; i++) indexOf[rebuilt[i]] = i;

        foreach (var e in rebuilt)
        {
            SceneEntityData? parent =
                childParent.TryGetValue(e, out var owner) ? owner
                : parentOf.TryGetValue(e, out var p) ? p
                : null;
            e.Parent = parent != null && indexOf.TryGetValue(parent, out var pi) ? pi : null;
        }

        scene.Entities = rebuilt;
        return (boxesInPlace, movedToChild);
    }

    /// <summary>Reshapes an old box body <c>{ bounds:[x,y,w,h], ... }</c> into the version-2
    /// <c>{ size:[w,h], activeLayers, passive, enabled }</c>, returning the box centre <c>(x+w/2, y+h/2)</c>
    /// (the collider entity's local position) and the canonical reshaped element.</summary>
    private static (float CentreX, float CentreY, JsonElement SizeElement) ReshapeBox(JsonElement box)
    {
        var bounds = box.GetProperty("bounds");
        float x = bounds[0].GetSingle(), y = bounds[1].GetSingle();
        float w = bounds[2].GetSingle(), h = bounds[3].GetSingle();

        var reshaped = CanonicalJson.SerializeToElement(new BoxSizeDto
        {
            Size = new[] { w, h },
            ActiveLayers = ReadIntArray(box, "activeLayers", new[] { -1 }),
            Passive = ReadBool(box, "passive", false),
            Enabled = ReadBool(box, "enabled", true),
        });
        return (x + w / 2f, y + h / 2f, reshaped);
    }

    /// <summary>Builds a canonical <c>core.Transform</c> element for a collider entity at local
    /// <c>(x, y)</c> (identity rotation/scale, zero origin) — the shape <c>TransformComponent</c>
    /// serializes to.</summary>
    private static JsonElement TransformElement(float x, float y) =>
        CanonicalJson.SerializeToElement(new TransformDto
        {
            Position = new[] { x, y },
            Rotation = 0f,
            Scale = new[] { 1f, 1f },
            Origin = new[] { 0f, 0f },
        });

    /// <summary>Returns a NEW <c>core.Transform</c> element equal to the owner's current transform but with
    /// its <c>position</c> nudged by <c>(dx, dy)</c> — preserving rotation/scale/origin verbatim so only the
    /// position line changes in the diff. Missing transform is treated as identity at the offset.</summary>
    private static JsonElement NudgeTransformPosition(Dictionary<string, JsonElement> comps, float dx, float dy)
    {
        if (!comps.TryGetValue(TransformKey, out var current))
            return TransformElement(dx, dy);

        var node = JsonNode.Parse(current.GetRawText())!.AsObject();
        var pos = node["position"]?.AsArray();
        var px = pos is { Count: >= 1 } ? pos[0]!.GetValue<float>() : 0f;
        var py = pos is { Count: >= 2 } ? pos[1]!.GetValue<float>() : 0f;
        node["position"] = new JsonArray(JsonValue.Create(px + dx), JsonValue.Create(py + dy));
        return CanonicalJson.SerializeToElement(node);
    }

    /// <summary>Shifts every DIRECT child of the entity at <paramref name="ownerIndex"/> (by parent index)
    /// by <c>(dx, dy)</c> in its local frame — used to keep children in the same world position after the
    /// owner's Transform was nudged for an in-place box reshape.</summary>
    private static void ShiftDirectChildren(List<SceneEntityData> entities, int ownerIndex, float dx, float dy)
    {
        for (var j = 0; j < entities.Count; j++)
        {
            if (entities[j].Parent != ownerIndex) continue;
            entities[j].Components[TransformKey] = NudgeTransformPosition(entities[j].Components, dx, dy);
        }
    }

    /// <summary>A fresh child collider entity carrying exactly a Transform + the one collider body.</summary>
    private static SceneEntityData NewColliderChild(JsonElement transform, string colliderKey, JsonElement collider) =>
        new()
        {
            Components = new Dictionary<string, JsonElement>
            {
                [TransformKey] = transform,
                [colliderKey] = collider,
            },
        };

    private static int[] ReadIntArray(JsonElement obj, string name, int[] fallback)
    {
        if (!obj.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return fallback;
        return arr.EnumerateArray().Select(e => e.GetInt32()).ToArray();
    }

    private static bool ReadBool(JsonElement obj, string name, bool fallback) =>
        obj.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean() : fallback;

    // DTOs mirroring EngineComponentSerializers' Transform/Box shapes (field names + order), so a migrated
    // component is byte-identical to one the live writer produces. Convex is copied verbatim (unchanged).
    private sealed class TransformDto
    {
        [JsonPropertyName("position")] public float[] Position { get; set; } = { 0f, 0f };
        [JsonPropertyName("rotation")] public float Rotation { get; set; }
        [JsonPropertyName("scale")] public float[] Scale { get; set; } = { 1f, 1f };
        [JsonPropertyName("origin")] public float[] Origin { get; set; } = { 0f, 0f };
    }

    private sealed class BoxSizeDto
    {
        [JsonPropertyName("size")] public float[] Size { get; set; } = { 0f, 0f };
        [JsonPropertyName("activeLayers")] public int[] ActiveLayers { get; set; } = { -1 };
        [JsonPropertyName("passive")] public bool Passive { get; set; }
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    }

    // ---- File / directory orchestration (dev-time tool; uses System.IO.File, like the importer) ----

    /// <summary>Per-file outcome for the CLI summary.</summary>
    public sealed class FileReport
    {
        public required string Path { get; init; }
        public required Result Result { get; init; }
        /// <summary>Whether the file was (or would be, under dry-run) written.</summary>
        public required bool Written { get; init; }
    }

    /// <summary>
    /// Migrates a single file at <paramref name="path"/> (reads, migrates, writes back unless
    /// <paramref name="dryRun"/>). Returns the per-file report. Throws on unparseable input.
    /// </summary>
    public static FileReport MigrateFile(string path, bool dryRun)
    {
        var json = File.ReadAllText(path);
        var result = Migrate(json, path);
        var willWrite = result.Changed && !dryRun;
        if (willWrite) File.WriteAllText(path, result.Json);
        return new FileReport { Path = path, Result = result, Written = willWrite };
    }

    /// <summary>
    /// Recursively migrates every <c>.mdscene</c>/<c>.mdprefab</c> under <paramref name="dir"/> (sorted for
    /// deterministic output). Returns one report per file.
    /// </summary>
    public static IReadOnlyList<FileReport> MigrateDirectory(string dir, bool dryRun)
    {
        var reports = new List<FileReport>();
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => MigratableExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal);
        foreach (var file in files)
            reports.Add(MigrateFile(file, dryRun));
        return reports;
    }

    /// <summary>Dispatches a path to <see cref="MigrateFile"/> (a file) or <see cref="MigrateDirectory"/>
    /// (a directory). Throws <see cref="FileNotFoundException"/> when the path does not exist.</summary>
    public static IReadOnlyList<FileReport> MigratePath(string path, bool dryRun)
    {
        if (Directory.Exists(path)) return MigrateDirectory(path, dryRun);
        if (File.Exists(path)) return new[] { MigrateFile(path, dryRun) };
        throw new FileNotFoundException($"[migrate-colliders] Path not found: '{path}'.", path);
    }
}
