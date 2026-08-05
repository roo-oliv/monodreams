using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.LevelEditor.Assets;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.Message;
using MonoDreams.State;
using MonoDreams.System.Collision;
using MonoDreams.System.Physics;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects trigger-zone placement (island-authoring Slice 3, plan §5.3): a placed trigger is a
/// <c>Passive</c> box collider whose identity rides <see cref="EntityInfoComponent"/> (Type = the
/// category prefix, Name = an auto-numbered scene-unique instance id), it round-trips through the
/// existing serializers unchanged, and — in a real collision frame — a moving player hits it and
/// the emitted <see cref="CollisionMessage"/> carries the trigger's identity. Also protects the two
/// additive <see cref="TriggerType"/> knobs that let a game teach the palette to build ITS objects
/// without the module knowing what it built: <c>Configure</c> (a hook that attaches game components
/// after the standard stack) and <c>ActiveLayers</c> (which scope the placed box, empty = a pure
/// marker that collides with nothing). Names the premise
/// "Trigger zones are Passive colliders identified by an auto-numbered EntityInfo string".
/// </summary>
public class TriggerPlacementTests
{
    private static readonly TriggerType Evidence = new("evidence", "Evidence", new Vector2(48, 48));

    /// <summary>Stands in for a caller-defined game component the module knows nothing about — the
    /// whole point of the <c>Configure</c> seam.</summary>
    private readonly record struct SpawnMarker(string Kind);

    [Fact]
    public void Create_MakesPassiveCenteredBox_WithPrefixIdentity()
    {
        using var world = new World();
        var trigger = TriggerFactory.Create(world, Evidence, new Vector2(100, 100), "evidence_01");

        var info = trigger.Get<EntityInfoComponent>();
        Assert.Equal("evidence", info.Type);
        Assert.Equal("evidence_01", info.Name);

        var box = trigger.Get<BoxColliderComponent>();
        Assert.True(box.Passive); // a trigger senses, it does not block
        Assert.Equal(new Vector2(48, 48), box.Size); // centered on the point by the shape
        Assert.Equal(new Vector2(100, 100), trigger.Get<TransformComponent>().Position);
    }

    [Fact]
    public void Create_RunsTheTypesConfigureHook_AfterTheStandardStack()
    {
        using var world = new World();

        // What the hook SEES is the assertion: the standard stack must already be on the entity when
        // the game's hook runs, so a hook may read the identity/box it is decorating (and so a hook
        // that overwrites part of the stack wins, rather than being silently clobbered).
        var sawIdentity = false;
        var sawTransform = false;
        var sawBox = false;
        var spawner = new TriggerType("spawner", "Spawner", new Vector2(32, 32))
        {
            Configure = e =>
            {
                sawIdentity = e.Has<EntityInfoComponent>();
                sawTransform = e.Has<TransformComponent>();
                sawBox = e.Has<BoxColliderComponent>();
                e.Set(new SpawnMarker("enemy"));
            },
        };

        var trigger = TriggerFactory.Create(world, spawner, new Vector2(10, 20), "spawner_01");

        Assert.True(sawIdentity);
        Assert.True(sawTransform);
        Assert.True(sawBox);

        // The caller's component rode along: one palette click authored a functional game object.
        Assert.Equal(new SpawnMarker("enemy"), trigger.Get<SpawnMarker>());

        // …and the standard stack survived the hook.
        Assert.Equal("spawner", trigger.Get<EntityInfoComponent>().Type);
        Assert.Equal("spawner_01", trigger.Get<EntityInfoComponent>().Name);
        Assert.Equal(new Vector2(10, 20), trigger.Get<TransformComponent>().Position);
        Assert.True(trigger.Get<BoxColliderComponent>().Passive);
        Assert.Equal(new Vector2(32, 32), trigger.Get<BoxColliderComponent>().Size);
    }

