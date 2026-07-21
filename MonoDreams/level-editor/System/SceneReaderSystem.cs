#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Navigation;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.Platform;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// Loads a native MonoDreams scene in response to a <see cref="LoadSceneRequest"/>. This is the
/// read half of Wave 3 — deliberately on its <b>own</b> message, never <c>LoadLevelRequest</c>, so
/// it never triggers (or, on failure, clobbers) the LDtk content path
/// (<c>LevelLoadRequestSystem</c>'s unconditional <c>Content.Load</c> / <c>Remove&lt;CurrentLevelComponent&gt;</c>).
///
/// <para>It reconstructs entities from serialized components, never by re-running factories. Two
/// passes (delegated to <see cref="SceneSerializer.Deserialize"/>): create every entity +
/// deserialize its components, then wire the parent graph from the recorded indices. After
/// deserialize it <b>re-tags each scene root</b> (see <see cref="RetagSceneRoots"/>) with
/// <see cref="SceneObjectComponent"/> — the transient save-root tag is never serialized, so
/// without this a reloaded scene would have zero tagged roots and the next Save would write an
/// empty scene, silently losing every edit made since loading. It then <b>rehydrates</b> each
/// sprite's <c>Texture2D</c> from its <see cref="SpriteInfoComponent.AssetKey"/> via the content
/// loader (the in-memory deserialize leaves <c>SpriteSheet</c> null on purpose — it has no
/// <c>ContentManager</c>).</para>
///
/// <para>It then <b>restores the transient <c>DrawComponent</c></b> on every reconstructed sprite (see
/// <see cref="RestoreDrawComponents"/>): <c>DrawComponent</c> is deliberately never serialized (its
/// sprite fields are re-prepped every frame and its <c>LayerDepth</c> is per-frame-derived), so a
/// reconstructed sprite has <c>SpriteInfoComponent</c> + <c>TransformComponent</c> but no
/// <c>DrawComponent</c> — and <c>SpritePrepSystem</c>'s <c>[With(DrawComponent, …)]</c> query then
/// never preps it, so it never draws and the Main target stays the backbuffer clear color. Restoring
/// the pairing on load (mirroring <c>SpritePropFactory</c>) fixes the "reloaded scene renders blank"
/// bug.</para>
///
/// <para>Finally, when a <see cref="Camera"/> is supplied and the loaded scene has <b>no active
/// camera-follow target</b> (a dressed prop-only scene has no player), it <b>auto-frames the camera</b>
/// on the loaded content's AABB (via the pure <see cref="CameraNav"/> frame-scene math) so the scene is
/// centered + visible instead of the camera sitting at (0,0) while the content is elsewhere. When there
/// IS an active follow target it leaves the camera alone (<c>CameraFollowSystem</c> owns it).</para>
///
/// <para>It <b>fails loud</b>: a component key in the file with no registered serializer throws from
/// the registry (the load aborts with a clear message rather than silently dropping data); the
/// exception is logged and surfaced.</para>
/// </summary>
public sealed class SceneReaderSystem : ISystem<GameState>
{
    /// <summary>Fit margin used when auto-framing loaded content (10% padding), matching
    /// <see cref="CameraNavSystem"/>'s frame-scene feel.</summary>
    private const float FrameMargin = 0.9f;

