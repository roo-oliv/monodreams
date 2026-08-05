#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.State;
using MonoDreams.System.Draw;
using Xunit;

namespace MonoDreams.Tests.Rendering;

/// <summary>
/// Guards the rendering premise "`SpriteAnimationSystem` mutates SOURCE fields only". The animator
/// advances <see cref="SpriteAnimationComponent"/> and writes the current frame onto the entity's
/// <see cref="SpriteInfoComponent"/> SOURCE fields — never <c>DrawComponent</c>, and never the
/// texture greedily (a null frame <c>AssetKey</c> keeps the sprite's current texture; the sheet is
/// reassigned only when the key CHANGES). Also pinned: the apply-on-index-CHANGE rule and its
/// <c>FrameIndex = -1</c> force-re-apply escape hatch, and that the serializer persists the AUTHORED
/// clip only — <c>Time</c> / <c>FrameIndex</c> never reach the file, so <c>load → save</c> stays a
/// byte fixed point.
///
/// Pure logic — a <see cref="World"/> and hand-built entities, no live <c>GraphicsDevice</c>.
/// </summary>
public class SpriteAnimationTests
{
    // ---- (a) Per-frame durations are honored ----

    [Fact]
    public void PerFrameDurations_AdvanceAtTheAuthoredBoundaries_AndLoop()
    {
        // An unequal clip: 0.6s / 0.1s / 0.3s (total 1.0s). A uniform-duration animator would land
        // on the wrong frame at every one of these ticks.
        using var world = new World();
        var entity = NewAnimatedSprite(world, Clip(loop: true,
            Frame(0, duration: 0.6f), Frame(16, duration: 0.1f), Frame(32, duration: 0.3f)));

        using var system = new SpriteAnimationSystem(world);
        var state = NewState();

        // First sight: frame 0 is applied WITHOUT advancing time (FrameIndex starts at -1).
        Tick(system, state, 0.25f);
        Assert.Equal(0, entity.Get<SpriteAnimationComponent>().FrameIndex);
        Assert.Equal(0f, entity.Get<SpriteAnimationComponent>().Time);
        Assert.Equal(new Rectangle(0, 0, 16, 16), entity.Get<SpriteInfoComponent>().Source);

        Tick(system, state, 0.5f); // t = 0.50 — still inside frame 0's 0.6s
        Assert.Equal(0, entity.Get<SpriteAnimationComponent>().FrameIndex);

        Tick(system, state, 0.15f); // t = 0.65 — past 0.6, inside frame 1's [0.6, 0.7)
        Assert.Equal(1, entity.Get<SpriteAnimationComponent>().FrameIndex);
        Assert.Equal(new Rectangle(16, 0, 16, 16), entity.Get<SpriteInfoComponent>().Source);

        Tick(system, state, 0.1f); // t = 0.75 — inside frame 2's [0.7, 1.0)
        Assert.Equal(2, entity.Get<SpriteAnimationComponent>().FrameIndex);
        Assert.Equal(new Rectangle(32, 0, 16, 16), entity.Get<SpriteInfoComponent>().Source);

        Tick(system, state, 0.3f); // t = 1.05 — wraps to 0.05, back on frame 0
        Assert.Equal(0, entity.Get<SpriteAnimationComponent>().FrameIndex);
        Assert.Equal(new Rectangle(0, 0, 16, 16), entity.Get<SpriteInfoComponent>().Source);
        Assert.True(entity.Get<SpriteAnimationComponent>().Playing);
    }

    // ---- (b) A non-looping clip holds its last frame and stops ----

