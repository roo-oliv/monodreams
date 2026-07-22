using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using MonoDreams.Component.Audio;
using MonoDreams.Message;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Audio;

namespace MonoDreams.Tests.Audio;

/// <summary>
/// Protects the audio module's contract (see <c>MonoDreams/audio/docs/premises.md</c>):
/// one-shot fullplay via <see cref="PlaySoundRequest"/>, lifecycle playback via
/// <see cref="AudioSourceComponent"/> (loop / cut / pause-resume), simultaneous instances,
/// parameter propagation, and instance lifecycle tied to component lifecycle.
///
/// Pure logic — a fake <see cref="IAudioPlayer"/> records every backend interaction, so no
/// audio hardware (and no GraphicsDevice) is needed.
/// </summary>
public class AudioSystemTests : IDisposable
{
    private sealed class FakeAudioPlayer : IAudioPlayer
    {
        public sealed class Instance
        {
            public string SoundKey = "";
            public float Volume;
            public float Pitch;
            public float Pan;
            public bool Loop;
            public bool Live = true;    // started and not yet stopped/released
            public bool Paused;
            public bool Finished;       // test-set: simulates a non-loop instance reaching its end
            public int StopCalls;
            public int PauseCalls;
            public int ResumeCalls;
        }

        public readonly List<Instance> Instances = [];
        public int PlayCalls => Instances.Count;

        public Instance Get(int handle) => Instances[handle - 1];

        public int Play(string soundKey, float volume, float pitch, float pan, bool loop)
        {
            Instances.Add(new Instance { SoundKey = soundKey, Volume = volume, Pitch = pitch, Pan = pan, Loop = loop });
            return Instances.Count; // handles are 1-based, positive — 0 stays invalid
        }

        public void Stop(int handle)
        {
            var instance = Get(handle);
            instance.Live = false;
            instance.StopCalls++;
        }

        public void Pause(int handle)
        {
            var instance = Get(handle);
            instance.Paused = true;
            instance.PauseCalls++;
        }

        public void Resume(int handle)
        {
            var instance = Get(handle);
            instance.Paused = false;
            instance.ResumeCalls++;
        }

        public void SetVolume(int handle, float volume) => Get(handle).Volume = volume;
        public void SetPitch(int handle, float pitch) => Get(handle).Pitch = pitch;
        public void SetPan(int handle, float pan) => Get(handle).Pan = pan;

        public bool IsPlaying(int handle)
        {
            var instance = Get(handle);
            return instance.Live && !instance.Paused && !instance.Finished;
        }

        public void Dispose() { }
    }

    private readonly World _world = new();
    private readonly FakeAudioPlayer _player = new();
    private readonly AudioSystem _system;

    public AudioSystemTests()
    {
        _system = new AudioSystem(_world, _player);
    }

    public void Dispose()
    {
        _system.Dispose();
        _world.Dispose();
    }

    private static GameState NewState() => new(new GameTime());

    private Entity NewSource(AudioSourceComponent source)
    {
        var entity = _world.CreateEntity();
        entity.Set(source);
        return entity;
    }

    // ---- Contract 2: one-shot fullplay via PlaySoundRequest ----

    [Fact]
    public void PlaySoundRequest_StartsExactlyOneOneShot_WithRequestedParameters()
    {
        _world.Publish(new PlaySoundRequest("Sounds/click", Volume: 0.5f, Pitch: 0.25f, Pan: -1f));

        Assert.Equal(1, _player.PlayCalls);
        var instance = _player.Instances[0];
        Assert.Equal("Sounds/click", instance.SoundKey);
        Assert.Equal(0.5f, instance.Volume);
        Assert.Equal(0.25f, instance.Pitch);
        Assert.Equal(-1f, instance.Pan);
        Assert.False(instance.Loop); // one-shots never loop

        // Reconciling while it is still playing neither restarts nor stops it.
        _system.Update(NewState());
        Assert.Equal(1, _player.PlayCalls);
        Assert.True(instance.Live);
        Assert.Equal(0, instance.StopCalls);
    }

    [Fact]
    public void OneShot_PlaysToCompletion_ThenItsInstanceIsReleased()
    {
        _world.Publish(new PlaySoundRequest("Sounds/click"));
        var instance = _player.Instances[0];

        _system.Update(NewState());
        Assert.Equal(0, instance.StopCalls); // still audible — untouched

        instance.Finished = true; // the sound reached its natural end
        _system.Update(NewState());
        Assert.Equal(1, instance.StopCalls); // released exactly once
        Assert.False(instance.Live);

        _system.Update(NewState());
        Assert.Equal(1, instance.StopCalls); // no double-release: it left the tracking list
        Assert.Equal(1, _player.PlayCalls);  // and it never restarts
    }

    // ---- Contract 3: looping source keeps a single live instance ----

