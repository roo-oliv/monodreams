using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using MonoDreams.State;

namespace MonoDreams.System.Audio;

/// <summary>
/// Default <see cref="IAudioPlayer"/>: loads <see cref="SoundEffect"/>s through the screen's
/// <see cref="ContentManager"/> (cached per key) and plays them via <see cref="SoundEffectInstance"/>s.
/// Works identically on MonoGame DesktopGL and KNI/BlazorGL (WebAudio buffers).
///
/// Without audio hardware (headless CI, deviceless machines) the XNA audio backend throws on
/// first use; this player catches the backend failure, logs a single <c>Logger.Warning</c>, and
/// degrades to a silent no-op for the rest of its lifetime — game and test logic keep running.
/// A missing content key is a developer error and still fails loud (<see cref="ContentLoadException"/>).
/// </summary>
public class ContentAudioPlayer(ContentManager content) : IAudioPlayer
{
    private readonly Dictionary<string, SoundEffect> _sounds = new();
    private readonly Dictionary<int, SoundEffectInstance> _instances = new();
    private int _nextHandle;
    private bool _disabled;

    public int Play(string soundKey, float volume, float pitch, float pan, bool loop)
    {
        if (_disabled) return IAudioPlayer.InvalidHandle;

        try
        {
            if (!_sounds.TryGetValue(soundKey, out var sound))
            {
                sound = LoadSoundEffect(soundKey);
                _sounds[soundKey] = sound;
            }

            var instance = sound.CreateInstance();
            instance.IsLooped = loop;
            instance.Volume = Math.Clamp(volume, 0f, 1f);
            instance.Pitch = Math.Clamp(pitch, -1f, 1f);
            instance.Pan = Math.Clamp(pan, -1f, 1f);
            instance.Play();

            var handle = ++_nextHandle;
            _instances[handle] = instance;
            return handle;
        }
        catch (Exception e) when (IsAudioBackendFailure(e))
        {
            Disable(e);
            return IAudioPlayer.InvalidHandle;
        }
    }

    public void Stop(int handle)
    {
        if (!_instances.Remove(handle, out var instance)) return;
        instance.Stop();
        instance.Dispose();
    }

    public void Pause(int handle)
    {
        if (_instances.TryGetValue(handle, out var instance)) instance.Pause();
    }

    public void Resume(int handle)
    {
        if (_instances.TryGetValue(handle, out var instance)) instance.Resume();
    }

    public void SetVolume(int handle, float volume)
    {
        if (_instances.TryGetValue(handle, out var instance)) instance.Volume = Math.Clamp(volume, 0f, 1f);
    }

    public void SetPitch(int handle, float pitch)
    {
        if (_instances.TryGetValue(handle, out var instance)) instance.Pitch = Math.Clamp(pitch, -1f, 1f);
    }

    public void SetPan(int handle, float pan)
    {
        if (_instances.TryGetValue(handle, out var instance)) instance.Pan = Math.Clamp(pan, -1f, 1f);
    }

    public bool IsPlaying(int handle) =>
        _instances.TryGetValue(handle, out var instance) && instance.State == SoundState.Playing;

    /// <summary>
    /// Loads the <see cref="SoundEffect"/> for <paramref name="soundKey"/>. Virtual so tests can
    /// force the no-hardware failure path (and so games can swap the loading strategy) without
    /// a real <see cref="ContentManager"/>.
    /// </summary>
    protected virtual SoundEffect LoadSoundEffect(string soundKey) => content.Load<SoundEffect>(soundKey);

    /// <summary>
    /// Whether <paramref name="e"/> (or anything in its inner chain, covering
    /// <see cref="ContentLoadException"/>/<see cref="TypeInitializationException"/> wrappers) is an
    /// audio-backend-unavailable failure: <see cref="NoAudioHardwareException"/>, or the
    /// <see cref="DllNotFoundException"/> a missing native audio library raises on headless machines.
    /// A plain content miss has neither in its chain and propagates to the caller.
    /// </summary>
    private static bool IsAudioBackendFailure(Exception e)
    {
        for (var current = e; current != null; current = current.InnerException)
        {
            if (current is NoAudioHardwareException or DllNotFoundException) return true;
        }

        return false;
    }

    private void Disable(Exception cause)
    {
        // Only reachable once: Play short-circuits on _disabled, so the warning is logged a single time.
        _disabled = true;
        ReleaseAllInstances();
        Logger.Warning(
            $"Audio backend unavailable ({cause.GetType().Name}: {cause.Message}); " +
            "audio playback disabled — all further audio calls are silent no-ops.");
    }

    private void ReleaseAllInstances()
    {
        foreach (var instance in _instances.Values)
        {
            instance.Stop();
            instance.Dispose();
        }

        _instances.Clear();
    }

    public void Dispose()
    {
        ReleaseAllInstances();
        // Cached SoundEffects are owned by the ContentManager (disposed on content.Unload()),
        // so only the instances this player created are disposed here.
        _sounds.Clear();
        GC.SuppressFinalize(this);
    }
}