    [Fact]
    public void NonLooping_HoldsTheLastFrame_AndClearsPlaying()
    {
        using var world = new World();
        var clip = Clip(loop: false, Frame(0), Frame(16), Frame(32));
        clip.DefaultFrameDuration = 0.1f; // no per-frame durations: 3 x 0.1 = 0.3s total
        var entity = NewAnimatedSprite(world, clip);

        using var system = new SpriteAnimationSystem(world);
        var state = NewState();

        Tick(system, state, 0f);    // apply frame 0
        Tick(system, state, 0.35f); // past the 0.3s total

        var anim = entity.Get<SpriteAnimationComponent>();
        Assert.Equal(2, anim.FrameIndex);
        Assert.False(anim.Playing);
        Assert.Equal(new Rectangle(32, 0, 16, 16), entity.Get<SpriteInfoComponent>().Source);

        // ...and it stays there: a stopped clip no longer accumulates time or changes frame.
        Tick(system, state, 5f);
        Assert.Equal(2, entity.Get<SpriteAnimationComponent>().FrameIndex);
        Assert.False(entity.Get<SpriteAnimationComponent>().Playing);
        Assert.Equal(new Rectangle(32, 0, 16, 16), entity.Get<SpriteInfoComponent>().Source);
    }

    // ---- (c) A null AssetKey never touches the texture (the atlas animation) ----

    [Fact]
    public void NullAssetKeyFrames_MoveOnlyTheSource_AndNeverCallTheResolver()
    {
        using var world = new World();
        var clip = Clip(loop: true, Frame(0), Frame(16), Frame(32)); // every AssetKey null
        clip.DefaultFrameDuration = 0.1f;
        var entity = NewAnimatedSprite(world, clip);

        var sheet = StubTexture();
        entity.Get<SpriteInfoComponent>().SpriteSheet = sheet;
        entity.Get<SpriteInfoComponent>().AssetKey = "Atlas/Hero";

        var resolver = new RecordingResolver(); // returns null AND records — neither should be needed
        using var system = new SpriteAnimationSystem(world, resolver.Resolve);
        var state = NewState();

        Tick(system, state, 0f);
        Tick(system, state, 0.1f);
        Tick(system, state, 0.1f);

        var sprite = entity.Get<SpriteInfoComponent>();
        Assert.Same(sheet, sprite.SpriteSheet);          // texture untouched...
        Assert.Equal("Atlas/Hero", sprite.AssetKey);     // ...and so is its key
        Assert.Equal(new Rectangle(32, 0, 16, 16), sprite.Source); // only the source moved
        Assert.Empty(resolver.Keys);
    }

    [Fact]
    public void ResolutionFailure_KeepsTheCurrentTexture_AndStillMovesTheSource()
    {
        using var world = new World();
        var clip = Clip(loop: true, Frame(0, assetKey: "Frames/missing"));
        var entity = NewAnimatedSprite(world, clip);

        var sheet = StubTexture();
        entity.Get<SpriteInfoComponent>().SpriteSheet = sheet;
        entity.Get<SpriteInfoComponent>().AssetKey = "Atlas/Hero";

        var resolver = new RecordingResolver(); // resolves to null
        using var system = new SpriteAnimationSystem(world, resolver.Resolve);

        Tick(system, NewState(), 0f);

        var sprite = entity.Get<SpriteInfoComponent>();
        Assert.Equal(new[] { "Frames/missing" }, resolver.Keys); // it was asked...
        Assert.Same(sheet, sprite.SpriteSheet);                  // ...and the failure is survivable
        Assert.Equal("Atlas/Hero", sprite.AssetKey);
        Assert.Equal(new Rectangle(0, 0, 16, 16), sprite.Source);
    }

    // ---- (d) The serializer persists the AUTHORED clip only ----