    [Fact]
    public void LoopingSource_StartsOnFirstReconcile_AndKeepsASingleInstanceAcrossFrames()
    {
        NewSource(new AudioSourceComponent("Sounds/wind", loop: true));

        _system.Update(NewState());
        _system.Update(NewState());
        _system.Update(NewState());

        Assert.Equal(1, _player.PlayCalls); // started once, never restarted
        var instance = _player.Instances[0];
        Assert.True(instance.Loop);
        Assert.True(instance.Live);
        Assert.Equal(0, instance.StopCalls);
    }

    // ---- Contract 4: cutting a live source (the jukebox mid-play cut), three ways ----

    [Fact]
    public void SettingStateStopped_CutsTheLiveInstance()
    {
        var entity = NewSource(new AudioSourceComponent("Sounds/jukebox"));
        _system.Update(NewState());
        var instance = _player.Instances[0];
        Assert.True(instance.Live);

        entity.Get<AudioSourceComponent>().State = AudioPlaybackState.Stopped;
        _system.Update(NewState());

        Assert.False(instance.Live);
        Assert.Equal(1, instance.StopCalls);
        Assert.Null(entity.Get<AudioSourceComponent>().Instance);

        // Stays cut — no restart while Stopped.
        _system.Update(NewState());
        Assert.Equal(1, _player.PlayCalls);
    }

    [Fact]
    public void RemovingTheComponent_CutsTheLiveInstance()
    {
        var entity = NewSource(new AudioSourceComponent("Sounds/jukebox"));
        _system.Update(NewState());
        var instance = _player.Instances[0];

        entity.Remove<AudioSourceComponent>();

        // The cut is immediate (inside the removal callback), no Update needed.
        Assert.False(instance.Live);
        Assert.Equal(1, instance.StopCalls);
    }

    [Fact]
    public void DisposingTheEntity_CutsTheLiveInstance()
    {
        var entity = NewSource(new AudioSourceComponent("Sounds/jukebox"));
        _system.Update(NewState());
        var instance = _player.Instances[0];

        entity.Dispose();

        Assert.False(instance.Live);
        Assert.Equal(1, instance.StopCalls);
    }

    [Fact]
    public void OverwritingTheComponentViaSet_CutsTheOldValuesLiveInstance()
    {
        var entity = NewSource(new AudioSourceComponent("Sounds/track1", loop: true));
        _system.Update(NewState());
        var track1 = _player.Instances[0];
        Assert.True(track1.Live);

        // Set() on an entity that already has the component fires ComponentChanged (never
        // Removed) in DefaultEcs: the discarded old value still holds the live handle, so the
        // system must cut it immediately or the loop plays forever with nothing left to stop it.
        entity.Set(new AudioSourceComponent("Sounds/track2", loop: true));

        Assert.False(track1.Live);
        Assert.Equal(1, track1.StopCalls);

        // The replacement source starts cleanly on the next reconcile.
        _system.Update(NewState());
        Assert.Equal(2, _player.PlayCalls);
        var track2 = _player.Instances[1];
        Assert.Equal("Sounds/track2", track2.SoundKey);
        Assert.True(track2.Live);
        Assert.Same(track2, _player.Get(entity.Get<AudioSourceComponent>().Instance!.Value));
    }

    // ---- Contract 5: pause / resume on a live source ----

    [Fact]
    public void PausedSource_PausesTheInstance_AndResumeContinuesWithoutRestart()
    {
        var entity = NewSource(new AudioSourceComponent("Sounds/music"));
        _system.Update(NewState());
        var instance = _player.Instances[0];

        entity.Get<AudioSourceComponent>().State = AudioPlaybackState.Paused;
        _system.Update(NewState());
        Assert.True(instance.Paused);
        Assert.Equal(1, instance.PauseCalls);

        // Reconciling a paused source again neither re-pauses nor releases it.
        _system.Update(NewState());
        Assert.Equal(1, instance.PauseCalls);
        Assert.True(instance.Live);

        entity.Get<AudioSourceComponent>().State = AudioPlaybackState.Playing;
        _system.Update(NewState());
        Assert.False(instance.Paused);
        Assert.Equal(1, instance.ResumeCalls);
        Assert.Equal(1, _player.PlayCalls); // resumed, not restarted
    }

    // ---- Contract 6: >= 3 simultaneous audios with independent handles ----

