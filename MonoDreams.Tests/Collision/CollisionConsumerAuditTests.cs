using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Dialogue;
using MonoDreams.Examples.Collision;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Component.Runner;
using MonoDreams.Examples.System;
using MonoDreams.Examples.System.Dialogue;
using MonoDreams.Examples.System.Runner;
using MonoDreams.Extension;
using MonoDreams.Message;
using MonoDreams.State;
using MonoDreams.System.Collision;
using Xunit;

namespace MonoDreams.Tests.Collision;

/// <summary>
/// THE COLLIDERS-AS-ENTITIES CONSUMER AUDIT (CE-D, design pre-mortem #4 — completion). Under the
/// entity model identity lives on the collider entity (a zone's <see cref="DialogueZoneComponent"/>,
/// a trigger's <see cref="EntityInfoComponent"/>) while physics lives on the resolved BODY, so a
/// consumer that reads the wrong side of <see cref="CollisionMessage"/> silently misses identity or
/// mutates a collider child instead of the game object. This class PROVES every shipping-code
/// <see cref="CollisionMessage"/> consumer/classifier reads the correct side, closing the audit the
/// collision premise's "CollisionMessage carries both collider and body granularities" table names.
///
/// <para><b>The complete consumer set</b> (grep <c>in CollisionMessage</c> / <c>Subscribe&lt;CollisionMessage&gt;</c>
/// across engine + Examples + Demos) and where each is proven:</para>
/// <list type="table">
///   <item><b>resolution systems</b> (engine) — <c>ColliderA/B</c> shapes + <c>BodyA</c> write-back +
///     <c>BodyB</c> touch — proven by <see cref="ColliderEntityTests"/>
///     (<c>Resolution_CorrectsTheBody_NotTheColliderChild</c>, <c>TwoColliderBody_…</c>).</item>
///   <item><b><see cref="GameCollisionHelper"/></b> (LoadLevel classifier) — identity collider-first-
///     then-body — proven HERE.</item>
///   <item><b><see cref="ZoneDialogueTriggerSystem"/></b> (LoadLevel) — <c>ColliderB</c> (the zone's
///     <see cref="DialogueZoneComponent"/>) — proven HERE (positive + a negative that it never reads
///     <c>BodyB</c>).</item>
///   <item><b><see cref="RunnerCollisionHandlerSystem"/></b> (InfiniteRunner) — <c>BodyA</c> (player
///     state) + <c>BodyB</c> (dispose the whole body) — proven HERE.</item>
///   <item><b><see cref="NPCInteractionSystem"/></b> (LoadLevel) — a PROXIMITY consumer (reads the
///     collider off the entity OR its collider CHILD, not via the message) — proven HERE.</item>
///   <item><b><c>BallBounceSystem</c></b> (physics demo) — <c>BodyA</c> write + <c>BodyB</c> FloorTag;
///     collider == body there, so the side is vacuous — the live render-path smoke is
///     <c>HeadlessDemoTests.HeadlessPhysicsDemo_…</c>.</item>
/// </list>
/// </summary>
public class CollisionConsumerAuditTests
{
    private static GameState Play() =>
        new(new GameTime()) { RunMode = RunMode.Play };

    private static CollisionMessage Dialogue(Entity colliderA, Entity colliderB, Entity bodyA, Entity bodyB)
        => new(colliderA, colliderB, bodyA, bodyB, Vector2.Zero, Vector2.UnitX, 0f, 0f, -1, CollisionType.Dialogue);

    // ─── GameCollisionHelper: identity is read COLLIDER-first-then-body ──────────────────────────

    [Fact]
    public void GameCollisionHelper_ReadsPlayerIdentityFromBody_AndZoneIdentityFromCollider()
    {
        using var world = new World();

        // Player: identity on the BODY; its collider is a CHILD with no identity of its own.
        var player = world.CreateEntity();
        player.Set(new EntityInfoComponent("Player"));
        var playerCollider = world.CreateEntity();
        playerCollider.Set(new TransformComponent());
        playerCollider.Set(new BoxColliderComponent(new Vector2(16, 16)));
        playerCollider.SetParent(player);

        // Zone: identity ON the collider entity itself (standalone → collider == body).
        var zone = world.CreateEntity();
        zone.Set(new EntityInfoComponent("Zone"));
        zone.Set(new TransformComponent());
        zone.Set(new BoxColliderComponent(new Vector2(48, 48), passive: true));

        // A = the moving player (collider child + player body), B = the zone.
        var msg = GameCollisionHelper.Create(playerCollider, zone, player, zone,
            Vector2.Zero, Vector2.UnitX, 0f, 0f, -1);

        // "Player" resolved via the BODY fallback (its collider child had none); "Zone" via the COLLIDER.
        Assert.Equal(CollisionType.Dialogue, msg.Type);
    }