    [Fact]
    public void Serializer_MidAnimationRuntimeState_NeverReachesTheFile()
    {
        using var world = new World();
        var registry = NewEngineRegistry();

        var e = world.CreateEntity();
        e.Set(new SpriteAnimationComponent
        {
            Frames = new[]
            {
                Frame(0, duration: 0.2f),
                Frame(16, assetKey: "Frames/hero_02"),
            },
            DefaultFrameDuration = 0.08f,
            Loop = false,
            Playing = false,
            Speed = 1.5f,
            // Runtime playback state, mid-animation — must NOT be written.
            Time = 0.83f,
            FrameIndex = 1,
        });

        var json = registry.SerializeEntity(e).Components[EngineComponentSerializers.SpriteAnimationKey];

        // The authored fields are there...
        Assert.Equal(2, json.GetProperty("frames").GetArrayLength());
        Assert.Equal(0.08f, json.GetProperty("defaultFrameDuration").GetSingle());
        Assert.False(json.GetProperty("loop").GetBoolean());
        Assert.False(json.GetProperty("playing").GetBoolean());
        Assert.Equal(1.5f, json.GetProperty("speed").GetSingle());
        // ...a null frame assetKey is omitted, the set one is written.
        Assert.False(json.GetProperty("frames")[0].TryGetProperty("assetKey", out _));
        Assert.Equal("Frames/hero_02", json.GetProperty("frames")[1].GetProperty("assetKey").GetString());

        // ...and the runtime state is nowhere in the body.
        Assert.False(json.TryGetProperty("time", out _));
        Assert.False(json.TryGetProperty("frameIndex", out _));
        var raw = json.GetRawText();
        Assert.DoesNotContain("time", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("frameIndex", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serializer_WriteReadWrite_IsByteIdentical_AndReloadsAtFrameZero()
    {
        using var world = new World();
        var serializer = new SceneSerializer(NewEngineRegistry());

        var e = world.CreateEntity();
        e.Set(new SpriteInfoComponent { AssetKey = "Atlas/Hero", Target = RenderTargetID.Main });
        e.Set(new SpriteAnimationComponent
        {
            Frames = new[] { Frame(0, duration: 0.2f), Frame(16), Frame(32, assetKey: "Frames/hero_03") },
            DefaultFrameDuration = 0.09f,
            Loop = true,
            Playing = true,
            Speed = 2f,
            Time = 1.37f,   // mid-animation runtime state...
            FrameIndex = 2, // ...on the entity being saved
        });

        var scene = serializer.Serialize(new List<Entity> { e });
        var json1 = CanonicalJson.Serialize(scene);

        using var freshWorld = new World();
        var loaded = serializer.Deserialize(freshWorld, scene);
        var json2 = CanonicalJson.Serialize(serializer.Serialize(loaded));

        // load → save is a byte fixed point precisely because the runtime state is not persisted.
        Assert.Equal(json1, json2);

        var anim = loaded[0].Get<SpriteAnimationComponent>();
        Assert.Equal(-1, anim.FrameIndex); // "nothing applied yet" — the clip starts from frame 0
        Assert.Equal(0f, anim.Time);
        Assert.Equal(3, anim.Frames.Length);
        Assert.Equal(0.2f, anim.Frames[0].Duration);
        Assert.Null(anim.Frames[1].AssetKey);
        Assert.Equal("Frames/hero_03", anim.Frames[2].AssetKey);
        Assert.Equal(new Rectangle(32, 0, 16, 16), anim.Frames[2].Source);
        Assert.Equal(0.09f, anim.DefaultFrameDuration);
        Assert.Equal(2f, anim.Speed);
    }

    // ---- (e) The texture is resolved only when the asset key CHANGES ----

    [Fact]
    public void Resolver_IsInvokedOnKeyChangeOnly_AcrossAOneTexturePerFrameStrip()
    {
        using var world = new World();
        var clip = Clip(loop: true,
            Frame(0, assetKey: "Frames/run"),
            Frame(16, assetKey: "Frames/run"),  // same key as the previous frame — no second resolve
            Frame(32, assetKey: "Frames/idle"));
        clip.DefaultFrameDuration = 0.1f;
        var entity = NewAnimatedSprite(world, clip);
        entity.Get<SpriteInfoComponent>().SpriteSheet = StubTexture();
        entity.Get<SpriteInfoComponent>().AssetKey = "Frames/idle"; // the sprite's current texture

        var resolver = new RecordingResolver(perKeyTextures: true);
        using var system = new SpriteAnimationSystem(world, resolver.Resolve);
        var state = NewState();

        Tick(system, state, 0f);    // frame 0: "Frames/run" != "Frames/idle" ⇒ one resolve
        Assert.Equal(new[] { "Frames/run" }, resolver.Keys);
        Assert.Equal("Frames/run", entity.Get<SpriteInfoComponent>().AssetKey);

        Tick(system, state, 0.1f);  // frame 1: same key as the sprite now carries ⇒ no resolve
        Assert.Equal(1, entity.Get<SpriteAnimationComponent>().FrameIndex);
        Assert.Equal(new[] { "Frames/run" }, resolver.Keys);

        Tick(system, state, 0.1f);  // frame 2: key changes ⇒ one more resolve
        Assert.Equal(2, entity.Get<SpriteAnimationComponent>().FrameIndex);
        Assert.Equal(new[] { "Frames/run", "Frames/idle" }, resolver.Keys);
        Assert.Same(resolver.TextureFor("Frames/idle"), entity.Get<SpriteInfoComponent>().SpriteSheet);
    }

    [Fact]
    public void Resolver_IsNotInvoked_WhenTheFrameKeyEqualsTheSpritesCurrentKey()
    {
        using var world = new World();
        var entity = NewAnimatedSprite(world, Clip(loop: true, Frame(16, assetKey: "Atlas/Hero")));
        var sheet = StubTexture();
        entity.Get<SpriteInfoComponent>().SpriteSheet = sheet;
        entity.Get<SpriteInfoComponent>().AssetKey = "Atlas/Hero";

        var resolver = new RecordingResolver(perKeyTextures: true);
        using var system = new SpriteAnimationSystem(world, resolver.Resolve);

        Tick(system, NewState(), 0f);

        Assert.Empty(resolver.Keys);
        Assert.Same(sheet, entity.Get<SpriteInfoComponent>().SpriteSheet);
        Assert.Equal(new Rectangle(16, 0, 16, 16), entity.Get<SpriteInfoComponent>().Source);
    }

    // ---- (f) FrameIndex = -1 forces a re-apply after an external texture swap ----

    [Fact]
    public void FrameIndexMinusOne_ForcesAReApply_AfterGameCodeSwappedTheSpriteItself()
    {
        using var world = new World();
        var clip = Clip(loop: true, Frame(0), Frame(16), Frame(32));
        clip.DefaultFrameDuration = 0.1f;
        var entity = NewAnimatedSprite(world, clip);

        using var system = new SpriteAnimationSystem(world);
        var state = NewState();

        Tick(system, state, 0f);
        Tick(system, state, 0.1f);
        Tick(system, state, 0.1f);
        Assert.Equal(2, entity.Get<SpriteAnimationComponent>().FrameIndex);

        // Game code swaps the sprite's source itself (a white-flash blink / telegraph tint)...
        entity.Get<SpriteInfoComponent>().Source = new Rectangle(400, 400, 8, 8);

        // ...without the reset the animator would settle back on the same index and never re-apply.
        Tick(system, state, 0f);
        Assert.Equal(new Rectangle(400, 400, 8, 8), entity.Get<SpriteInfoComponent>().Source);

        entity.Get<SpriteAnimationComponent>().FrameIndex = -1;
        Tick(system, state, 0f);

        Assert.Equal(0, entity.Get<SpriteAnimationComponent>().FrameIndex);
        Assert.Equal(new Rectangle(0, 0, 16, 16), entity.Get<SpriteInfoComponent>().Source);
    }

    // ---- (g) Size follows the source only for a sprite that rendered unscaled ----

    [Fact]
    public void UnscaledSprite_SizeFollowsTheNewSource_WhileAnAuthoredScaleIsPreserved()
    {
        using var world = new World();
        var clip = Clip(loop: true, Frame(0), FrameOf(new Rectangle(16, 0, 32, 24)));
        clip.DefaultFrameDuration = 0.1f;

        // Unscaled: Size == the source's pixel size.
        var unscaled = NewAnimatedSprite(world, clip);
        // Deliberately scaled: Size is twice the source.
        var scaled = NewAnimatedSprite(world, clip);
        scaled.Get<SpriteInfoComponent>().Size = new Vector2(32f, 32f);

        using var system = new SpriteAnimationSystem(world);
        var state = NewState();
        Tick(system, state, 0f);
        Tick(system, state, 0.1f);

        Assert.Equal(new Vector2(32f, 24f), unscaled.Get<SpriteInfoComponent>().Size);
        Assert.Equal(new Vector2(32f, 32f), scaled.Get<SpriteInfoComponent>().Size);
    }

    // ---- (h) Speed is a playback-rate multiplier ----

    [Fact]
    public void Speed_ScalesThePlaybackRate()
    {
        using var world = new World();
        var clip = Clip(loop: true, Frame(0), Frame(16));
        clip.DefaultFrameDuration = 0.1f;

        var normal = NewAnimatedSprite(world, clip);
        var fast = NewAnimatedSprite(world, clip);
        fast.Get<SpriteAnimationComponent>().Speed = 2f;

        using var system = new SpriteAnimationSystem(world);
        var state = NewState();
        Tick(system, state, 0f);
        Tick(system, state, 0.06f); // 0.06s authored, 0.12s at 2x

        Assert.Equal(0, normal.Get<SpriteAnimationComponent>().FrameIndex);
        Assert.Equal(1, fast.Get<SpriteAnimationComponent>().FrameIndex);
    }

    // ---- helpers ----

    private static ComponentSerializerRegistry NewEngineRegistry()
    {
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        return registry;
    }

    private static GameState NewState() => new(new GameTime());

    /// <summary>Advances the frame clock by <paramref name="seconds"/> and runs the system once.</summary>
    private static void Tick(SpriteAnimationSystem system, GameState state, float seconds)
    {
        state.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(seconds)));
        system.Update(state);
    }

    private static SpriteAnimationFrame Frame(int x, float duration = 0f, string? assetKey = null) =>
        new() { AssetKey = assetKey, Source = new Rectangle(x, 0, 16, 16), Duration = duration };

    private static SpriteAnimationFrame FrameOf(Rectangle source, float duration = 0f) =>
        new() { Source = source, Duration = duration };

    private static SpriteAnimationComponent Clip(bool loop, params SpriteAnimationFrame[] frames) =>
        new() { Frames = frames, Loop = loop };

    /// <summary>
    /// The pair the animator queries: a <see cref="SpriteInfoComponent"/> whose <c>Size</c> matches its
    /// initial 16x16 source (i.e. rendered unscaled) plus the clip. No <c>DrawComponent</c> — the
    /// animator must never need or touch one.
    /// </summary>
    private static Entity NewAnimatedSprite(World world, SpriteAnimationComponent clip)
    {
        var e = world.CreateEntity();
        e.Set(new SpriteInfoComponent
        {
            Source = new Rectangle(0, 0, 16, 16),
            Size = new Vector2(16f, 16f),
            Color = Color.White,
            Target = RenderTargetID.Main,
        });
        e.Set(clip);
        return e;
    }

    /// <summary>A texture resolver stub that records every key it is asked for. By default it resolves
    /// to <c>null</c> (the "content key not found" path the system must survive); with
    /// <c>perKeyTextures</c> it hands back a stable stand-in texture per key so the key-change guard
    /// can be observed across a one-texture-per-frame strip.</summary>
    private sealed class RecordingResolver(bool perKeyTextures = false)
    {
        private readonly Dictionary<string, Texture2D> _textures = new();

        public List<string> Keys { get; } = new();

        public Texture2D? Resolve(string key)
        {
            Keys.Add(key);
            if (!perKeyTextures) return null;
            if (!_textures.TryGetValue(key, out var texture)) _textures[key] = texture = StubTexture();
            return texture;
        }

        public Texture2D TextureFor(string key) => _textures[key];
    }

    /// <summary>
    /// A stand-in <see cref="Texture2D"/>: the animator only ever copies the reference onto the
    /// sprite (and reads <c>Bounds</c> for whole-texture frames, which none of these tests use), and
    /// a real texture needs a <c>GraphicsDevice</c> no unit test has. Same ctor-less trick as
    /// <c>SpriteFlipTests.StubTexture</c>, with the finalizer suppressed (it would dereference the
    /// null graphics device).
    /// </summary>
    private static Texture2D StubTexture()
    {
        var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
        GC.SuppressFinalize(texture);
        return texture;
    }
}
