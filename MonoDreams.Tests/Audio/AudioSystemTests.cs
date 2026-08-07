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
/// key still fails loud, and the backend's voice cap (<see cref="InstancePlayLimitException"/>)
/// drops just that voice without disabling the player. The failure paths are forced through the
/// protected <c>LoadSoundEffect</c> / <c>StartInstance</c> hooks, so no real ContentManager or
/// hardware is involved.
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

    /// <summary>
    /// Counts both seams and hands back nothing: <c>LoadSoundEffect</c> returns <c>null!</c> (safe —
    /// with <c>StartInstance</c> also overridden, nothing ever dereferences the sound) and
    /// <c>StartInstance</c> returns a <c>null!</c> instance. Lets a test observe exactly how many
    /// loads a warm-then-play sequence costs.
    /// </summary>
    private sealed class CountingPlayer() : ContentAudioPlayer(null!)
    {
        public int LoadCalls { get; private set; }
        public int StartCalls { get; private set; }

        protected override SoundEffect LoadSoundEffect(string soundKey)
        {
            LoadCalls++;
            return null!;
        }

        protected override SoundEffectInstance StartInstance(
            SoundEffect sound, float volume, float pitch, float pan, bool loop)
        {
            StartCalls++;
            return null!;
        }
    }

    /// <summary>Loads every key successfully except <paramref name="failingKey"/>, which raises a
    /// plain content miss — the "one bad key in a warm list" case.</summary>
    private sealed class SelectiveFailurePlayer(string failingKey) : ContentAudioPlayer(null!)
    {
        public int LoadCalls { get; private set; }

        protected override SoundEffect LoadSoundEffect(string soundKey)
        {
            LoadCalls++;
            if (soundKey == failingKey) throw new ContentLoadException($"asset '{soundKey}' not found");
            return null!;
        }
    }

    private sealed class VoiceCapPlayer() : ContentAudioPlayer(null!)
    {
        public int StartAttempts { get; private set; }

        protected override SoundEffect LoadSoundEffect(string soundKey) => null!;

        protected override SoundEffectInstance StartInstance(
            SoundEffect sound, float volume, float pitch, float pan, bool loop)
        {
            StartAttempts++;
            throw new InstancePlayLimitException();
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
    public void VoiceCapReached_DropsTheVoice_WithoutDisablingThePlayer()
    {
        // The 257th simultaneous voice makes the backend throw InstancePlayLimitException
        // (MAX_PLAYING_INSTANCES). That is a transient mixing condition, not backend absence:
        // Play must swallow it (drop THIS voice, return InvalidHandle) WITHOUT flipping the
        // player into the permanent disabled no-op. (The failed instance's disposal is
        // structural: the production StartInstance disposes on any start failure.)
        var player = new VoiceCapPlayer();

        var first = player.Play("Sounds/click", 1f, 0f, 0f, loop: false);
        Assert.Equal(IAudioPlayer.InvalidHandle, first);
        Assert.Equal(1, player.StartAttempts);

        // NOT disabled: the next Play consults the backend again (unlike the degraded path,
        // which never does).
        var second = player.Play("Sounds/click", 1f, 0f, 0f, loop: true);
        Assert.Equal(IAudioPlayer.InvalidHandle, second);
        Assert.Equal(2, player.StartAttempts);
    }

    [Fact]
    public void MissingContentKey_StillFailsLoud()
    {
        // A plain content miss (no audio-backend failure in the chain) is a developer error —
        // it must propagate, not degrade into silence.
        var player = new ThrowingPlayer(new ContentLoadException("asset 'Sounds/typo' not found"));

        Assert.Throws<ContentLoadException>(() => player.Play("Sounds/typo", 1f, 0f, 0f, loop: false));
    }

    // ---- Preload: the warm decodes into the same cache Play reads ----

    [Fact]
    public void PreloadedKeys_NeverTouchTheLoaderInPlay()
    {
        // The point of the warm: a SoundEffect is a disk read plus a PCM decode, and Play runs
        // mid-frame. Preload moves that cost to a loading moment by filling the SAME cache Play
        // consults — so a warmed key's Play never reaches the loader at all.
        var player = new CountingPlayer();

        player.Preload(["Sounds/a", "Sounds/b"]);
        Assert.Equal(2, player.LoadCalls);

        var handle = player.Play("Sounds/a", 1f, 0f, 0f, loop: false);
        Assert.True(handle > IAudioPlayer.InvalidHandle); // playback still works, from the cache
        Assert.Equal(1, player.StartCalls);
        Assert.Equal(2, player.LoadCalls);               // ...and cost no load

        // Warming an already-warm key is free: the cache-hit skip means no second decode.
        player.Preload(["Sounds/a", "Sounds/b"]);
        Assert.Equal(2, player.LoadCalls);

        // NB: StartInstance handed back a null instance, so Stop/Dispose/IsPlaying are deliberately
        // not exercised on this handle — they would dereference it.
    }

    [Fact]
    public void PreloadFailingKey_WarnsAndSkips_WithoutAbortingTheWarm_AndStaysOnTheLazyPath()
    {
        // Warming is an optimisation: refusing to boot over one absent sound effect would turn a
        // cosmetic gap into a crash. The bad key is logged, skipped, and left UNCACHED — so Play
        // still fails loud on it, where a content miss is a real developer error.
        var logDir = Path.Combine(Path.GetTempPath(), "md-audio-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logDir);
        Logger.Shutdown(); // make sure we own the logger for this test
        Logger.Initialize(logDir);
        SelectiveFailurePlayer player;
        try
        {
            player = new SelectiveFailurePlayer("Sounds/bad");

            player.Preload(["Sounds/good1", "Sounds/bad", "Sounds/good2"]);

            // All three attempted: the failure skipped one key, it did not abort the warm.
            Assert.Equal(3, player.LoadCalls);
        }
        finally
        {
            Logger.Shutdown();
        }

        var logFile = Directory.GetFiles(logDir, "monodreams_*.log").Single();
        var warnings = File.ReadAllLines(logFile).Where(l => l.Contains("[ WARN]")).ToList();
        var warmWarning = Assert.Single(warnings);
        Assert.Contains("Could not preload sound", warmWarning);
        Assert.Contains("Sounds/bad", warmWarning);
        Directory.Delete(logDir, recursive: true);

        // The skipped key stayed on the lazy path: Play consults the loader again AND the content
        // miss propagates — degrading it into silence here would hide a typo'd key forever.
        Assert.Throws<ContentLoadException>(() => player.Play("Sounds/bad", 1f, 0f, 0f, loop: false));
        Assert.Equal(4, player.LoadCalls);
    }

    [Fact]
    public void BackendAbsenceDuringWarm_ShortCircuitsAndDisables_SoHeadlessWarmIsANoOp()
    {
        // On a deviceless machine (headless CI) the warm must cost nothing: the first key's backend
        // failure goes through the same Disable path Play uses and returns, so the rest of the list
        // is never attempted and the player is permanently a silent no-op.
        var logDir = Path.Combine(Path.GetTempPath(), "md-audio-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logDir);
        Logger.Shutdown(); // make sure we own the logger for this test
        Logger.Initialize(logDir);
        try
        {
            var player = new ThrowingPlayer(new NoAudioHardwareException("no audio device"));

            player.Preload(["Sounds/a", "Sounds/b", "Sounds/c"]); // no throw
            Assert.Equal(1, player.LoadAttempts);                 // short-circuited after the first

            // Disabled for the rest of its lifetime: Play is a silent no-op that never loads...
            Assert.Equal(IAudioPlayer.InvalidHandle, player.Play("Sounds/a", 1f, 0f, 0f, loop: false));
            Assert.Equal(1, player.LoadAttempts);

            // ...and a later warm is skipped wholesale by the same _disabled guard.
            player.Preload(["Sounds/d"]);
            Assert.Equal(1, player.LoadAttempts);
        }
        finally
        {
            Logger.Shutdown();
        }

        // One warning for the whole degraded lifetime — the warm reuses Play's Disable, it does not
        // add a second announcement.
        var logFile = Directory.GetFiles(logDir, "monodreams_*.log").Single();
        var warnings = File.ReadAllLines(logFile).Where(l => l.Contains("[ WARN]")).ToList();
        var audioWarning = Assert.Single(warnings);
        Assert.Contains("Audio backend unavailable", audioWarning);
        Directory.Delete(logDir, recursive: true);
    }
}