    [Fact]
    public void GameCollisionHelper_ColliderIdentityWinsOverBodyIdentity()
    {
        using var world = new World();

        var player = world.CreateEntity();
        player.Set(new EntityInfoComponent("Player"));
        var playerCollider = world.CreateEntity();
        playerCollider.Set(new TransformComponent());
        playerCollider.Set(new BoxColliderComponent(new Vector2(16, 16)));
        playerCollider.SetParent(player);

        // A zone collider whose BODY (a plain parent) carries a DIFFERENT identity. The collider's own
        // "NPCZone" must win — proving the read is collider-first, not body-first.
        var building = world.CreateEntity();
        building.Set(new EntityInfoComponent("Building"));
        var zoneCollider = world.CreateEntity();
        zoneCollider.Set(new EntityInfoComponent("NPCZone"));
        zoneCollider.Set(new TransformComponent());
        zoneCollider.Set(new BoxColliderComponent(new Vector2(48, 48), passive: true));
        zoneCollider.SetParent(building);

        var msg = GameCollisionHelper.Create(playerCollider, zoneCollider, player, building,
            Vector2.Zero, Vector2.UnitX, 0f, 0f, -1);

        // "Player" (body B has "Building", but zoneCollider's own "NPCZone" is read first).
        Assert.Equal(CollisionType.NPCInteraction, msg.Type);
    }

    // ─── ZoneDialogueTriggerSystem: the zone component is read off ColliderB, never BodyB ────────

    [Fact]
    public void ZoneDialogueTrigger_ReadsColliderB_ForTheZoneComponent_AndPublishesStart()
    {
        using var world = new World();
        using var sys = new ZoneDialogueTriggerSystem(world);

        var starts = new List<DialogueStartMessage>();
        world.Subscribe((in DialogueStartMessage m) => starts.Add(m));

        // The DialogueZone (identity) lives on the COLLIDER entity — the trigger IS a collider entity.
        var zoneCollider = world.CreateEntity();
        zoneCollider.Set(new DialogueZoneComponent("Boldo_Start", autoStart: true));
        var zoneBody = world.CreateEntity(); // its own body; carries NO zone component
        var player = world.CreateEntity();

        world.Publish(Dialogue(player, zoneCollider, player, zoneBody));

        Assert.Single(starts);
        Assert.Equal(zoneCollider, starts[0].DialogueEntity); // fired FROM the collider entity
        Assert.Equal("Boldo_Start", starts[0].StartNode);
        Assert.True(zoneCollider.Get<DialogueZoneComponent>().HasBeenTriggered);
    }

    [Fact]
    public void ZoneDialogueTrigger_NeverFallsBackToBodyB_ForTheZoneComponent()
    {
        using var world = new World();
        using var sys = new ZoneDialogueTriggerSystem(world);

        var starts = new List<DialogueStartMessage>();
        world.Subscribe((in DialogueStartMessage m) => starts.Add(m));

        // The zone component sits on BODY B only; ColliderB has none. A body-reading consumer would
        // fire — the correct ColliderB read finds nothing and stays silent.
        var colliderB = world.CreateEntity();
        var bodyB = world.CreateEntity();
        bodyB.Set(new DialogueZoneComponent("ShouldNotFire", autoStart: true));
        var player = world.CreateEntity();

        world.Publish(Dialogue(player, colliderB, player, bodyB));

        Assert.Empty(starts);
    }

    // ─── RunnerCollisionHandlerSystem: state on BodyA, dispose the whole BodyB ───────────────────