    private readonly World _world;
    private readonly SceneSerializer _serializer;
    private readonly ContentManager _content;
    private readonly Func<string, Texture2D> _loadTexture;
    private readonly Func<string, Texture2D?>? _fileTextureLoader;
    private readonly Camera? _camera;
    private readonly PrefabExpander? _prefabExpander;
    private readonly bool _ensureSingleCamera;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Whether a scene was ever loaded into this world this session (set true on the first
    /// successful <see cref="LoadSceneRequest"/>). The empty-save guard reads it: zero scene roots +
    /// never-loaded = "nothing to save" (refused), but a designer who deliberately emptied a
    /// <b>loaded</b> scene may still save it empty. Never reset — a session-scoped one-way flag.</summary>
    public bool SceneWasLoaded { get; private set; }

    /// <summary>
    /// Subscribes to <see cref="LoadSceneRequest"/>. <paramref name="content"/> resolves the scene's
    /// content path and rehydrates textures; pass an explicit <paramref name="loadTexture"/> only to
    /// override the default <c>content.Load&lt;Texture2D&gt;</c> (e.g. a test stub with no GraphicsDevice).
    /// <paramref name="fileTextureLoader"/> handles <c>file:</c> asset keys (runtime-loaded PNGs from
    /// the asset drop folder — see <see cref="Assets.FileAssetKey"/>); wire
    /// <c>FileAssetTextureLoader.Load</c>, which returns a visible magenta placeholder (with a loud
    /// warning) for a missing file so the entity is never silently invisible.
    /// <paramref name="camera"/> (optional; the overlay supplies the screen's live camera / VIEW)
    /// auto-frames that VIEW on the loaded content so it is visible on load; null (the pure round-trip
    /// tests) skips all camera positioning. The authored camera is a scene ENTITY now (CM): it rides the
    /// serialized <c>entities[]</c> like everything else, so there is no camera-block seam — in Play
    /// <c>CameraSyncSystem</c> drives the VIEW from that entity, and in Edit the VIEW stays the editor's
    /// free camera (this framing just centres it on load).
    /// </summary>
    /// <param name="ensureSingleCamera">When true (the editor + shipped game readers — CM), a loaded scene
    /// with NO <c>core.Camera</c> entity gets a default one created post-load
    /// (<c>EntityInfo("Camera")</c> + Transform positioned by the auto-frame math + <c>core.Camera</c>,
    /// <c>SceneObjectComponent</c>-tagged so it saves). Idempotent (a scene that already has one is left
    /// alone), and never applied to a prefab context (a prefab has no camera — see
    /// <see cref="LoadSceneRequest.SuppressCameraEnsure"/>). Left <c>false</c> on the pure round-trip test
    /// path so serialization-fidelity tests round-trip exactly what they wrote.</param>
    public SceneReaderSystem(
        World world,
        SceneSerializer serializer,
        ContentManager content,
        Func<string, Texture2D>? loadTexture = null,
        Func<string, Texture2D?>? fileTextureLoader = null,
        Camera? camera = null,
        PrefabExpander? prefabExpander = null,
        bool ensureSingleCamera = false)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _content = content; // may be null when an explicit loadTexture is supplied (tests)
        _loadTexture = loadTexture ?? (key => _content.Load<Texture2D>(key));
        _fileTextureLoader = fileTextureLoader;
        _camera = camera;
        _prefabExpander = prefabExpander;
        _ensureSingleCamera = ensureSingleCamera;
        _world.Subscribe<LoadSceneRequest>(On);
    }

    [Subscribe]
    public void On(in LoadSceneRequest message)
    {
        if (!IsEnabled) return;

        // In-memory restore (UX2-F): an already-built SceneData (the Game-mode sandbox snapshot) is
        // reconstructed through the EXACT same pipeline as a file load — no second restore path
        // (pre-mortem #2). Otherwise read + deserialize the JSON from the path.
        if (message.Scene is { } inMemory)
        {
            Logger.Info("[level-editor] Restoring scene from an in-memory snapshot (no file read).");
            Load(inMemory, "<in-memory>", message.SuppressCameraEnsure);
            return;
        }

        var path = message.Path;
        Logger.Info($"[level-editor] Loading scene '{path}' (fromContent={message.FromContent}).");

        SceneData? scene;
        try
        {
            var json = ReadSceneJson(path, message.FromContent);
            scene = CanonicalJson.Deserialize<SceneData>(json);
        }
        catch (Exception ex)
        {
            Logger.Error($"[level-editor] Error reading scene '{path}': {ex.Message}\n{ex.StackTrace}");
            throw;
        }

        if (scene == null)
        {
            Logger.Error($"[level-editor] Scene '{path}' deserialized to null; aborting load.");
            return;
        }

        // Fail-loud version gate (CE-B colliders, CM camera; pre-mortem #2): a legacy file that would
        // deserialize to silently-wrong shapes (a v1 embedded collider) or silently drop its authored camera
        // (a legacy 'camera' block) is refused with the migrator hint. Only FILE reads guard — the in-memory
        // snapshot path above is version-agnostic. Log the refusal so the fail-loud is observable in the log,
        // then re-throw (the load aborts before any entity is created).
        try
        {
            SceneVersionGuard.CheckFileLoad(scene, path);
        }
        catch (InvalidOperationException ex)
        {
            Logger.Error(ex.Message);
            throw;
        }

        Load(scene, path, message.SuppressCameraEnsure);
    }