    [Fact]
    public void MultipleSourcesAndOneShots_PlaySimultaneously_OnIndependentInstances()
    {
        var wind = NewSource(new AudioSourceComponent("Sounds/wind", loop: true));
        var jukebox = NewSource(new AudioSourceComponent("Sounds/jukebox"));
        _world.Publish(new PlaySoundRequest("Sounds/click"));
        _world.Publish(new PlaySoundRequest("Sounds/thud"));

        _system.Update(NewState());

        Assert.Equal(4, _player.PlayCalls);
        Assert.All(_player.Instances, i => Assert.True(i.Live));
        Assert.Equal(4, _player.Instances.Select(i => i.SoundKey).Distinct().Count());

        // Cutting one source leaves the other three playing.
        jukebox.Get<AudioSourceComponent>().State = AudioPlaybackState.Stopped;
        _system.Update(NewState());

        var jukeboxInstance = _player.Instances.Single(i => i.SoundKey == "Sounds/jukebox");
        Assert.False(jukeboxInstance.Live);
        Assert.Equal(3, _player.Instances.Count(i => i.Live));
        Assert.True(wind.Get<AudioSourceComponent>().Instance.HasValue);
    }

    // ---- Contract 8: volume/pitch/pan mutations propagate on the next reconcile ----

    [Fact]
    public void VolumePitchPanMutations_PropagateToTheLiveInstance_OnNextReconcile()
    {
        var entity = NewSource(new AudioSourceComponent("Sounds/wind", loop: true, volume: 1f, pitch: 0f, pan: 0f));
        _system.Update(NewState());
        var instance = _player.Instances[0];
        Assert.Equal(1f, instance.Volume);

        var source = entity.Get<AudioSourceComponent>();
        source.Volume = 0.3f;
        source.Pitch = -0.5f;
        source.Pan = 1f;
        _system.Update(NewState());

        Assert.Equal(0.3f, instance.Volume);
        Assert.Equal(-0.5f, instance.Pitch);
        Assert.Equal(1f, instance.Pan);
        Assert.Equal(1, _player.PlayCalls); // mutation is propagation, never a restart
    }

    [Fact]
    public void VolumePitchPanMutations_PropagateWhilePaused_WithoutRePausing()
    {
        var entity = NewSource(new AudioSourceComponent("Sounds/music", volume: 1f));
        _system.Update(NewState());
        var instance = _player.Instances[0];

        var source = entity.Get<AudioSourceComponent>();
        source.State = AudioPlaybackState.Paused;
        _system.Update(NewState());
        Assert.True(instance.Paused);

        // The "next reconcile" contract holds while paused too: the instance is live, only
        // silent — turning the volume down during a pause menu must not wait for resume.
        source.Volume = 0.2f;
        _system.Update(NewState());

        Assert.Equal(0.2f, instance.Volume);
        Assert.Equal(1, instance.PauseCalls);  // the pause itself stays transition-guarded
        Assert.True(instance.Paused);
    }

    // ---- Desired-state reconciliation: a finished non-loop source settles at Stopped ----

    [Fact]
    public void NonLoopingSource_ThatFinishes_FlipsItselfToStopped_AndDoesNotRestart()
    {
        var entity = NewSource(new AudioSourceComponent("Sounds/stinger"));
        _system.Update(NewState());
        var instance = _player.Instances[0];

        instance.Finished = true; // reached its natural end
        _system.Update(NewState());

        var source = entity.Get<AudioSourceComponent>();
        Assert.Equal(AudioPlaybackState.Stopped, source.State); // the system reflects reality
        Assert.Null(source.Instance);
        Assert.Equal(1, instance.StopCalls); // released

        _system.Update(NewState());
        Assert.Equal(1, _player.PlayCalls); // and it never restarts on its own
    }

    // ---- System teardown: everything the system started is stopped, subscriptions dropped ----

    [Fact]
    public void DisposingTheSystem_StopsEverythingItStarted_AndUnsubscribes()
    {
        NewSource(new AudioSourceComponent("Sounds/wind", loop: true));
        _world.Publish(new PlaySoundRequest("Sounds/click"));
        _system.Update(NewState());
        Assert.Equal(2, _player.Instances.Count(i => i.Live));

        _system.Dispose();

        Assert.Equal(0, _player.Instances.Count(i => i.Live));

        // Unsubscribed: publishing after dispose starts nothing.
        _world.Publish(new PlaySoundRequest("Sounds/late"));
        Assert.Equal(2, _player.PlayCalls);
    }

    // ---- Edit-mode policy: Freeze stops reconciliation, not already-live playback ----