    [Fact]
    public void RunnerCollisionHandler_Collectible_ScoresBodyA_DisposesTheWholeBodyB()
    {
        using var world = new World();
        using var sys = new RunnerCollisionHandlerSystem(world);

        var player = world.CreateEntity();            // BodyA — RunnerState rides the body
        var runnerState = new RunnerState();
        player.Set(runnerState);

        // A charm: a BODY with a collider CHILD. The handler must dispose the BODY, not the child.
        var charmBody = world.CreateEntity();         // BodyB
        var charmCollider = world.CreateEntity();
        charmCollider.Set(new TransformComponent());
        charmCollider.SetParent(charmBody);

        world.Publish(new CollisionMessage(player, charmCollider, player, charmBody,
            Vector2.Zero, Vector2.UnitX, 0f, 0f, -1, CollisionType.Collectible));

        Assert.False(charmBody.IsAlive);   // the whole body was disposed (BodyB)
        Assert.True(charmCollider.IsAlive); // NOT the collider child (no HierarchySystem here to cascade)
        Assert.Equal(1, runnerState.Score); // scored on BodyA
    }

    [Fact]
    public void RunnerCollisionHandler_Damage_GameOverOnBodyA_DisposesTheWholeBodyB()
    {
        using var world = new World();
        using var sys = new RunnerCollisionHandlerSystem(world);

        var player = world.CreateEntity();
        var runnerState = new RunnerState();
        player.Set(runnerState);

        var obstacleBody = world.CreateEntity();
        var obstacleCollider = world.CreateEntity();
        obstacleCollider.Set(new TransformComponent());
        obstacleCollider.SetParent(obstacleBody);

        world.Publish(new CollisionMessage(player, obstacleCollider, player, obstacleBody,
            Vector2.Zero, Vector2.UnitX, 0f, 0f, -1, CollisionType.Damage));

        Assert.False(obstacleBody.IsAlive);
        Assert.True(runnerState.IsGameOver); // BodyA state flipped
    }

    // ─── End-to-end: the REAL pipeline chain (detection + GameCollisionHelper → ZoneDialogueTrigger) ──

    /// <summary>The full shipping dialogue-trigger chain, exactly as <c>LoadLevelExampleGameScreen</c>
    /// composes it: the REAL <see cref="TransformCollisionDetectionSystem{T}"/> classifying through the
    /// REAL <see cref="GameCollisionHelper.Create"/>, the REAL physical resolver, and the REAL
    /// <see cref="ZoneDialogueTriggerSystem"/> — over the CE entity shapes (a "Player" BODY with a
    /// collider CHILD walks into a standalone "Zone" collider entity). The zone's identity rides the
    /// collider entity, the classifier maps ("Player","Zone") → Dialogue, the trigger system reads
    /// ColliderB and publishes <see cref="DialogueStartMessage"/> — and because Dialogue is not Physics,
    /// the resolver ignores it: the zone SENSES without BLOCKING (the player sails through).</summary>
    [Fact]
    public void RealPipeline_PlayerBodyWithColliderChild_EntersZone_DialogueFires_AndZoneDoesNotBlock()
    {
        using var world = new World();
        using var runner = new DefaultEcs.Threading.DefaultParallelRunner(1);

        // The shipping composition (LoadLevelExampleGameScreen): detection classifies via
        // GameCollisionHelper; the physical resolver consumes Physics only; the zone trigger reads
        // Dialogue. Detection FIRST so its component-added subscription auto-tags the colliders below.
        var detect = new TransformCollisionDetectionSystem<CollisionMessage>(world, GameCollisionHelper.Create);
        var resolve = new TransformPhysicalCollisionResolutionSystem(world);
        using var zoneTrigger = new ZoneDialogueTriggerSystem(world);
        var velocity = new MonoDreams.System.Physics.TransformVelocitySystem(world, runner);
        var commit = new MonoDreams.System.TransformCommitSystem(world, runner);

        // Player: identity + velocity on the BODY; the collider is a CHILD entity (PlayerEntityFactory's shape).
        var player = world.CreateEntity();
        player.Set(new EntityInfoComponent("Player"));
        player.Set(new TransformComponent(new Vector2(0, 0)));
        player.Set(new MonoDreams.Component.Physics.VelocityComponent(new Vector2(20, 0)));
        var playerCollider = world.CreateEntity();
        playerCollider.Set(new TransformComponent(Vector2.Zero));
        playerCollider.Set(new BoxColliderComponent(new Vector2(16, 16)));
        playerCollider.SetParent(player);

        // The dialogue zone: a STANDALONE collider entity (the trigger palette's output) — identity +
        // zone component ON the collider entity, passive (senses, never initiates).
        var zone = world.CreateEntity();
        zone.Set(new EntityInfoComponent("Zone", "BoldoZone"));
        // oneTimeOnly so the multi-frame overlap fires exactly once (also proving that guard here).
        zone.Set(new DialogueZoneComponent("Boldo_Start", oneTimeOnly: true, autoStart: true));
        zone.Set(new TransformComponent(new Vector2(80, 0)));
        zone.Set(new BoxColliderComponent(new Vector2(48, 48), passive: true));

        var starts = new List<DialogueStartMessage>();
        world.Subscribe((in DialogueStartMessage m) => starts.Add(m));

        var play = new GameState(new GameTime(global::System.TimeSpan.Zero, global::System.TimeSpan.FromSeconds(1)))
            { RunMode = RunMode.Play };
        for (var i = 0; i < 10; i++)
        {
            velocity.Update(play);
            detect.Update(play);
            resolve.Update(play);
            commit.Update(play);
        }

        // The dialogue fired FROM the zone collider entity with its yarn node …
        Assert.Single(starts);
        Assert.Equal(zone, starts[0].DialogueEntity);
        Assert.Equal("Boldo_Start", starts[0].StartNode);
        Assert.True(zone.Get<DialogueZoneComponent>().HasBeenTriggered);
        // … and the zone did NOT block (Dialogue ≠ Physics, so the resolver ignored it): the player
        // sailed through and past the zone (zone right edge = 80+24 = 104).
        Assert.True(player.Get<TransformComponent>().Position.X > 104,
            $"the zone must sense, not block; player X={player.Get<TransformComponent>().Position.X}");
        // The collider child never drifted inside its parent (pre-mortem #1, on the shipping chain).
        Assert.Equal(Vector2.Zero, playerCollider.Get<TransformComponent>().Position);
    }

