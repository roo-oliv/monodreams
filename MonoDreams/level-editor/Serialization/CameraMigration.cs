#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// The <b>camera migrator</b> (CM, camera-as-entity): rewrites a legacy version-2 <c>.mdscene</c> — whose
/// camera is a special top-level <c>camera</c> file block — into the version-3 shape where the camera is an
/// ordinary scene ENTITY (<c>EntityInfo("Camera")</c> + <c>Transform</c> + <c>core.Camera</c> zoom). It is
/// the second lift in the umbrella <c>monodreams migrate</c> chain (after <see cref="ColliderMigration"/>);
/// it lives beside <see cref="CanonicalJson"/> so its output is <b>byte-canonical</b> (the same policy every
/// native file is written through) and a <c>migrate → load → save</c> is a byte fixed point (CE pre-mortem
/// #3): the block's position/rotation/zoom are copied <b>verbatim</b> onto the camera entity, so there is no
/// float re-basing and no drift.
///
/// <para><b>The lift (per scene).</b></para>
/// <list type="bullet">
///   <item><b>A v2 scene WITH a <c>camera</c> block</b> — the block is removed and a camera entity is
///   APPENDED: <c>Transform</c> position/rotation from the block, <c>core.Camera</c> zoom from the block,
///   <c>EntityInfo("Camera")</c>. The entity is a new scene ROOT: its stable id is <c>max(root id) + 1</c>,
///   so it sorts LAST in the id-ordered <c>entities[]</c> and the migrate → load → save fixed point holds
///   (the reader restores the id, the writer preserves it).</item>
///   <item><b>A v2 scene that ALREADY has a camera entity</b> (a <c>core.Camera</c> in <c>entities[]</c>)
///   AND a stray block — the block is DROPPED in the entity's favour (<see cref="Result.CameraBlockDropped"/>
///   — reported by the CLI). No second camera is added (the one-camera rule).</item>
///   <item><b>A camera-less v2 scene</b> (no block, no camera entity) — the version is bumped AND a
///   <b>default camera entity at the origin</b> (position <c>[0,0]</c>, rotation 0, zoom 1) is added, so
///   every v3 file is <b>uniformly explicit</b> (a camera entity always present). The migrator has no live
///   view / virtual resolution, so it cannot frame content or fit-zoom the way the reader's runtime ensure
///   does; the origin/zoom-1 default is the deterministic authored baseline the designer then adjusts.</item>
///   <item><b>A prefab</b> (<paramref name="isPrefab"/>) — version bump ONLY. A prefab is a class and NEVER
///   carries a camera (a camera inside a prefab is multi-camera terrain); any stray block is dropped and no
///   camera entity is ever added.</item>
/// </list>
///
/// <para><b>Idempotent + loud.</b> A version-3 (or newer) input is a no-op (reported, bytes untouched).
/// Unparseable JSON throws loud. Like <see cref="ColliderMigration"/> it runs only at dev time (the CLI),
/// never in the game, and it expects the collider lift to have run first (the umbrella orders the chain), so
/// a scene it sees is already collider-current — it touches only the camera block + version.</para>
/// </summary>
public static class CameraMigration
{
    /// <summary>The version this lift stamps — the current scene format version (v3, camera-as-entity). A
    /// literal (not aliasing <see cref="SceneData.CurrentVersion"/>) so this source-linked CLI file stays
    /// pinned to the camera lift's target across future version bumps, mirroring
    /// <see cref="ColliderMigration.TargetVersion"/> (which pins the collider lift to v2).</summary>
    public const int TargetVersion = 3;

    // The stable component keys the camera entity carries. Kept in sync with EngineComponentSerializers
    // (a unit test asserts equality); duplicated here so this file stays dependency-free (System.Text.Json
    // only) and can be source-linked into the CLI, which cannot reference the engine assembly.
    internal const string CameraKey = "core.Camera";
    internal const string TransformKey = "core.Transform";
    internal const string EntityInfoKey = "core.EntityInfo";

    /// <summary>The <c>EntityInfo.Type</c> the camera entity carries (matches the reader's ensure-default
    /// and <c>CameraComponent</c> doc: <c>EntityInfoComponent("Camera")</c> — type "Camera", name null).</summary>
    internal const string CameraEntityType = "Camera";

    /// <summary>Outcome of applying the camera lift to one file's content.</summary>
    public sealed class Result
    {
        /// <summary>The (possibly rewritten) canonical JSON. Equals the input verbatim on a no-op.</summary>
        public required string Json { get; init; }

        /// <summary>Whether the bytes changed (a real migration happened).</summary>
        public required bool Changed { get; init; }

        /// <summary>Whether the input was already version 3+ (an idempotent no-op).</summary>
        public required bool AlreadyCurrent { get; init; }

        /// <summary>A <c>camera</c> block was lifted into a new camera entity.</summary>
        public bool CameraBlockLifted { get; init; }

        /// <summary>A camera-less scene got a default camera entity at the origin.</summary>
        public bool DefaultCameraAdded { get; init; }

        /// <summary>A stray <c>camera</c> block was dropped because a camera entity already existed.</summary>
        public bool CameraBlockDropped { get; init; }

        /// <summary>The input was a prefab (version bump only — never a camera).</summary>
        public bool IsPrefab { get; init; }
    }

