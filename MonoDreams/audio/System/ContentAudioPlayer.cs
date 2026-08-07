using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// A full voice pool (<see cref="InstancePlayLimitException"/> — the backend's cap on simultaneous
/// instances) is transient, not backend absence: the new voice is dropped
/// (<see cref="IAudioPlayer.InvalidHandle"/>) without disabling the player.
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
                // Reached only for a key the warm did not cover (see Preload): still correct, just late.
                sound = LoadSoundEffect(soundKey);
                _sounds[soundKey] = sound;
            }

            var instance = StartInstance(sound, volume, pitch, pan, loop);
            var handle = ++_nextHandle;
            _instances[handle] = instance;
            return handle;
        }
        catch (InstancePlayLimitException)
        {
            // The backend's cap on simultaneous voices (XNA's MAX_PLAYING_INSTANCES, 256 in
            // MonoGame 3.8) is a transient mixing condition, not backend absence: drop THIS
            // voice and keep the player alive for future calls. StartInstance already disposed
            // the failed instance, so nothing leaks.
            Logger.Debug($"Audio voice limit reached; dropping playback of '{soundKey}'.");
            return IAudioPlayer.InvalidHandle;
        }
        catch (Exception e) when (IsAudioBackendFailure(e))
        {
            Disable(e);
            return IAudioPlayer.InvalidHandle;
        }
    }

    /// <summary>
    /// Decodes <paramref name="soundKeys"/> into the cache NOW, so no <see cref="Play"/> ever pays the
    /// load. A <see cref="SoundEffect"/> is a disk read plus a decode to PCM on first request, and
    /// <see cref="Play"/> runs mid-frame — an unwarmed game stutters once per distinct sound, which reads
    /// as a gameplay bug because it only ever happens the first time. Call it from a loading moment, where
    /// a hitch is invisible.
    ///
    /// <para><b>A failure is never fatal.</b> A missing or unreadable key is logged as a warning and
    /// skipped: warming is an optimisation, and refusing to boot over one absent effect would turn a
    /// cosmetic gap into a crash. The key stays uncached, so <see cref="Play"/> behaves exactly as it did
    /// before — including failing loud there, where a content miss is still a developer error.</para>
    ///
    /// <para>Backend absence (headless CI) short-circuits the whole warm through the same
    /// <see cref="Disable"/> path <see cref="Play"/> uses, so a deviceless machine spends nothing here.</para>
    /// </summary>
    public void Preload(IEnumerable<string> soundKeys)
    {
        var started = Stopwatch.GetTimestamp();
        var loaded = 0;

        foreach (var soundKey in soundKeys)
        {
            if (_disabled) return;
            if (_sounds.ContainsKey(soundKey)) continue;

            try
            {
                _sounds[soundKey] = LoadSoundEffect(soundKey);
                loaded++;
            }
            catch (Exception e) when (IsAudioBackendFailure(e))
            {
                Disable(e);
                return;
            }
            catch (Exception e)
            {
                Logger.Warning($"Could not preload sound '{soundKey}' ({e.GetType().Name}: {e.Message}); " +
                               "it will be loaded on first play instead.");
            }
        }

        var elapsedMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        Logger.Info($"Audio warm: {loaded} sound effect(s) decoded in {elapsedMs:F1}ms.");
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
    /// Creates, configures, and starts the backend instance for an already-loaded
    /// <see cref="SoundEffect"/>. On any start failure (e.g. the voice cap's
    /// <see cref="InstancePlayLimitException"/>) the just-created instance is disposed before the
    /// exception propagates, so no instance leaks. Virtual so tests can force the voice-cap
    /// failure path without audio hardware.
    /// </summary>
    protected virtual SoundEffectInstance StartInstance(SoundEffect sound, float volume, float pitch, float pan, bool loop)
    {
        var instance = sound.CreateInstance();
        try
        {
            instance.IsLooped = loop;
            instance.Volume = Math.Clamp(volume, 0f, 1f);
            instance.Pitch = Math.Clamp(pitch, -1f, 1f);
            instance.Pan = Math.Clamp(pan, -1f, 1f);
            instance.Play();
            return instance;
        }
        catch
        {
            instance.Dispose();
            throw;
        }
    }

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
