#nullable enable
using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Navigation;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The <b>ONE</b> ensure-exactly-one-camera implementation (CM one-camera rule), shared by every scene
/// context that must end up with a camera entity — never a second copy (CM-D). Two callers reuse it:
/// <list type="bullet">
///   <item><see cref="SceneReaderSystem"/> — post-load, over the entities a real scene load reconstructed
///   (frames the default camera on that content's AABB / fit-zoom);</item>
///   <item><see cref="Composition.NativeLevelLoader.TryPublishSceneLoad"/>'s <b>FILE-ABSENT branch</b> —
///   a code-built screen bound to a scene id whose file does not exist yet (LevelSelection's
///   <c>level_selection</c>, every Demos screen) never runs the reader, so it would otherwise have zero
///   camera entities and no "Camera" tree row. The absent branch runs this ensure with no content /
///   no view, so the default camera lands at the origin.</item>
/// </list>
///
/// <para><b>Idempotent by construction:</b> a no-op when the world already has ANY
/// <see cref="CameraComponent"/> entity (a camera restored from the file, a prior ensure, a
/// snapshot restore). That convergence is what keeps a later real file load, a
/// <see cref="Composition.EditorTransport.Restart"/> (sweep + re-run), a Game-tab round-trip, and a
/// cross-screen pending-activation restore all landing on exactly one camera (CM pre-mortem #3).</para>
///
/// <para><b>Prefab contexts are excluded by the CALLER:</b> a prefab is a class and carries no camera
/// (the writer/expander refuse one), so the reader gates this off via
/// <c>LoadSceneRequest.SuppressCameraEnsure</c>, and <c>TryPublishSceneLoad</c> is never invoked for a
/// prefab context (a prefab tab's content-load is the in-memory suppressed reader path). This helper is
/// therefore only ever reached for genuine scene contexts.</para>
/// </summary>
public static class SceneCameraEnsure
{
    /// <summary>Fit margin used when framing the default camera on content (10% padding), matching
    /// <see cref="CameraNavSystem"/>'s frame-scene feel and the reader's view-framing margin.</summary>
    private const float FrameMargin = 0.9f;

    /// <summary>
    /// Ensures the world has exactly ONE camera ENTITY. If any live entity already carries a
    /// <see cref="CameraComponent"/> this is a no-op (idempotent). Otherwise it creates a default camera
    /// root: <c>EntityInfoComponent("Camera")</c> + a <see cref="TransformComponent"/> + a
    /// <see cref="CameraComponent"/> + <see cref="SceneObjectComponent"/> so it saves in <c>entities[]</c>
    /// like any scene root.
    ///
    /// <para>When <paramref name="content"/> is supplied the camera is positioned on that content's AABB
    /// centre (and, when a live <paramref name="camera"/> view supplies the immutable virtual size,
    /// fit-zoomed to frame it); with no content it lands at the origin with zoom 1 — the FILE-ABSENT
    /// branch's case, where the screen's own content is code-built (never scene content).</para>
    /// </summary>
    /// <param name="world">The world to ensure a camera entity in.</param>
    /// <param name="camera">The live editor VIEW (optional) — used only to fit-zoom the default camera on
    /// content; null (the absent branch, and the pure-logic tests) leaves zoom at the component default.</param>
    /// <param name="content">The loaded content entities to frame on (optional). Null / empty → the camera
    /// lands at the origin.</param>
    public static void EnsureCameraEntity(World world, Camera? camera = null, IReadOnlyList<Entity>? content = null)
    {
        using (var existing = world.GetEntities().With<CameraComponent>().AsSet())
            if (existing.Count > 0) return; // already exactly one (or restored from the file) — idempotent

        // Reuse the auto-frame math: centre on the loaded content's AABB (origin when there is none), and
        // fit-zoom when a live view supplies the immutable virtual size (else the CameraComponent default).
        var position = Vector2.Zero;
        var zoom = 1f;
        if (content != null)
        {
            var quads = new List<Vector2[]>();
            foreach (var entity in content)
            {
                if (!entity.Has<SpriteInfoComponent>() || !entity.Has<TransformComponent>()) continue;
                quads.Add(GizmoTransform.SpriteWorldQuad(
                    entity.Get<TransformComponent>(), entity.Get<SpriteInfoComponent>()));
            }

            if (CameraNav.ContentBounds(quads) is { } aabb)
            {
                position = CameraNav.Center(aabb);
                if (camera != null && aabb.Width > 0 && aabb.Height > 0)
                    zoom = CameraNav.FitZoom(aabb, camera.LayoutWidth, camera.LayoutHeight,
                        FrameMargin, CameraNavSystem.DefaultMinZoom, CameraNavSystem.DefaultMaxZoom);
            }
        }

        var cameraEntity = world.CreateEntity();
        cameraEntity.Set(new EntityInfoComponent("Camera"));
        cameraEntity.Set(new TransformComponent(position));
        cameraEntity.Set(new CameraComponent { Zoom = zoom });
        cameraEntity.Set(new SceneObjectComponent()); // a scene root — saved in entities[] like everything else

        Logger.Info(
            $"[level-editor] Scene had no camera entity — created a default 'Camera' at {position} " +
            $"(zoom {zoom:F3}). It saves with the scene (CM one-camera rule).");
    }
}
