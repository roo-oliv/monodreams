#nullable enable
using System;
using DefaultEcs;
using MonoDreams.Component.Draw;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// The <c>SpriteInfoComponent ⇒ DrawComponent</c> pairing rule (rendering premise; PF-A pre-mortem #6),
/// enforced by the add/remove component commands: a serialized sprite carries a
/// <see cref="SpriteInfoComponent"/>, but the transient <see cref="DrawComponent"/> that
/// <c>SpritePrepSystem</c>'s <c>[With(DrawComponent, …)]</c> query needs is never serialized — it is
/// re-derived (a sprite <see cref="DrawComponent"/> whose <c>Target</c> is the sprite's own
/// <see cref="SpriteInfoComponent.Target"/>, mirroring <c>SceneReaderSystem.RestoreDrawComponents</c> /
/// <c>SpritePropFactory</c>). So adding a <see cref="SpriteInfoComponent"/> must ALSO add the paired
/// <see cref="DrawComponent"/>, and removing it must remove the transient <see cref="DrawComponent"/> —
/// undo restoring both. Without this the "reloaded/edited sprite renders blank" class of bug returns.
/// </summary>
internal static class ComponentPairing
{
    /// <summary>Whether <paramref name="type"/> is <see cref="SpriteInfoComponent"/> — the one component
    /// whose add/remove drags the transient <see cref="DrawComponent"/> with it.</summary>
    public static bool PairsDrawComponent(Type type) => type == typeof(SpriteInfoComponent);

    /// <summary>Ensures the sprite's transient <see cref="DrawComponent"/> exists (idempotent) — the
    /// add path and the undo-of-remove path. A no-op when the entity has no sprite or already has a
    /// <see cref="DrawComponent"/>.</summary>
    public static void EnsureSpriteDraw(Entity entity)
    {
        if (!entity.IsAlive || !entity.Has<SpriteInfoComponent>() || entity.Has<DrawComponent>()) return;
        entity.Set(new DrawComponent
        {
            Type = DrawElementType.Sprite,
            Target = entity.Get<SpriteInfoComponent>().Target,
        });
    }

    /// <summary>Removes the transient <see cref="DrawComponent"/> — the remove path and the
    /// undo-of-add path. A no-op when absent.</summary>
    public static void RemoveSpriteDraw(Entity entity)
    {
        if (entity.IsAlive && entity.Has<DrawComponent>()) entity.Remove<DrawComponent>();
    }
}
