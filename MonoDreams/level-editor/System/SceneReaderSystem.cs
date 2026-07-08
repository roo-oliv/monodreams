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
    private readonly Action<SceneCameraData?>? _applyCameraToRig;

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
    /// positions that VIEW on load; null (the pure round-trip tests) skips all camera positioning.
    /// <paramref name="applyCameraToRig"/> (optional; the editor overlay wires it to
    /// <c>EditorCameraRig.SyncFromScene</c>) is the <b>editor rig seam</b>: when present it hands the
    /// loaded <c>scene.camera</c> to the rig (the authored game-camera state lives on the rig), and the
    /// live VIEW is auto-framed on the content so the designer sees the scene regardless of where the
    /// authored camera sits (UX2-E). When ABSENT (a shipped game — no editor overlay), there is no rig
    /// and the live camera IS the authored camera: <c>scene.camera</c> is applied to it directly (or, for
    /// a legacy camera-less scene, the camera auto-frames on content, byte-identical to pre-UX2-E).
    /// </summary>
    public SceneReaderSystem(
        World world,
        SceneSerializer serializer,
        ContentManager content,
        Func<string, Texture2D>? loadTexture = null,
        Func<string, Texture2D?>? fileTextureLoader = null,
        Camera? camera = null,
        Action<SceneCameraData?>? applyCameraToRig = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _content = content; // may be null when an explicit loadTexture is supplied (tests)
        _loadTexture = loadTexture ?? (key => _content.Load<Texture2D>(key));
        _fileTextureLoader = fileTextureLoader;
        _camera = camera;
        _applyCameraToRig = applyCameraToRig;
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
            Load(inMemory, "<in-memory>");
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

        Load(scene, path);
    }

    /// <summary>
    /// Reconstructs <paramref name="scene"/> into the world and finishes the load — the ONE restore
    /// implementation shared by the file path and the UX2-F in-memory snapshot restore
    /// (<see cref="LoadSceneRequest(SceneData)"/>): two-pass create + deserialize + wire-parents
    /// (from components, not factories), re-tag roots, rehydrate textures (content AND <c>file:</c>
    /// keys), restore the transient <c>DrawComponent</c>, then route <c>scene.camera</c> to the rig /
    /// view. Throws loud on an unregistered component key. <paramref name="pathForLogging"/> only names
    /// the source in the log line.
    /// </summary>
    private void Load(SceneData scene, string pathForLogging)
    {
        try
        {
            // Two-pass create + deserialize + wire-parents (reconstructs from components, not factories).
            // Throws loud on an unregistered component key — do not swallow that here; let it surface.
            var created = _serializer.Deserialize(_world, scene);

            RetagSceneRoots(scene, created);

            RehydrateTextures(created);

            RestoreDrawComponents(created);

            ApplyCamera(scene, created);

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
    /// (the portable content-stream primitive — a file read on desktop, an HTTP fetch on web,
    /// matching <c>BlenderLevelParserSystem</c>); a host-filesystem path goes through
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
        for (var i = 0; i < created.Count && i < scene.Entities.Count; i++)
        {
            var parentIndex = scene.Entities[i].Parent;
            var hasInScopeParent = parentIndex is { } pi && pi >= 0 && pi < created.Count;
            if (hasInScopeParent) continue;

            created[i].Set(new SceneObjectComponent());
            if (scene.Entities[i].Id is { } id)
                created[i].Set(new SceneEntityIdComponent(id));
        }
    }

    /// <summary>
    /// Rehydrates the live <c>Texture2D</c> for every loaded entity whose <c>SpriteInfo.AssetKey</c>
    /// is set: <c>file:</c> keys go through the file-asset loader (which shows a magenta
    /// placeholder for a missing file — fail loud, never invisible), everything else through the
    /// content loader. A sprite with a null asset key keeps a null <c>SpriteSheet</c> (it had no
    /// re-loadable texture).
    /// </summary>
    private void RehydrateTextures(List<Entity> entities)
    {
        foreach (var entity in entities)
        {
            if (!entity.Has<SpriteInfoComponent>()) continue;
            ref var sprite = ref entity.Get<SpriteInfoComponent>();
            if (string.IsNullOrEmpty(sprite.AssetKey)) continue;

            if (Assets.FileAssetKey.IsFileKey(sprite.AssetKey))
            {
                if (_fileTextureLoader != null)
                    sprite.SpriteSheet = _fileTextureLoader(sprite.AssetKey);
                else
                    Logger.Warning($"[level-editor] Asset key '{sprite.AssetKey}' is a file: key but " +
                                   "no file-asset loader is composed — the sprite stays invisible. " +
                                   "Compose the overlay's FileAssetTextureLoader (or graduate the " +
                                   "asset to an MGCB content key).");
                continue;
            }

            try
            {
                sprite.SpriteSheet = _loadTexture(sprite.AssetKey);
            }
            catch (Exception ex)
            {
                Logger.Error($"[level-editor] Failed to rehydrate texture for asset key '{sprite.AssetKey}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Restores the transient <see cref="DrawComponent"/> on every reconstructed sprite that lacks
    /// one — the <c>SpriteInfoComponent ⇒ DrawComponent</c> pairing every renderable sprite needs.
    /// <see cref="DrawComponent"/> is deliberately NOT serialized (its sprite fields are re-prepped
    /// each frame from <see cref="SpriteInfoComponent"/> and its <c>LayerDepth</c> is per-frame-derived),
    /// so the reader reconstructs it here rather than from the file, mirroring
    /// <see cref="Assets.SpritePropFactory"/> (sprite type, target taken from the sprite's own
    /// <see cref="SpriteInfoComponent.Target"/>). Without it a reloaded sprite has no
    /// <see cref="DrawComponent"/>, so <c>SpritePrepSystem</c>'s <c>[With(DrawComponent, …)]</c> query
    /// skips it and it never draws (the "reloaded scene renders blank" bug). Mesh/text draw data is not
    /// serializable today, so this handles sprites — the only serialized renderable.
    /// </summary>
    private static void RestoreDrawComponents(List<Entity> entities)
    {
        foreach (var entity in entities)
        {
            if (!entity.Has<SpriteInfoComponent>() || entity.Has<DrawComponent>()) continue;
            var target = entity.Get<SpriteInfoComponent>().Target;
            entity.Set(new DrawComponent
            {
                Type = DrawElementType.Sprite,
                Target = target,
            });
        }
    }

    /// <summary>
    /// Routes the loaded <c>scene.camera</c> and positions the camera on load — the view/camera split
    /// (UX2-E). The persisted form stays <c>scene.camera</c>; who consumes it depends on whether the
    /// editor is composed:
    /// <list type="bullet">
    ///   <item><b>Editor present</b> (<see cref="_applyCameraToRig"/> wired): the authored camera goes
    ///   to the <b>rig</b> (the seam), and the free <b>VIEW</b> (the live <see cref="Camera"/>)
    ///   auto-frames on the content so the designer sees the scene regardless of where the authored
    ///   camera sits. The rig holds the authored state; the glyph shows it when the view differs.</item>
    ///   <item><b>Shipped</b> (no rig seam): the live camera IS the authored camera. When
    ///   <c>scene.camera</c> is present it is applied to the camera directly (respecting the authored
    ///   view); a legacy camera-less scene auto-frames on content — byte-identical to pre-UX2-E.</item>
    /// </list>
    /// In BOTH cases the camera is left untouched when a null camera was supplied (a pure round-trip
    /// test, or the reference shipped reader that relies on <c>CameraFollowSystem</c>) or an <b>active</b>
    /// <see cref="CameraFollowTargetComponent"/> is present (<c>CameraFollowSystem</c> owns the camera in
    /// Play — the reader must not fight it).
    /// </summary>
    private void ApplyCamera(SceneData scene, List<Entity> created)
    {
        // The rig (editor only) always receives the authored camera — even when the VIEW is left alone
        // (a follow target present, or a null view camera): the rig owns the authored state, the view is
        // just what the viewport looks through.
        _applyCameraToRig?.Invoke(scene.Camera);

        if (_camera == null) return;         // no live VIEW supplied → nothing to position
        if (HasActiveFollowTarget()) return; // CameraFollowSystem owns it (Play) — don't fight it

        if (_applyCameraToRig != null)
        {
            // Editor: the authored state is on the rig; the free view frames the content.
            FrameViewOnContent(created);
        }
        else if (scene.Camera != null)
        {
            // Shipped: no rig — the live camera IS the authored camera, so respect the persisted view.
            ApplySceneCameraToView(scene.Camera);
        }
        else
        {
            // Shipped, legacy/camera-less scene: auto-frame on content (byte-identical to pre-UX2-E).
            FrameViewOnContent(created);
        }
    }

    /// <summary>Applies a persisted <see cref="SceneCameraData"/> to the live VIEW — the shipped path's
    /// "the authored camera is the game camera" (the rig only exists under the editor).</summary>
    private void ApplySceneCameraToView(SceneCameraData camera)
    {
        _camera!.Position = new Vector2(
            camera.Position.Length > 0 ? camera.Position[0] : 0f,
            camera.Position.Length > 1 ? camera.Position[1] : 0f);
        _camera.Zoom = camera.Zoom;
        _camera.Rotation = camera.Rotation;
        Logger.Info(
            $"[level-editor] Applied scene.camera to the view: center={_camera.Position}, zoom={_camera.Zoom:F3}.");
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
