#nullable enable
using System;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// The <b>fail-loud scene-format version gate</b> (CE-B colliders, CM camera). The one place that decides
/// whether a <see cref="SceneData"/> read <b>from a file</b> is loadable by the current engine. Two
/// legacy-migration gates, applied in version order:
///
/// <para><b>v1 embedded-collider gate (CE-B).</b> The colliders-as-entities model reshaped the collider
/// components: a <c>core.BoxCollider</c> body is now <c>{ size }</c> (centered on the collider entity's
/// Transform), not the old <c>{ bounds }</c> (an embedded top-left offset). Reading a <b>version-1</b>
/// collider with the current deserializer would silently produce a plausible-but-wrong shape (the old
/// <c>bounds</c> int[4] maps to no field, so <c>size</c> defaults to <c>[0,0]</c> — a zero-size box that
/// never collides). The refusal triggers on ANY collider component in a version-1 file. The fix is the
/// collider migrator (the <c>monodreams migrate-colliders</c> CLI command).</para>
///
/// <para><b>v2→v3 camera-block gate (CM).</b> The camera is a scene ENTITY now, not a special
/// <c>camera</c> file block. A legacy file that still carries a <c>camera</c> block would silently lose
/// its authored camera on load (the writer drops the block on re-save), so any legacy file carrying one is
/// refused → run the umbrella <c>monodreams migrate</c> (which lifts the block into a camera entity). A
/// version-2 file WITHOUT a camera block loads and re-saves as version 3.</para>
///
/// <para><b>File reads only.</b> <see cref="CheckFileLoad"/> is called by every path that deserializes a
/// <c>.mdscene</c>/<c>.mdprefab</c> from bytes (<c>SceneReaderSystem</c>'s file path, the prefab file
/// source). An <b>in-memory</b> <see cref="SceneData"/> — a Game-mode sandbox snapshot restored through
/// <c>LoadSceneRequest(SceneData)</c> — is version-agnostic and NOT guarded: it was produced by the live
/// (current-version) writer this session, never read off disk, so it can carry no legacy shape.</para>
/// </summary>
public static class SceneVersionGuard
{
    /// <summary>The current native scene/prefab format version — aliases <see cref="SceneData.CurrentVersion"/>
    /// (the constant lives on the dependency-free format type).</summary>
    public const int CurrentVersion = SceneData.CurrentVersion;

    /// <summary>
    /// Refuses <paramref name="scene"/> loudly when it is a legacy version below <see cref="CurrentVersion"/>
    /// that hits either migration gate: a <b>version-1</b> file carrying an embedded collider
    /// (<c>core.BoxCollider</c> / <c>core.ConvexCollider</c>) → run <c>monodreams migrate-colliders</c>; a
    /// legacy file carrying a <c>camera</c> block → run <c>monodreams migrate</c>. A version-3 file is
    /// allowed; a legacy file that hits neither gate (no v1 collider, no camera block) is allowed and loads
    /// (re-saving stamps the current version). <paramref name="sourceName"/> names the file in the thrown
    /// message.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file is a legacy version and hits a migration gate.</exception>
    public static void CheckFileLoad(SceneData scene, string sourceName)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (scene.Version >= CurrentVersion) return; // current: nothing to migrate

        // v1 embedded-collider gate (CE-B): only version-1 files (a version-2 collider is already current).
        if (scene.Version < 2 && ContainsCollider(scene))
            throw new InvalidOperationException(
                $"[level-editor] Scene '{sourceName}' is version {scene.Version} with legacy embedded colliders — " +
                "run 'monodreams migrate-colliders <file|dir>'. (A version-1 collider is an embedded box " +
                "'bounds' offset; the engine now models a collider as its own entity, so the file must be " +
                "migrated before it can load — reading it as-is would produce silently-wrong shapes.)");

        // v2→v3 camera-block gate (CM): a legacy file carrying a camera block would lose its authored camera.
        if (scene.Camera != null)
            throw new InvalidOperationException(
                $"[level-editor] Scene '{sourceName}' is version {scene.Version} with a legacy 'camera' block — " +
                "run 'monodreams migrate <file|dir>'. (The camera is a scene entity now; the block must be " +
                "lifted into a 'Camera' entity before it can load — re-saving as-is would silently drop the " +
                "authored camera.)");

        // Legacy but clean (no v1 collider, no camera block): loads, and re-saving stamps the current version.
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