    // ─── NPCInteractionSystem: the proximity consumer resolves the collider CHILD ────────────────

    [Fact]
    public void NpcInteraction_ProximityViaColliderChild_ShowsTheIcon()
    {
        using var world = new World();

        // Detection FIRST so its component-added subscription auto-tags (ColliderTagComponent) the
        // collider children below — NPCInteractionSystem's child-collider set queries that tag.
        _ = new TransformCollisionDetectionSystem<CollisionMessage>(world,
            (ca, cb, ba, bb, cp, cn, ct, pd, l) => new CollisionMessage(ca, cb, ba, bb, cp, cn, ct, pd, l));

        // Player: a PlayerState body with a collider CHILD (no collider on the player itself).
        var player = world.CreateEntity();
        player.Set(new PlayerState());
        player.Set(new TransformComponent(new Vector2(100, 100)));
        var playerCollider = world.CreateEntity();
        playerCollider.Set(new TransformComponent(Vector2.Zero));
        playerCollider.Set(new BoxColliderComponent(new Vector2(40, 40)));
        playerCollider.SetParent(player);

        // Zone: a DialogueZone + NPCInteractionIcon body, its collider also a CHILD, overlapping.
        var icon = world.CreateEntity();
        var zone = world.CreateEntity();
        zone.Set(new DialogueZoneComponent("npc_talk", autoStart: false));
        zone.Set(new NPCInteractionIcon { IconEntity = icon });
        zone.Set(new TransformComponent(new Vector2(100, 100)));
        var zoneCollider = world.CreateEntity();
        zoneCollider.Set(new TransformComponent(Vector2.Zero));
        zoneCollider.Set(new BoxColliderComponent(new Vector2(48, 48), passive: true));
        zoneCollider.SetParent(zone);

        using var npc = new NPCInteractionSystem(world);
        npc.Update(Play());

        // The icon became visible — proving the system resolved the collider off the CHILD of BOTH the
        // player and the zone (the colliders-as-entities proximity path), not off the entities directly.
        Assert.True(icon.Has<VisibleComponent>());
    }
}
