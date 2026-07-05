#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
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
/// <para>It <b>fails loud</b>: a component key in the file with no registered serializer throws from
/// the registry (the load aborts with a clear message rather than silently dropping data); the
/// exception is logged and surfaced.</para>
/// </summary>
public sealed class SceneReaderSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly SceneSerializer _serializer;
    private readonly ContentManager _content;
    private readonly Func<string, Texture2D> _loadTexture;
    private readonly Func<string, Texture2D?>? _fileTextureLoader;

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Subscribes to <see cref="LoadSceneRequest"/>. <paramref name="content"/> resolves the scene's
    /// content path and rehydrates textures; pass an explicit <paramref name="loadTexture"/> only to
    /// override the default <c>content.Load&lt;Texture2D&gt;</c> (e.g. a test stub with no GraphicsDevice).
    /// <paramref name="fileTextureLoader"/> handles <c>file:</c> asset keys (runtime-loaded PNGs from
    /// the asset drop folder — see <see cref="Assets.FileAssetKey"/>); wire
    /// <c>FileAssetTextureLoader.Load</c>, which returns a visible magenta placeholder (with a loud
    /// warning) for a missing file so the entity is never silently invisible.
    /// </summary>
    public SceneReaderSystem(
        World world,
        SceneSerializer serializer,
        ContentManager content,
        Func<string, Texture2D>? loadTexture = null,
        Func<string, Texture2D?>? fileTextureLoader = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _content = content; // may be null when an explicit loadTexture is supplied (tests)
        _loadTexture = loadTexture ?? (key => _content.Load<Texture2D>(key));
        _fileTextureLoader = fileTextureLoader;
        _world.Subscribe<LoadSceneRequest>(On);
    }

    [Subscribe]
    public void On(in LoadSceneRequest message)
    {
        if (!IsEnabled) return;

        var path = message.Path;
        Logger.Info($"[level-editor] Loading scene '{path}' (fromContent={message.FromContent}).");

        try
        {
            var json = ReadSceneJson(path, message.FromContent);
            var scene = CanonicalJson.Deserialize<SceneData>(json);
            if (scene == null)
            {
                Logger.Error($"[level-editor] Scene '{path}' deserialized to null; aborting load.");
                return;
            }

            // Two-pass create + deserialize + wire-parents (reconstructs from components, not factories).
            // Throws loud on an unregistered component key — do not swallow that here; let it surface.
            var created = _serializer.Deserialize(_world, scene);

            RetagSceneRoots(scene, created);

            RehydrateTextures(created);

            Logger.Info($"[level-editor] Loaded scene '{path}': {created.Count} entities.");
        }
        catch (Exception ex)
        {
            Logger.Error($"[level-editor] Error loading scene '{path}': {ex.Message}\n{ex.StackTrace}");
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

    public void Update(GameState state)
    {
        // Loading is message-driven (subscription); nothing per-frame.
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