    /// <summary>
    /// Migrates one file's JSON content from the version-2 camera-block shape to version 3. Returns the
    /// (possibly rewritten) canonical bytes plus a summary. A version-3+ input is returned unchanged
    /// (<see cref="Result.AlreadyCurrent"/>). Throws <see cref="InvalidOperationException"/> on JSON that
    /// does not parse as a <see cref="SceneData"/>.
    /// </summary>
    /// <param name="json">The file content.</param>
    /// <param name="sourceName">A display name for the file (used only in the thrown error message).</param>
    /// <param name="isPrefab">Whether the file is a <c>.mdprefab</c> — a prefab gets a version bump only and
    /// NEVER a camera entity.</param>
    public static Result Migrate(string json, string sourceName, bool isPrefab = false)
    {
        SceneData? scene;
        try
        {
            scene = CanonicalJson.Deserialize<SceneData>(json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"[migrate] Could not parse '{sourceName}' as a native scene/prefab: {ex.Message}", ex);
        }

        if (scene == null)
            throw new InvalidOperationException(
                $"[migrate] '{sourceName}' deserialized to null — not a valid native scene/prefab.");

        if (scene.Version >= TargetVersion)
            return new Result { Json = json, Changed = false, AlreadyCurrent = true, IsPrefab = isPrefab };

        var hadBlock = scene.Camera != null;
        var lifted = false;
        var defaultAdded = false;
        var dropped = false;

        if (isPrefab)
        {
            // A prefab is a class — it never carries a camera. Drop any stray block, add no entity.
            scene.Camera = null;
            dropped = hadBlock;
        }
        else if (HasCameraEntity(scene))
        {
            // A camera entity already exists — the block (if any) is redundant; drop it in the entity's favour.
            scene.Camera = null;
            dropped = hadBlock;
        }
        else if (scene.Camera is { } block)
        {
            // Lift the block into a camera entity (position/rotation/zoom copied verbatim — no drift).
            AppendCameraEntity(scene, block.Position, block.Rotation, block.Zoom);
            scene.Camera = null;
            lifted = true;
        }
        else
        {
            // Camera-less: add the uniformly-explicit default camera at the origin.
            AppendCameraEntity(scene, new[] { 0f, 0f }, 0f, 1f);
            defaultAdded = true;
        }

        scene.Version = TargetVersion;

        var migrated = CanonicalJson.Serialize(scene);
        return new Result
        {
            Json = migrated,
            Changed = migrated != json,
            AlreadyCurrent = false,
            CameraBlockLifted = lifted,
            DefaultCameraAdded = defaultAdded,
            CameraBlockDropped = dropped,
            IsPrefab = isPrefab,
        };
    }

    /// <summary>Whether any entity in <paramref name="scene"/> already carries a <c>core.Camera</c> body
    /// (a camera ENTITY — the v3 shape).</summary>
    internal static bool HasCameraEntity(SceneData scene)
    {
        foreach (var e in scene.Entities)
            if (e.Components != null && e.Components.ContainsKey(CameraKey)) return true;
        return false;
    }

    /// <summary>Appends a camera ROOT entity to <paramref name="scene"/> — <c>Transform</c> at
    /// <c>(position, rotation)</c>, <c>EntityInfo("Camera")</c>, <c>core.Camera</c> zoom — with a stable id of
    /// <c>max(root id) + 1</c> so it sorts LAST in the id-ordered <c>entities[]</c>. The components are
    /// byte-identical to what <c>EngineComponentSerializers</c> writes for such an entity, so a
    /// migrate → load → save is a byte fixed point.</summary>
    private static void AppendCameraEntity(SceneData scene, float[] position, float rotation, float zoom)
    {
        var maxRootId = -1;
        foreach (var e in scene.Entities)
            if (e.Id is { } id) maxRootId = Math.Max(maxRootId, id);

        scene.Entities.Add(new SceneEntityData
        {
            Id = maxRootId + 1, // a new root, sorts last (writer orders entities[] by root id)
            Components = new Dictionary<string, JsonElement>
            {
                [TransformKey] = TransformElement(position, rotation),
                [EntityInfoKey] = EntityInfoElement(CameraEntityType),
                [CameraKey] = CameraElement(zoom),
            },
        });
    }

    /// <summary>A canonical <c>core.Transform</c> element at <c>(position, rotation)</c>, identity
    /// scale/origin — the shape <c>TransformComponent</c> serializes to. Position is copied verbatim.</summary>
    private static JsonElement TransformElement(float[] position, float rotation) =>
        CanonicalJson.SerializeToElement(new TransformDto
        {
            Position = position.Length >= 2 ? new[] { position[0], position[1] } : new[] { 0f, 0f },
            Rotation = rotation,
            Scale = new[] { 1f, 1f },
            Origin = new[] { 0f, 0f },
        });

    /// <summary>A canonical <c>core.EntityInfo</c> element with the given type and a null name (the name is
    /// null-omitted by <see cref="CanonicalJson"/>) — matching <c>new EntityInfoComponent("Camera")</c>.</summary>
    private static JsonElement EntityInfoElement(string type) =>
        CanonicalJson.SerializeToElement(new EntityInfoDto { Type = type, Name = null });

    /// <summary>A canonical <c>core.Camera</c> element carrying the zoom (copied verbatim).</summary>
    private static JsonElement CameraElement(float zoom) =>
        CanonicalJson.SerializeToElement(new CameraDto { Zoom = zoom });

    // DTOs mirroring EngineComponentSerializers' Transform / EntityInfo / Camera shapes (field names +
    // order), so a lifted camera entity is byte-identical to one the live writer produces.
    private sealed class TransformDto
    {
        [JsonPropertyName("position")] public float[] Position { get; set; } = { 0f, 0f };
        [JsonPropertyName("rotation")] public float Rotation { get; set; }
        [JsonPropertyName("scale")] public float[] Scale { get; set; } = { 1f, 1f };
        [JsonPropertyName("origin")] public float[] Origin { get; set; } = { 0f, 0f };
    }

    private sealed class EntityInfoDto
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class CameraDto
    {
        [JsonPropertyName("zoom")] public float Zoom { get; set; } = 1f;
    }
}
