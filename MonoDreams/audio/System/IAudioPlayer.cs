using System;

namespace MonoDreams.System.Audio;

/// <summary>
/// Playback seam between <see cref="AudioSystem"/> and the audio backend. The default
/// implementation is <see cref="ContentAudioPlayer"/> (XNA <c>SoundEffect</c> instances loaded
/// through the <c>ContentManager</c>); tests inject a fake so audio logic runs without hardware.
///
/// Handles are positive integers minted by <see cref="Play"/>; <see cref="InvalidHandle"/> (0)
/// means "no playback started" (e.g. the backend is unavailable). Every method must be a safe
/// no-op for an unknown, released, or invalid handle.
/// </summary>
public interface IAudioPlayer : IDisposable
{
    /// <summary>The handle value meaning "no playback": returned by <see cref="Play"/> on failure,
    /// never minted for a live instance.</summary>
    const int InvalidHandle = 0;

    /// <summary>Start playing <paramref name="soundKey"/> and return a live handle, or
    /// <see cref="InvalidHandle"/> if playback could not start.</summary>
    int Play(string soundKey, float volume, float pitch, float pan, bool loop);

    /// <summary>Stop and release the instance. The handle is invalid afterwards.</summary>
    void Stop(int handle);

    /// <summary>Pause the instance, keeping its position; <see cref="Resume"/> continues it.</summary>
    void Pause(int handle);

    /// <summary>Resume a paused instance.</summary>
    void Resume(int handle);

    void SetVolume(int handle, float volume);

    void SetPitch(int handle, float pitch);

    void SetPan(int handle, float pan);

    /// <summary>Whether the instance is currently audible (live, not paused, not finished).</summary>
    bool IsPlaying(int handle);
}
