#nullable enable
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Serialization;
using MonoDreams.LevelEditor.Serialization;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects PS5 <b>full component serialization for game components</b>: the reference game registers
/// serializers for its own components (<c>PlayerState</c>, <c>OrbitalMotion</c>,
/// <c>StopMotionEffect</c>, <c>DialogueZoneComponent</c>) via
/// <see cref="GameComponentSerializers.RegisterGameComponents"/>, and the engine ships one for
/// <c>CameraFollowTargetComponent</c> — so a native scene migrated from an LDtk/Blender level
/// round-trips those components through the registry (reconstructed from components, never by
/// re-running a factory). Pure logic — hand-built entities, no <c>GraphicsDevice</c>, no disk.
///
/// Covers the level-editor premise "Game components round-trip through registered serializers".
/// </summary>
public class GameComponentSerializerTests
{
    private static ComponentSerializerRegistry FullRegistry()
    {
        var r = new ComponentSerializerRegistry();
        r.RegisterEngineComponents();
        r.RegisterGameComponents();
        return r;
    }

    [Fact]
    public void RegisterGameComponents_RegistersEveryGameType()
    {
        var registry = FullRegistry();
        Assert.True(registry.IsRegistered(typeof(PlayerState)));
        Assert.True(registry.IsRegistered(typeof(OrbitalMotion)));
        Assert.True(registry.IsRegistered(typeof(StopMotionEffect)));
        Assert.True(registry.IsRegistered(typeof(DialogueZoneComponent)));
        // Engine gap closed in PS5:
        Assert.True(registry.IsRegistered(typeof(CameraFollowTargetComponent)));
    }

    [Fact]
    public void PlayerState_RoundTrips()
    {
        var registry = FullRegistry();
        using var src = new World();
        var e = src.CreateEntity();
        e.Set(new PlayerState());
        var data = registry.SerializeEntity(e);
        Assert.True(data.Components.ContainsKey(GameComponentSerializers.PlayerStateKey));

        using var dst = new World();
        var reloaded = dst.CreateEntity();
        registry.DeserializeEntity(reloaded, data);
        Assert.True(reloaded.Has<PlayerState>());
    }

    [Fact]
    public void OrbitalMotion_RoundTrips_AllFields()
    {
        var registry = FullRegistry();
        using var src = new World();
        var e = src.CreateEntity();
        e.Set(new OrbitalMotion { Angle = 1.5f, Radius = 20f, Speed = 5.25f, CenterOffset = new Vector2(8, 16) });
        var data = registry.SerializeEntity(e);

        using var dst = new World();
        var reloaded = dst.CreateEntity();
        registry.DeserializeEntity(reloaded, data);
        var m = reloaded.Get<OrbitalMotion>();
        Assert.Equal(1.5f, m.Angle);
        Assert.Equal(20f, m.Radius);
        Assert.Equal(5.25f, m.Speed);
        Assert.Equal(new Vector2(8, 16), m.CenterOffset);
    }

    [Fact]
    public void StopMotionEffect_RoundTrips_AllFields()
    {
        var registry = FullRegistry();
        using var src = new World();
        var e = src.CreateEntity();
        e.Set(new StopMotionEffect { BaseRotation = 0.1f, OffsetRadians = 0.035f, CycleDuration = 0.75f });
        var data = registry.SerializeEntity(e);

        using var dst = new World();
        var reloaded = dst.CreateEntity();
        registry.DeserializeEntity(reloaded, data);
        var s = reloaded.Get<StopMotionEffect>();
        Assert.Equal(0.1f, s.BaseRotation);
        Assert.Equal(0.035f, s.OffsetRadians);
        Assert.Equal(0.75f, s.CycleDuration);
    }

    [Fact]
    public void DialogueZone_RoundTrips_AllFields()
    {
        var registry = FullRegistry();
        using var src = new World();
        var e = src.CreateEntity();
        e.Set(new DialogueZoneComponent("Boldo", oneTimeOnly: true, autoStart: false, npcName: "Boldo")
        {
            HasBeenTriggered = true,
        });
        var data = registry.SerializeEntity(e);

        using var dst = new World();
        var reloaded = dst.CreateEntity();
        registry.DeserializeEntity(reloaded, data);
        var z = reloaded.Get<DialogueZoneComponent>();
        Assert.Equal("Boldo", z.YarnNodeName);
        Assert.Equal("Boldo", z.NpcName);
        Assert.True(z.OneTimeOnly);
        Assert.False(z.AutoStart);
        Assert.True(z.HasBeenTriggered);
    }

    [Fact]
    public void CameraFollowTarget_RoundTrips_WithAndWithoutBounds()
    {
        var registry = FullRegistry();

        // Free-follow (Bounds null → omitted from the file → null on read).
        using (var src = new World())
        using (var dst = new World())
        {
            var e = src.CreateEntity();
            e.Set(new CameraFollowTargetComponent { DampingX = 5f, DampingY = 6f, MaxDistanceX = 150f, MaxDistanceY = 100f, IsActive = true });
            var data = registry.SerializeEntity(e);
            var reloaded = dst.CreateEntity();
            registry.DeserializeEntity(reloaded, data);
            var c = reloaded.Get<CameraFollowTargetComponent>();
            Assert.Equal(5f, c.DampingX);
            Assert.Equal(6f, c.DampingY);
            Assert.Equal(150f, c.MaxDistanceX);
            Assert.Equal(100f, c.MaxDistanceY);
            Assert.True(c.IsActive);
            Assert.Null(c.Bounds);
        }

        // Clamped follow (Bounds set → round-trips as a rectangle).
        using (var src = new World())
        using (var dst = new World())
        {
            var e = src.CreateEntity();
            e.Set(new CameraFollowTargetComponent { Bounds = new Rectangle(1, 2, 300, 400), IsActive = false });
            var data = registry.SerializeEntity(e);
            var reloaded = dst.CreateEntity();
            registry.DeserializeEntity(reloaded, data);
            var c = reloaded.Get<CameraFollowTargetComponent>();
            Assert.Equal(new Rectangle(1, 2, 300, 400), c.Bounds);
            Assert.False(c.IsActive);
        }
    }
}
