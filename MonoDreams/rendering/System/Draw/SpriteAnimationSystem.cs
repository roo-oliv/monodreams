#nullable enable
using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.System.Draw;

/// <summary>
/// Advances every <see cref="SpriteAnimationComponent"/> and applies the current frame to the
/// entity's <see cref="SpriteInfoComponent"/> SOURCE fields (<c>SpriteSheet</c>/<c>AssetKey</c>/
/// <c>Source</c>, and <c>Size</c> when the sprite was unscaled) — an update-pipeline system that
/// runs BEFORE the draw prep; it never touches <c>DrawComponent</c> or the render path.
///
/// <para><b>Texture resolution is injected</b> (<c>resolveTexture</c>): the screen supplies the
/// same content-or-<c>file:</c> resolver its scene reader uses, so a frame's
/// <see cref="SpriteAnimationFrame.AssetKey"/> may be an MGCB content key or a drop-folder
/// <c>file:</c> key. Resolution failures keep the current texture (loud once via the resolver's
/// own warning conventions), never a crash mid-frame.</para>
///
/// <para><b>Applies on index CHANGE — force a re-apply with <c>FrameIndex = -1</c>.</b> A frame is
/// written to the sprite only when the resolved index differs from
/// <see cref="SpriteAnimationComponent.FrameIndex"/>. Game code that swaps the sprite's
/// texture/source itself (a white-flash blink, a telegraph tint) and then re-points the entity at a
/// strip of the SAME length can land on the same index and skip the apply, leaving the swapped-in
/// art on screen. Setting <c>FrameIndex = -1</c> means "nothing applied yet", so the next update
/// applies frame 0 unconditionally.</para>
///
/// <para><b>Edit-time policy:</b> register it <c>Freeze</c> in editor-capable screens — in
/// <c>RunMode.Edit</c> sprites hold their authored frame, so scene saves and prefab override
/// diffs stay deterministic (an animating sprite would otherwise serialize a random frame).</para>
/// </summary>
public sealed class SpriteAnimationSystem : ISystem<GameState>
{
    private readonly EntitySet _animated;
    private readonly Func<string, Texture2D?>? _resolveTexture;

    public bool IsEnabled { get; set; } = true;

    public SpriteAnimationSystem(World world, Func<string, Texture2D?>? resolveTexture = null)
    {
        _animated = world.GetEntities()
            .With<SpriteAnimationComponent>()
            .With<SpriteInfoComponent>()
            .AsSet();
        _resolveTexture = resolveTexture;
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        foreach (var entity in _animated.GetEntities())
        {
            ref var anim = ref entity.Get<SpriteAnimationComponent>();
            if (anim.Frames is not { Length: > 0 }) continue;

            if (anim.Playing && anim.FrameIndex >= 0)
                anim.Time += state.Time * anim.Speed;

            var target = ResolveFrameIndex(ref anim);
            if (target == anim.FrameIndex) continue;

            ApplyFrame(entity, ref anim, target);
        }
    }

    /// <summary>The frame index <see cref="SpriteAnimationComponent.Time"/> lands on, honoring
    /// per-frame durations, looping, and the hold-last-and-stop non-loop end.</summary>
    private static int ResolveFrameIndex(ref SpriteAnimationComponent anim)
    {
        if (anim.FrameIndex < 0) return 0; // first sight: apply frame 0 immediately

        var total = 0f;
        for (var i = 0; i < anim.Frames.Length; i++) total += FrameDuration(in anim, i);
        if (total <= 0f) return anim.FrameIndex;

        var time = anim.Time;
        if (anim.Loop)
        {
            time %= total;
        }
        else if (time >= total)
        {
            anim.Playing = false; // hold the last frame
            return anim.Frames.Length - 1;
        }

        var acc = 0f;
        for (var i = 0; i < anim.Frames.Length; i++)
        {
            acc += FrameDuration(in anim, i);
            if (time < acc) return i;
        }
        return anim.Frames.Length - 1;
    }

    private static float FrameDuration(in SpriteAnimationComponent anim, int index)
    {
        var d = anim.Frames[index].Duration;
        return d > 0f ? d : Math.Max(0f, anim.DefaultFrameDuration);
    }

    /// <summary>Writes <paramref name="target"/>'s texture/source onto the sprite. A sprite whose
    /// <c>Size</c> matched its previous source (rendered unscaled) keeps rendering unscaled — the
    /// size follows the new source; an authored scale is preserved by leaving <c>Size</c> alone.</summary>
    private void ApplyFrame(Entity entity, ref SpriteAnimationComponent anim, int target)
    {
        ref var sprite = ref entity.Get<SpriteInfoComponent>();
        var frame = anim.Frames[target];
        var previousSource = sprite.Source;

        if (frame.AssetKey != null && !string.Equals(frame.AssetKey, sprite.AssetKey, StringComparison.Ordinal))
        {
            var texture = _resolveTexture?.Invoke(frame.AssetKey);
            if (texture != null)
            {
                sprite.SpriteSheet = texture;
                sprite.AssetKey = frame.AssetKey;
            }
        }

        var source = frame.Source;
        if (source == Rectangle.Empty && sprite.SpriteSheet != null)
            source = sprite.SpriteSheet.Bounds; // Empty = "the whole frame texture"
        if (source != Rectangle.Empty)
        {
            sprite.Source = source;
            var wasUnscaled = previousSource.Width > 0 && previousSource.Height > 0
                && Math.Abs(sprite.Size.X - previousSource.Width) < 0.01f
                && Math.Abs(sprite.Size.Y - previousSource.Height) < 0.01f;
            if (wasUnscaled)
                sprite.Size = new Vector2(source.Width, source.Height);
        }

        anim.FrameIndex = target;
    }

    public void Dispose() => _animated.Dispose();
}