    [Fact]
    public void FreezeGatedInEdit_SkipsReconciliation_ButAlreadyLiveInstancesKeepPlaying()
    {
        // The reference edit-mode registration (see the audio premises and the module demo):
        // audio is game logic, so the single AudioSystem is Freeze-gated in edit-capable screens.
        var gate = new GatedSystem(_system, EditTimeBehavior.Freeze);

        // Play mode: the wind loop starts normally through the gate.
        var windEntity = NewSource(new AudioSourceComponent("Sounds/wind", loop: true));
        gate.Update(NewState());
        Assert.Equal(1, _player.PlayCalls);
        var wind = _player.Instances[0];
        Assert.True(wind.Live);

        // Edit mode: the gate skips the reconcile — a new desired-Playing source is NOT
        // started, and the already-live loop is NOT stopped (Freeze operates at the update
        // seam; it cannot reach into the backend to silence live instances).
        var edit = NewState();
        edit.RunMode = RunMode.Edit;
        NewSource(new AudioSourceComponent("Sounds/jukebox", loop: true));
        gate.Update(edit);
        Assert.Equal(1, _player.PlayCalls); // the jukebox did not start
        Assert.True(wind.Live);             // the wind keeps sounding in Edit
        Assert.Equal(0, wind.StopCalls);

        // Lifecycle cuts stay live in Edit: the removal subscription is not gated, so
        // disposing the entity that owns the live loop cuts it immediately.
        windEntity.Dispose();
        Assert.False(wind.Live);
        Assert.Equal(1, wind.StopCalls);

        // Back in Play mode the pending source starts on the next reconcile.
        gate.Update(NewState());
        Assert.Equal(2, _player.PlayCalls);
        Assert.Equal("Sounds/jukebox", _player.Instances[1].SoundKey);
    }
}

/// <summary>
/// Contract 7: without an audio backend, <see cref="ContentAudioPlayer"/> degrades to a
/// silent no-op — a single <c>Logger.Warning</c>, no throw — while a plain missing content
/// key still fails loud. The failure path is forced through the protected
/// <c>LoadSoundEffect</c> hook, so no real ContentManager or hardware is involved.
/// </summary>
public class ContentAudioPlayerTests
{
    private sealed class ThrowingPlayer(Exception failure) : ContentAudioPlayer(null!)
    {
        public int LoadAttempts { get; private set; }

        protected override SoundEffect LoadSoundEffect(string soundKey)
        {
            LoadAttempts++;
            throw failure;
        }
    }

    [Fact]
    public void WithoutAudioBackend_PlayDegradesToSilentNoOp_WithASingleWarning()
    {
        var logDir = Path.Combine(Path.GetTempPath(), "md-audio-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logDir);
        Logger.Shutdown(); // make sure we own the logger for this test
        Logger.Initialize(logDir);
        try
        {
            var player = new ThrowingPlayer(new NoAudioHardwareException("no audio device"));

            // First Play hits the backend failure: no throw, invalid handle, disabled from now on.
            var first = player.Play("Sounds/click", 1f, 0f, 0f, loop: false);
            Assert.Equal(IAudioPlayer.InvalidHandle, first);
            Assert.Equal(1, player.LoadAttempts);

            // Subsequent calls are silent no-ops — the loader is never consulted again.
            var second = player.Play("Sounds/other", 1f, 0f, 0f, loop: true);
            Assert.Equal(IAudioPlayer.InvalidHandle, second);
            Assert.Equal(1, player.LoadAttempts);

            // Every other member is safe on the invalid handle.
            player.Stop(first);
            player.Pause(first);
            player.Resume(first);
            player.SetVolume(first, 0.5f);
            player.SetPitch(first, 0.5f);
            player.SetPan(first, 0.5f);
            Assert.False(player.IsPlaying(first));
        }
        finally
        {
            Logger.Shutdown();
        }

        // Exactly one warning was logged for the whole degraded lifetime.
        var logFile = Directory.GetFiles(logDir, "monodreams_*.log").Single();
        var warnings = File.ReadAllLines(logFile).Where(l => l.Contains("[ WARN]")).ToList();
        var audioWarning = Assert.Single(warnings);
        Assert.Contains("Audio backend unavailable", audioWarning);
        Directory.Delete(logDir, recursive: true);
    }

    [Fact]
    public void MissingNativeAudioLibrary_AlsoDegrades_EvenWhenWrapped()
    {
        // A DllNotFoundException buried in a ContentLoadException chain (how MonoGame surfaces a
        // missing native openal on headless machines) is still recognized as backend absence.
        var wrapped = new ContentLoadException("loading failed",
            new TypeInitializationException("Microsoft.Xna.Framework.Audio.SoundEffect",
                new DllNotFoundException("openal not found")));
        var player = new ThrowingPlayer(wrapped);

        var handle = player.Play("Sounds/click", 1f, 0f, 0f, loop: false);

        Assert.Equal(IAudioPlayer.InvalidHandle, handle);
        Assert.Equal(1, player.LoadAttempts);
    }

    [Fact]
    public void MissingContentKey_StillFailsLoud()
    {
        // A plain content miss (no audio-backend failure in the chain) is a developer error —
        // it must propagate, not degrade into silence.
        var player = new ThrowingPlayer(new ContentLoadException("asset 'Sounds/typo' not found"));

        Assert.Throws<ContentLoadException>(() => player.Play("Sounds/typo", 1f, 0f, 0f, loop: false));
    }
}