    /// <summary>
    /// Reconstructs <paramref name="scene"/> into the world and finishes the load — the ONE restore
    /// implementation shared by the file path and the UX2-F in-memory snapshot restore
    /// (<see cref="LoadSceneRequest(SceneData)"/>): two-pass create + deserialize + wire-parents
    /// (from components, not factories), re-tag roots, rehydrate textures (content AND <c>file:</c>
    /// keys), restore the transient <c>DrawComponent</c>, auto-frame the VIEW on the content, then ensure
    /// exactly one camera ENTITY. Throws loud on an unregistered component key.
    /// <paramref name="pathForLogging"/> only names the source in the log line.
    /// </summary>
    private void Load(SceneData scene, string pathForLogging, bool suppressCameraEnsure = false)
    {
        try
        {
            // Two-pass create + deserialize + wire-parents (reconstructs from components, not factories).
            // When a prefab expander is composed, each compact `prefab` entry is expanded into a full
            // linked-instance subtree (the ONE expansion implementation, shared with the factory +
            // propagation); its prefab-owned children are finished inside the expander (rehydrate +
            // DrawComponent), since the top-level loop here never sees them. Throws loud on an unregistered
            // component key OR a prefab entry with no expander — do not swallow that here; let it surface.
            var created = _prefabExpander != null
                ? _prefabExpander.ExpandScene(_world, scene)
                : _serializer.Deserialize(_world, scene);

            RetagSceneRoots(scene, created);

            RehydrateTextures(created);

            RestoreDrawComponents(created);

            FrameViewOnLoad(created);

            // CM one-camera rule: a real scene load ensures exactly one camera ENTITY exists (idempotent).
            // Skipped for a prefab context (a prefab is a class, has no camera — pre-mortem #8) and on the
            // pure round-trip path (ensureSingleCamera false → serialization-fidelity tests are untouched).
            if (_ensureSingleCamera && !suppressCameraEnsure)
                EnsureCameraEntity(created);

            SceneWasLoaded = true; // a real load happened → the empty-save guard now permits an empty save

            Logger.Info($"[level-editor] Loaded scene '{pathForLogging}': {created.Count} entities.");
        }
        catch (Exception ex)
        {
            Logger.Error($"[level-editor] Error loading scene '{pathForLogging}': {ex.Message}\n{ex.StackTrace}");
            throw;
        }
    }

