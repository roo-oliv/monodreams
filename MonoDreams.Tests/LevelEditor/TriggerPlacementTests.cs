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
/// the emitted <see cref="CollisionMessage"/> carries the trigger's identity. Names the premise
/// "Trigger zones are Passive colliders identified by an auto-numbered EntityInfo string".
/// </summary>
public class TriggerPlacementTests
{
    private static readonly TriggerType Evidence = new("evidence", "Evidence", new Vector2(48, 48));

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
