#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// The shared post-deserialize finishing pass reused by <b>both</b> the scene reader
/// (<c>SceneReaderSystem</c>, over a whole loaded scene) and the prefab expander
/// (<c>PrefabExpander</c>, over each reconstructed prefab-instance subtree — whose children are NOT
/// visible to the reader's top-level loop, so the expander MUST finish them itself). Keeping it in ONE
/// place means the reloaded/expanded sprite path (texture rehydration + the transient
/// <c>DrawComponent</c> restore) can never diverge between the two.
///
/// <list type="bullet">
///   <item><b>Rehydrate textures.</b> A live <c>Texture2D</c> is a GPU resource the JSON cannot carry;
///   the serializer persists only the <c>SpriteInfo.AssetKey</c>. This re-loads the texture — a
///   <c>file:</c> key through the file-asset loader (a magenta placeholder for a missing file — fail
///   loud, never invisible), everything else through the content loader.</item>
///   <item><b>Restore <c>DrawComponent</c>.</b> <c>DrawComponent</c> is deliberately not serialized (its
///   sprite fields are re-prepped each frame and its <c>LayerDepth</c> is per-frame-derived), so a
///   reconstructed sprite has <c>SpriteInfoComponent</c> + <c>TransformComponent</c> but no
///   <c>DrawComponent</c> and never enters <c>SpritePrepSystem</c>'s query — the "reloaded scene renders
///   blank" bug. This restores the sprite <c>DrawComponent</c> (target from the sprite's own
///   <c>SpriteInfoComponent.Target</c>, mirroring <c>SpritePropFactory</c>) for every sprite lacking one.</item>
/// </list>
/// </summary>
public static class SceneRehydration
{
    /// <summary>
    /// Rehydrates the live <c>Texture2D</c> for every entity in <paramref name="entities"/> whose
    /// <c>SpriteInfo.AssetKey</c> is set: <c>file:</c> keys via <paramref name="fileTextureLoader"/>
    /// (magenta placeholder for a missing file), everything else via <paramref name="loadTexture"/>. A
    /// sprite with a null/empty asset key keeps a null <c>SpriteSheet</c>.
    /// </summary>
    public static void RehydrateTextures(
        IEnumerable<Entity> entities,
        Func<string, Texture2D>? loadTexture,
        Func<string, Texture2D?>? fileTextureLoader)
    {
        foreach (var entity in entities)
        {
            if (!entity.Has<SpriteInfoComponent>()) continue;
            ref var sprite = ref entity.Get<SpriteInfoComponent>();
            if (string.IsNullOrEmpty(sprite.AssetKey)) continue;

            if (Assets.FileAssetKey.IsFileKey(sprite.AssetKey))
            {
                if (fileTextureLoader != null)
                    sprite.SpriteSheet = fileTextureLoader(sprite.AssetKey);
                else
                    Logger.Warning($"[level-editor] Asset key '{sprite.AssetKey}' is a file: key but " +
                                   "no file-asset loader is composed — the sprite stays invisible. " +
                                   "Compose the overlay's FileAssetTextureLoader (or graduate the " +
                                   "asset to an MGCB content key).");
                continue;
            }

            if (loadTexture == null) continue; // no content loader (a pure in-memory test): leave SpriteSheet null

            try
            {
                sprite.SpriteSheet = loadTexture(sprite.AssetKey);
            }
            catch (Exception ex)
            {
                Logger.Error($"[level-editor] Failed to rehydrate texture for asset key '{sprite.AssetKey}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Restores the transient sprite <see cref="DrawComponent"/> on every entity in
    /// <paramref name="entities"/> that has a <c>SpriteInfoComponent</c> but no <c>DrawComponent</c>
    /// (target taken from the sprite's own <see cref="SpriteInfoComponent.Target"/>). Idempotent — an
    /// entity that already has a <c>DrawComponent</c> is left untouched.
    /// </summary>
    public static void RestoreDrawComponents(IEnumerable<Entity> entities)
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
}
