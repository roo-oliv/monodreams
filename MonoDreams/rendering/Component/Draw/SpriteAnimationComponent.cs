#nullable enable
using System;
using Microsoft.Xna.Framework;

namespace MonoDreams.Component.Draw;

/// <summary>
/// One frame of a <see cref="SpriteAnimationComponent"/>: which texture (by asset key) and which
/// source rectangle the sprite shows while this frame is current.
/// </summary>
public struct SpriteAnimationFrame
{
    /// <summary>The frame texture's asset key (a content key or a <c>file:</c> key — whatever the
    /// screen's texture resolver understands), or <c>null</c> to keep the sprite's current texture
    /// (an atlas animation that only moves <see cref="Source"/>).</summary>
    public string? AssetKey;

    /// <summary>The source rectangle within the frame's texture. <see cref="Rectangle.Empty"/>
    /// means "the whole texture" (resolved when the frame is applied) — the natural value for
    /// one-PNG-per-frame sequences whose sizes are only known once the texture loads.</summary>
    public Rectangle Source;

    /// <summary>Seconds this frame holds; a non-positive value falls back to the component's
    /// <see cref="SpriteAnimationComponent.DefaultFrameDuration"/>.</summary>
    public float Duration;
}

/// <summary>
/// A frame-sequence animation over an entity's <see cref="SpriteInfoComponent"/> — pure data;
/// <c>SpriteAnimationSystem</c> advances <see cref="Time"/> and writes the current frame's
/// texture/source back onto the sprite's SOURCE fields (never the derived draw state). Frames may
/// live on one atlas (null <see cref="SpriteAnimationFrame.AssetKey"/>, moving
/// <see cref="SpriteAnimationFrame.Source"/>) or one texture per frame (the
/// pixel-art one-PNG-per-frame convention).
///
/// <para><see cref="Time"/> and <see cref="FrameIndex"/> are RUNTIME state — the serializer
/// persists the authored fields only, so a loaded scene always starts an animation from frame 0
/// and <c>load → save</c> stays byte-stable.</para>
///
/// <para><b>Forcing a re-apply after an external texture swap:</b> set <c>FrameIndex = -1</c>. The
/// animator applies a frame only when the resolved index CHANGES, so game code that swaps the
/// sprite's texture/source itself (a white-flash hit blink, a telegraph tint) and then hands the
/// entity back to a strip of the SAME length can resolve to the same index and skip the apply — the
/// swapped-in art sticks. <c>FrameIndex = -1</c> is "nothing has been applied yet", which makes the
/// next update apply frame 0 unconditionally. (This bit a real bug in the reference game's telegraph
/// flames.)</para>
/// </summary>
public struct SpriteAnimationComponent()
{
    /// <summary>The frames, in play order. Empty disables the system for this entity.</summary>
    public SpriteAnimationFrame[] Frames = Array.Empty<SpriteAnimationFrame>();

    /// <summary>Seconds per frame for frames that don't carry their own duration.</summary>
    public float DefaultFrameDuration = 0.12f;

    /// <summary>Whether the sequence wraps (true) or holds its last frame and stops (false).</summary>
    public bool Loop = true;

    /// <summary>Whether the animation advances. A non-looping animation clears this at its end.</summary>
    public bool Playing = true;

    /// <summary>Playback rate multiplier (1 = authored speed).</summary>
    public float Speed = 1f;

    /// <summary>Runtime: seconds into the sequence. Not serialized.</summary>
    public float Time = 0f;

    /// <summary>Runtime: the frame currently applied to the sprite, or -1 when none has been
    /// applied yet (the system applies frame 0 on first sight). Not serialized. Set it back to -1 to
    /// force a re-apply after game code swapped the sprite's texture externally.</summary>
    public int FrameIndex = -1;
}
