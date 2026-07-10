#nullable enable
using System;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// The <b>fail-loud scene-format version gate</b> (CE-B, colliders-as-entities). The one place that
/// decides whether a <see cref="SceneData"/> read <b>from a file</b> is loadable by the current engine.
///
/// <para>The colliders-as-entities model (CE-A) reshaped the collider components: a
/// <c>core.BoxCollider</c> body is now <c>{ size }</c> (centered on the collider entity's Transform),
/// not the old <c>{ bounds }</c> (an embedded top-left offset). Reading a <b>version-1</b> collider
/// with the version-2 deserializer would silently produce a plausible-but-wrong shape (the old
/// <c>bounds</c> int[4] maps to no field, so <c>size</c> defaults to <c>[0,0]</c> — a zero-size box that
/// never collides). Pre-mortem #2 of the CE design: the refusal must trigger on <b>ANY</b> collider
/// component in a version-1 file, so the corruption can never load. The fix is the migrator
/// (<see cref="ColliderMigration"/>, the <c>monodreams migrate-colliders</c> CLI command).</para>
///
/// <para><b>File reads only.</b> <see cref="CheckFileLoad"/> is called by every path that deserializes a
/// <c>.mdscene</c>/<c>.mdprefab</c> from bytes (<c>SceneReaderSystem</c>'s file path, the prefab file
/// source). An <b>in-memory</b> <see cref="SceneData"/> — a Game-mode sandbox snapshot restored through
/// <c>LoadSceneRequest(SceneData)</c> — is version-agnostic and NOT guarded: it was produced by the live
/// (version-2) writer this session, never read off disk, so it can carry no legacy shape.</para>
/// </summary>
public static class SceneVersionGuard
{
    /// <summary>The current native scene/prefab format version — aliases <see cref="SceneData.CurrentVersion"/>
    /// (the constant lives on the dependency-free format type).</summary>
    public const int CurrentVersion = SceneData.CurrentVersion;

    /// <summary>
    /// Refuses <paramref name="scene"/> loudly when it is a legacy version (below
    /// <see cref="CurrentVersion"/>) that carries ANY embedded collider component
    /// (<c>core.BoxCollider</c> / <c>core.ConvexCollider</c>) — the migration gate. A version-1 file with
    /// no colliders is allowed (it loads and re-saves as version 2), and any version-2 file is allowed
    /// (its colliders are already the new shape). <paramref name="sourceName"/> names the file in the
    /// thrown message.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file is a legacy version and contains a collider.</exception>
    public static void CheckFileLoad(SceneData scene, string sourceName)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (scene.Version >= CurrentVersion) return; // version 2+: collider shapes are already current
        if (!ContainsCollider(scene)) return;         // legacy but collider-free: loads, re-saves as v2

        throw new InvalidOperationException(
            $"[level-editor] Scene '{sourceName}' is version {scene.Version} with legacy embedded colliders — " +
            "run 'monodreams migrate-colliders <file|dir>'. (A version-1 collider is an embedded box " +
            "'bounds' offset; the engine now models a collider as its own entity, so the file must be " +
            "migrated before it can load — reading it as-is would produce silently-wrong shapes.)");
    }

    /// <summary>Whether any entity in <paramref name="scene"/> carries a box or convex collider component
    /// body (the two shape keys the CE reshape changed).</summary>
    public static bool ContainsCollider(SceneData scene)
    {
        if (scene?.Entities == null) return false;
        foreach (var entity in scene.Entities)
        {
            if (entity.Components == null) continue;
            if (entity.Components.ContainsKey(EngineComponentSerializers.BoxColliderKey)) return true;
            if (entity.Components.ContainsKey(EngineComponentSerializers.ConvexColliderKey)) return true;
        }
        return false;
    }
}