    /// <summary>
    /// Reads the scene JSON. A content-relative path is read through <see cref="TitleContainer"/>
    /// (the portable content-stream primitive — a file read on desktop, an HTTP fetch on web);
    /// a host-filesystem path goes through
    /// <see cref="IPlatformServices.ReadAllText"/>.
    /// </summary>
    private string ReadSceneJson(string path, bool fromContent)
    {
        if (fromContent)
        {
            var contentPath = _content != null ? Path.Combine(_content.RootDirectory, path) : path;
            using var stream = TitleContainer.OpenStream(contentPath);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        return PlatformServices.Current.ReadAllText(path);
    }

    /// <summary>
    /// Re-tags each reconstructed <b>scene root</b> with <see cref="SceneObjectComponent"/> so a
    /// loaded scene re-saves identically — <c>save → load → edit → save</c> is a <b>fixed point</b>.
    /// <see cref="SceneObjectComponent"/> is transient editor state (never registered / serialized),
    /// so a freshly reconstructed scene carries no save-root tags; the next Save (which only writes
    /// <c>[With(SceneObjectComponent)]</c> roots + their closure) would otherwise write an empty
    /// scene and silently drop every edit made since loading. It also <b>restores each root's
    /// persisted stable scene-local id</b> (<see cref="SceneEntityIdComponent"/> from the entry's
    /// <see cref="SceneEntityData.Id"/>), so the next Save reuses the same ids and keeps
    /// <c>entities[]</c> byte-stable — <c>load → save</c> equals the source file.
    ///
    /// <para>A scene root is a <b>top-level <c>entities[]</c> entry</b> — one with no in-scope parent
    /// (<see cref="SceneEntityData.Parent"/> null or out of the created range, mirroring the exact
    /// in-scope test <see cref="SceneSerializer.Deserialize"/> uses to wire parents). This matches
    /// <see cref="SceneWriter.CollectMembership"/> precisely: those roots seed the membership closure,
    /// so re-tagging exactly them reproduces the same serialized set on the next Save. A
    /// <c>ChildOf</c> descendant is deliberately NOT re-tagged (the writer auto-closes it from its
    /// tagged ancestor) and carries no stable id of its own. An entry with no <c>id</c> (an
    /// older file predating stable ids) is left un-stamped — the writer assigns it a fresh id on the
    /// next Save. Bake products (<c>BakedProductComponent</c>, e.g. a boundary's segment
    /// colliders) never reach this loop — they are never serialized, so they never appear in
    /// <c>entities[]</c> / <paramref name="created"/>; they regenerate on load and the writer
    /// excludes them from any tagged root's closure, so re-tagging cannot reintroduce them into the
    /// save.</para>
    /// </summary>
    private static void RetagSceneRoots(SceneData scene, List<Entity> created)
    {
        // Self-heal duplicate stable ids in the FILE (PF-F): a corrupt scene (e.g. a double-load that
        // was saved) can carry two roots with the SAME `id`. Restoring both verbatim keeps the collision
        // stable across load → save (the writer preserves restored ids), so the diff never recovers.
        // Re-stamp the LATER duplicates to the next free id in-world (the load still succeeds); the next
        // Save then writes distinct ids and the scene is byte-stable again. First pass: the max id present.
        var maxId = -1;
        for (var i = 0; i < created.Count && i < scene.Entities.Count; i++)
            if (!HasInScopeParent(scene, created.Count, i) && scene.Entities[i].Id is { } present)
                maxId = Math.Max(maxId, present);
        var nextFree = maxId + 1;
        var usedIds = new HashSet<int>();
        var restamped = 0;

        for (var i = 0; i < created.Count && i < scene.Entities.Count; i++)
        {
            if (HasInScopeParent(scene, created.Count, i)) continue;

            created[i].Set(new SceneObjectComponent());
            if (scene.Entities[i].Id is { } id)
            {
                if (!usedIds.Add(id))
                {
                    var healed = nextFree++;
                    usedIds.Add(healed);
                    Logger.Warning(
                        $"[level-editor] Scene had a duplicate stable id {id} — re-stamped a later root " +
                        $"to {healed} on load (self-healing a corrupt/double-loaded scene). The next Save " +
                        "writes distinct ids.");
                    id = healed;
                    restamped++;
                }
                created[i].Set(new SceneEntityIdComponent(id));
            }
        }

        if (restamped > 0)
            Logger.Warning(
                $"[level-editor] Loaded scene self-healed {restamped} duplicate stable id(s) — Save to " +
                "persist the repair.");
    }

    /// <summary>Whether the entry at <paramref name="index"/> has an in-scope parent (a <c>ChildOf</c>
    /// descendant), mirroring <see cref="SceneSerializer.Deserialize"/>'s in-scope test — a top-level
    /// entry (no in-scope parent) is a scene root.</summary>
    private static bool HasInScopeParent(SceneData scene, int createdCount, int index) =>
        scene.Entities[index].Parent is { } pi && pi >= 0 && pi < createdCount;

    /// <summary>
    /// Rehydrates the live <c>Texture2D</c> for every loaded entity whose <c>SpriteInfo.AssetKey</c>
    /// is set — delegated to <see cref="SceneRehydration.RehydrateTextures"/>, the ONE implementation
    /// shared with the prefab expander (so a reloaded scene and an expanded prefab instance rehydrate
    /// identically): <c>file:</c> keys through the file-asset loader (magenta placeholder for a missing
    /// file — fail loud, never invisible), everything else through the content loader.
    /// </summary>
    private void RehydrateTextures(List<Entity> entities) =>
        SceneRehydration.RehydrateTextures(entities, _loadTexture, _fileTextureLoader);

    /// <summary>
    /// Restores the transient <see cref="DrawComponent"/> on every reconstructed sprite that lacks one —
    /// the <c>SpriteInfoComponent ⇒ DrawComponent</c> pairing every renderable sprite needs (the
    /// "reloaded scene renders blank" bug otherwise). Delegated to
    /// <see cref="SceneRehydration.RestoreDrawComponents"/>, shared with the prefab expander.
    /// </summary>
    private static void RestoreDrawComponents(List<Entity> entities) =>
        SceneRehydration.RestoreDrawComponents(entities);

    /// <summary>
    /// Auto-frames the free VIEW on the loaded content so it is visible on load (CM). The authored camera
    /// is a scene ENTITY now — it rides <c>entities[]</c> like everything else and needs no camera-block
    /// seam. In Play <c>CameraSyncSystem</c> drives the VIEW from the camera entity; in Edit the VIEW is
    /// the editor's free camera and this framing centres it on the content on load. Left untouched when no
    /// live view was supplied (a pure round-trip test) or an <b>active</b>
    /// <see cref="CameraFollowTargetComponent"/> owns the camera (<c>CameraFollowSystem</c> in Play — the
    /// reader must not fight it). Safe for a prefab context too (a prefab has no follow target).
    /// </summary>
    private void FrameViewOnLoad(List<Entity> created)
    {
        if (_camera != null && !HasActiveFollowTarget())
            FrameViewOnContent(created);
    }

    /// <summary>
    /// Centres + zoom-fits the live VIEW on the loaded content's AABB (via the pure
    /// <see cref="CameraNav"/> frame-scene math, the same <see cref="CameraNavSystem"/> uses), so a
    /// loaded off-origin scene is visible instead of blank with the camera stuck at (0,0). No content →
    /// no-op. Callers guarantee <see cref="_camera"/> is non-null and no active follow target is present.
    /// </summary>
    private void FrameViewOnContent(List<Entity> entities)
    {
        var quads = new List<Vector2[]>();
        foreach (var entity in entities)
        {
            if (!entity.Has<SpriteInfoComponent>() || !entity.Has<TransformComponent>()) continue;
            quads.Add(GizmoTransform.SpriteWorldQuad(
                entity.Get<TransformComponent>(), entity.Get<SpriteInfoComponent>()));
        }

        if (CameraNav.ContentBounds(quads) is not { } aabb) return; // no content: leave the camera as-is

        _camera!.Position = CameraNav.Center(aabb);
        if (aabb.Width > 0 && aabb.Height > 0)
            _camera.Zoom = CameraNav.FitZoom(aabb, _camera.VirtualWidth, _camera.VirtualHeight,
                FrameMargin, CameraNavSystem.DefaultMinZoom, CameraNavSystem.DefaultMaxZoom);

        Logger.Info(
            $"[level-editor] Auto-framed the view on loaded content: center={_camera.Position}, zoom={_camera.Zoom:F3}.");
    }

    /// <summary>
    /// CM one-camera rule: ensures the loaded scene has exactly ONE camera ENTITY. If any live entity
    /// already carries a <see cref="CameraComponent"/> (loaded from the file, or a prior ensure) this is a
    /// no-op — idempotent (pre-mortem #3). Otherwise it creates a default camera root:
    /// <c>EntityInfoComponent("Camera")</c> + a <see cref="TransformComponent"/> positioned by the SAME
    /// auto-frame math the view uses (content AABB centre; origin for a content-less scene) + a
    /// <see cref="CameraComponent"/> (zoom = the fit-zoom when a live view supplies the virtual size, else
    /// 1) + <see cref="SceneObjectComponent"/> so it saves in <c>entities[]</c> like any scene root. Runs
    /// on BOTH the editor and shipped paths (the caller gates it via <c>ensureSingleCamera</c>).
    /// </summary>
    private void EnsureCameraEntity(List<Entity> created)
    {
        using (var existing = _world.GetEntities().With<CameraComponent>().AsSet())
            if (existing.Count > 0) return; // already exactly one (or restored from the file) — idempotent

        // Reuse the auto-frame math: centre on the loaded content's AABB (origin when there is none), and
        // fit-zoom when a live view supplies the immutable virtual size (else the CameraComponent default).
        var quads = new List<Vector2[]>();
        foreach (var entity in created)
        {
            if (!entity.Has<SpriteInfoComponent>() || !entity.Has<TransformComponent>()) continue;
            quads.Add(GizmoTransform.SpriteWorldQuad(
                entity.Get<TransformComponent>(), entity.Get<SpriteInfoComponent>()));
        }

        var position = Vector2.Zero;
        var zoom = 1f;
        if (CameraNav.ContentBounds(quads) is { } aabb)
        {
            position = CameraNav.Center(aabb);
            if (_camera != null && aabb.Width > 0 && aabb.Height > 0)
                zoom = CameraNav.FitZoom(aabb, _camera.VirtualWidth, _camera.VirtualHeight,
                    FrameMargin, CameraNavSystem.DefaultMinZoom, CameraNavSystem.DefaultMaxZoom);
        }

        var camera = _world.CreateEntity();
        camera.Set(new EntityInfoComponent("Camera"));
        camera.Set(new TransformComponent(position));
        camera.Set(new CameraComponent { Zoom = zoom });
        camera.Set(new SceneObjectComponent()); // a scene root — saved in entities[] like everything else

        Logger.Info(
            $"[level-editor] Scene had no camera entity — created a default 'Camera' at {position} " +
            $"(zoom {zoom:F3}). It saves with the scene (CM one-camera rule).");
    }

    /// <summary>Whether any live entity carries an <b>active</b> <see cref="CameraFollowTargetComponent"/>
    /// — the signal that <c>CameraFollowSystem</c> will drive the camera, so the reader must not.</summary>
    private bool HasActiveFollowTarget()
    {
        using var set = _world.GetEntities().With<CameraFollowTargetComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.Get<CameraFollowTargetComponent>().IsActive) return true;
        return false;
    }

    public void Update(GameState state)
    {
        // Loading is message-driven (subscription); nothing per-frame.
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
