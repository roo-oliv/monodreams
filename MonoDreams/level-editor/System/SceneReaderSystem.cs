#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component.Draw;
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
/// deserialize it <b>rehydrates</b> each sprite's <c>Texture2D</c> from its
/// <see cref="SpriteInfoComponent.AssetKey"/> via the content loader (the in-memory deserialize
/// leaves <c>SpriteSheet</c> null on purpose — it has no <c>ContentManager</c>).</para>
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

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Subscribes to <see cref="LoadSceneRequest"/>. <paramref name="content"/> resolves the scene's
    /// content path and rehydrates textures; pass an explicit <paramref name="loadTexture"/> only to
    /// override the default <c>content.Load&lt;Texture2D&gt;</c> (e.g. a test stub with no GraphicsDevice).
    /// </summary>
    public SceneReaderSystem(
        World world,
        SceneSerializer serializer,
        ContentManager content,
        Func<string, Texture2D>? loadTexture = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _content = content; // may be null when an explicit loadTexture is supplied (tests)
        _loadTexture = loadTexture ?? (key => _content.Load<Texture2D>(key));
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
            var scene = JsonSerializer.Deserialize<SceneData>(json);
            if (scene == null)
            {
                Logger.Error($"[level-editor] Scene '{path}' deserialized to null; aborting load.");
                return;
            }

            // Two-pass create + deserialize + wire-parents (reconstructs from components, not factories).
            // Throws loud on an unregistered component key — do not swallow that here; let it surface.
            var created = _serializer.Deserialize(_world, scene);

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
    /// Rehydrates the live <c>Texture2D</c> for every loaded entity whose <c>SpriteInfo.AssetKey</c>
    /// is set, via the content loader. A sprite with a null asset key keeps a null
    /// <c>SpriteSheet</c> (it had no re-loadable texture).
    /// </summary>
    private void RehydrateTextures(List<Entity> entities)
    {
        foreach (var entity in entities)
        {
            if (!entity.Has<SpriteInfoComponent>()) continue;
            ref var sprite = ref entity.Get<SpriteInfoComponent>();
            if (string.IsNullOrEmpty(sprite.AssetKey)) continue;

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