    [Fact]
    public void Create_ScopesTheBoxToTheTypesActiveLayers_EmptyMeaningAPureMarker()
    {
        using var world = new World();

        var scoped = new TriggerType("platform", "Platform") { ActiveLayers = new[] { 2, 5 } };
        var scopedTrigger = TriggerFactory.Create(world, scoped, Vector2.Zero, "platform_01");
        Assert.True(scopedTrigger.Get<BoxColliderComponent>().ActiveLayers.SetEquals(new[] { 2, 5 }));

        // An EMPTY array is a pure MARKER: selectable in the editor, collides with nothing in play.
        var marker = new TriggerType("marker", "Marker") { ActiveLayers = Array.Empty<int>() };
        var markerTrigger = TriggerFactory.Create(world, marker, Vector2.Zero, "marker_01");
        Assert.Empty(markerTrigger.Get<BoxColliderComponent>().ActiveLayers);

        // Omitted (null) leaves the collider's own default — a game that ignores layers is unaffected.
        var plainTrigger = TriggerFactory.Create(world, Evidence, Vector2.Zero, "evidence_01");
        Assert.True(plainTrigger.Get<BoxColliderComponent>().ActiveLayers.SetEquals(new[] { -1 }));
    }

    [Fact]
    public void NextName_AutoNumbersUniquelyPerPrefix()
    {
        using var world = new World();
        Assert.Equal("evidence_01", TriggerFactory.NextName(world, "evidence"));

        TriggerFactory.Create(world, Evidence, Vector2.Zero, "evidence_01");
        Assert.Equal("evidence_02", TriggerFactory.NextName(world, "evidence"));
        // A different prefix numbers independently.
        Assert.Equal("talkzone_01", TriggerFactory.NextName(world, "talkzone"));
    }

    [Fact]
    public void Trigger_RoundTripsThroughTheSerializers_Unchanged()
    {
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        var serializer = new SceneSerializer(registry);

        using var source = new World();
        var trigger = TriggerFactory.Create(source, Evidence, new Vector2(64, 80), "evidence_01");
        trigger.Set(new SceneObjectComponent());

        var scene = serializer.Serialize(SceneWriter.CollectMembership(source));

        using var loaded = new World();
        var restored = serializer.Deserialize(loaded, scene);
        Assert.Single(restored);
        var info = restored[0].Get<EntityInfoComponent>();
        Assert.Equal("evidence", info.Type);
        Assert.Equal("evidence_01", info.Name);
        Assert.True(restored[0].Get<BoxColliderComponent>().Passive);
        Assert.Equal(new Vector2(48, 48), restored[0].Get<BoxColliderComponent>().Size);
    }

    [Fact]
    public void MovingPlayer_EnteringATrigger_EmitsACollisionWithTheTriggerIdentity()
    {
        using var world = new World();
        using var runner = new DefaultParallelRunner(1);

        // The detection system auto-tags colliders via a component-ADDED subscription, so it must
        // exist BEFORE the colliders are created (mirrors the real screen: detection is composed at
        // setup, before any level load).
        var velocity = new TransformVelocitySystem(world, runner);
        var detect = new TransformCollisionDetectionSystem<CollisionMessage>(world, MilestoneCollision.Create);
        var commit = new MonoDreams.System.TransformCommitSystem(world, runner);

        // The player: an Active (non-passive) box that moves right into the trigger.
        var player = world.CreateEntity();
        player.Set(new EntityInfoComponent("Player"));
        player.Set(new TransformComponent(new Vector2(0, 0)));
        player.Set(new BoxColliderComponent(new Vector2(16, 16)));
        player.Set(new MonoDreams.Component.Physics.VelocityComponent(new Vector2(20, 0)));

        // The trigger: a Passive zone at x=60 with the evidence identity.
        var trigger = TriggerFactory.Create(world, Evidence, new Vector2(60, 0), "evidence_01");

        var hits = new List<CollisionMessage>();
        world.Subscribe((in CollisionMessage m) => hits.Add(m));

        var play = new GameState(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1))) { RunMode = RunMode.Play };
        for (var i = 0; i < 6; i++)
        {
            velocity.Update(play); // moves the player 20 units/frame
            detect.Update(play);   // publishes on overlap/sweep
            commit.Update(play);   // lastpos = pos, so next frame's Delta is the step
        }

        Assert.Contains(hits, m =>
            m.ColliderB == trigger
            && m.ColliderB.Get<EntityInfoComponent>().Type == "evidence"
            && m.ColliderB.Get<EntityInfoComponent>().Name == "evidence_01");
    }
}
