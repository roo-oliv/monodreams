namespace MonoDreams.Component.Audio;

/// <summary>
/// The playback state a game wants an <see cref="AudioSourceComponent"/> to be in.
/// Game code writes the desired state; <c>AudioSystem</c> reconciles the live
/// backend instance against it once per update.
/// </summary>
public enum AudioPlaybackState
{
    /// <summary>The source should be audible. Starts playback if no live instance exists,
    /// or resumes a paused one.</summary>
    Playing,

    /// <summary>The source should hold its position silently and be resumable.</summary>
    Paused,

    /// <summary>The source should not be playing. Cuts the live instance (jukebox-style
    /// interruption); setting <see cref="AudioPlaybackState.Playing"/> again restarts from the beginning.</summary>
    Stopped,
}

/// <summary>
/// Entity-owned audio playback with a lifecycle: looping ambience, interruptible music,
/// anything that must be cut, paused, or mutated after it starts. Pure data — all behavior
/// lives in <c>AudioSystem</c>. For fire-and-forget effects (button click), publish a
/// <c>PlaySoundRequest</c> message instead of creating an entity.
///
/// A class (not a struct) because the system stores the live playback handle on it,
/// following the <c>DrawComponent</c> precedent of components carrying runtime handles.
/// </summary>
public class AudioSourceComponent(
    string soundKey,
    bool loop = false,
    float volume = 1f,
    float pitch = 0f,
    float pan = 0f)
{
    /// <summary>Content key of the <c>SoundEffect</c> to play (e.g. <c>"Sounds/wind"</c>).
    /// Changing it while an instance is live has no effect until the source is stopped and restarted.</summary>
    public string SoundKey = soundKey;

    /// <summary>Whether playback loops. Applied when the instance starts; mutating it mid-play
    /// has no effect on the live instance (XNA's <c>IsLooped</c> cannot change after Play).</summary>
    public bool Loop = loop;

    /// <summary>Volume in [0, 1]. Mutations propagate to the live instance on the next reconcile.</summary>
    public float Volume = volume;

    /// <summary>Pitch in [-1, 1] (octaves). Mutations propagate to the live instance on the next reconcile.</summary>
    public float Pitch = pitch;

    /// <summary>Stereo pan in [-1, 1]. Mutations propagate to the live instance on the next reconcile.</summary>
    public float Pan = pan;

    /// <summary>The desired playback state. Game code writes this; <c>AudioSystem</c> makes it so.
    /// A non-looping source that plays to completion is flipped back to
    /// <see cref="AudioPlaybackState.Stopped"/> by the system.</summary>
    public AudioPlaybackState State = AudioPlaybackState.Playing;

    /// <summary>System-managed: the live <c>IAudioPlayer</c> handle, or null when no instance
    /// is live. Never write this from game code.</summary>
    public int? Instance;

    /// <summary>System-managed: the state <c>AudioSystem</c> last applied to the live instance
    /// (distinguishes "paused by us" from "finished naturally"). Never write this from game code.</summary>
    public AudioPlaybackState AppliedState = AudioPlaybackState.Stopped;
}
