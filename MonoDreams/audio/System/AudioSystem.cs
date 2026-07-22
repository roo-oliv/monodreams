using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component.Audio;
using MonoDreams.Message;
using MonoDreams.State;

namespace MonoDreams.System.Audio;

/// <summary>
/// The single audio system: plays one-shot <see cref="PlaySoundRequest"/> messages and
/// reconciles every <see cref="AudioSourceComponent"/>'s desired state against its live
/// backend instance once per update (start / pause / resume / stop, and volume/pitch/pan
/// propagation). Removing the component, overwriting it via <c>Set</c>, or disposing the
/// entity cuts its playback immediately. All backend access goes through the injected <see cref="IAudioPlayer"/>;
/// the system owns the instance lifecycle, the player owns the hardware.
/// </summary>
public class AudioSystem : ISystem<GameState>
{
    public bool IsEnabled { get; set; } = true;

    private readonly IAudioPlayer _player;
    private readonly EntitySet _sources;
    private readonly List<int> _oneShots = [];
    private readonly IDisposable _playRequestSubscription;
    private readonly IDisposable _sourceRemovedSubscription;
    private readonly IDisposable _sourceChangedSubscription;

    public AudioSystem(World world, IAudioPlayer player)
    {
        _player = player;
        _sources = world.GetEntities().With<AudioSourceComponent>().AsSet();
        // One-shots start at publish time (synchronous), so a click sounds the frame it happens.
        _playRequestSubscription = world.Subscribe<PlaySoundRequest>(OnPlaySoundRequest);
        // Covers both explicit component removal and entity disposal: either way the
        // instance must be cut immediately, not leak past its entity.
        _sourceRemovedSubscription = world.SubscribeEntityComponentRemoved<AudioSourceComponent>(OnAudioSourceRemoved);
        // Overwriting the component on an entity that already has one (entity.Set(new ...))
        // fires ComponentChanged, never Removed: the discarded old value still holds the live
        // handle, so it must be cut here or the loop plays forever with no handle left to stop it.
        _sourceChangedSubscription = world.SubscribeEntityComponentChanged<AudioSourceComponent>(OnAudioSourceChanged);
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        foreach (ref readonly var entity in _sources.GetEntities())
        {
            Reconcile(entity.Get<AudioSourceComponent>());
        }

        ReleaseFinishedOneShots();
    }

    private void Reconcile(AudioSourceComponent source)
    {
        switch (source.State)
        {
            case AudioPlaybackState.Stopped:
                StopInstance(source);
                break;

            case AudioPlaybackState.Paused:
                if (source.Instance is { } pausable)
                {
                    if (source.AppliedState != AudioPlaybackState.Paused)
                    {
                        _player.Pause(pausable);
                        source.AppliedState = AudioPlaybackState.Paused;
                    }

                    // A paused instance still honors the "mutations propagate on the next
                    // reconcile" contract of Volume/Pitch/Pan (see AudioSourceComponent docs).
                    ApplyParameters(source, pausable);
                }

                break;

            case AudioPlaybackState.Playing:
                if (source.Instance is not { } handle)
                {
                    var started = _player.Play(source.SoundKey, source.Volume, source.Pitch, source.Pan, source.Loop);
                    if (started != IAudioPlayer.InvalidHandle)
                    {
                        source.Instance = started;
                        source.AppliedState = AudioPlaybackState.Playing;
                    }
                }
                else if (source.AppliedState == AudioPlaybackState.Paused)
                {
                    _player.Resume(handle);
                    source.AppliedState = AudioPlaybackState.Playing;
                    ApplyParameters(source, handle);
                }
                else if (!_player.IsPlaying(handle))
                {
                    // A non-looping source reached its natural end: release the instance and
                    // reflect reality on the component so it does not restart next frame.
                    StopInstance(source);
                    source.State = AudioPlaybackState.Stopped;
                }
                else
                {
                    ApplyParameters(source, handle);
                }

                break;
        }
    }

    private void StopInstance(AudioSourceComponent source)
    {
        if (source.Instance is { } handle) _player.Stop(handle);
        source.Instance = null;
        source.AppliedState = AudioPlaybackState.Stopped;
    }

    private void ApplyParameters(AudioSourceComponent source, int handle)
    {
        _player.SetVolume(handle, source.Volume);
        _player.SetPitch(handle, source.Pitch);
        _player.SetPan(handle, source.Pan);
    }

    private void OnPlaySoundRequest(in PlaySoundRequest request)
    {
        var handle = _player.Play(request.SoundKey, request.Volume, request.Pitch, request.Pan, loop: false);
        if (handle != IAudioPlayer.InvalidHandle) _oneShots.Add(handle);
    }

    private void OnAudioSourceRemoved(in Entity entity, in AudioSourceComponent source)
    {
        if (source is null) return;
        StopInstance(source);
    }

    private void OnAudioSourceChanged(in Entity entity, in AudioSourceComponent oldValue, in AudioSourceComponent newValue)
    {
        // ReferenceEquals covers NotifyChanged-style notifications (old == new): the value was
        // mutated in place, its instance is still owned — only a genuine replacement orphans one.
        if (oldValue is null || ReferenceEquals(oldValue, newValue)) return;
        StopInstance(oldValue);
    }

    private void ReleaseFinishedOneShots()
    {
        for (var i = _oneShots.Count - 1; i >= 0; i--)
        {
            var handle = _oneShots[i];
            if (_player.IsPlaying(handle)) continue;
            _player.Stop(handle); // release the finished instance
            _oneShots.RemoveAt(i);
        }
    }

    public void Dispose()
    {
        // Stop everything this system started; the injected player itself is owned by whoever
        // created it (dispose it with the screen, after the pipeline).
        foreach (ref readonly var entity in _sources.GetEntities())
        {
            StopInstance(entity.Get<AudioSourceComponent>());
        }

        foreach (var handle in _oneShots) _player.Stop(handle);
        _oneShots.Clear();

        _playRequestSubscription.Dispose();
        _sourceRemovedSubscription.Dispose();
        _sourceChangedSubscription.Dispose();
        _sources.Dispose();
        GC.SuppressFinalize(this);
    }
}
