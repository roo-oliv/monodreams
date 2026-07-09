using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DefaultEcs;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Physics;
using MonoDreams.Draw;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Serialization;
using MonoDreams.Extension;
using MonoDreams.LevelEditor.Assets;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.EntityFactory;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Message;
using MonoDreams.Platform;
using MonoDreams.State;
using MonoDreams.System;
using MonoDreams.System.Collision;
using MonoDreams.System.Physics;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// THE PREFAB PHASE ACCEPTANCE WALKTHROUGH (wave PF-E — the prefab-era sibling of the walkable-island
/// milestone). An end-to-end, in-process story over the REAL prefab core (PF-C: <see cref="PrefabWriter"/>,
/// <see cref="PrefabExpander"/>, <see cref="PrefabData"/>, <see cref="PrefabFileSource"/>,
/// <see cref="PrefabPropagation"/>, <see cref="PrefabFactory"/>), the DevTools Inspector commands
/// (PF-A: <see cref="AddComponentCommand"/>/<see cref="RemoveComponentCommand"/>/<see cref="MemberEditCommand"/>),
/// and the prefab UX commands (PF-D: <see cref="CreateInstanceCommand"/>/<see cref="UnpackPrefabCommand"/>/
/// the create-from-selection composite) — driven exactly as the <c>EditorOverlay</c> prefab ops drive them,
/// but headless (no GraphicsDevice / no chrome, per the in-process integration precedent; the visual shell is
/// exercised by the spawned-process overlay tests).
///
/// <para>It builds the user's actual targets — an <b>NPC prefab</b> (sprite + Passive collider + a
/// <c>DialogueZoneComponent</c> added through the Inspector), a <b>dialogue-zone prefab</b> assembled from
/// empty (trigger collider + zone component), and a <b>Player prefab</b> (sprite + RigidBody + PlayerState +
/// CameraFollowTarget) — then PLACES four linked instances, OVERRIDES one NPC's dialogue node, SAVES the
/// scene (asserting the compact instance entries + zero serialized instance children), RE-EDITS the NPC
/// prefab and verifies PROPAGATION on the scene's restore (the override survives), proves the SAVE→LOAD→SAVE
/// BYTE FIXED POINT with instances present, then BOOTS the saved scene through the native-first reader with
/// the prefab source and PLAYS (the player's physics live, the dialogue zone's trigger collider fires a
/// sensor collision, a Restart-equivalent reload returns the authored state).</para>
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class PrefabMilestoneTests
{
    private const string SceneFile = "prefab-island.scene.json";

    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    // Time = 1s so a velocity of v moves v units per stepped frame (mirrors IslandMilestoneTests).
    private static GameState Play() =>
        new(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1))) { RunMode = RunMode.Play };

    private static void WithPlatform(InMemoryPlatform fake, Action body)
    {
        var previous = PlatformServices.Current;
        try { PlatformServices.Current = fake; body(); }
        finally { PlatformServices.Current = previous; }
    }

    /// <summary>Engine + game serializers — the DialogueZone / PlayerState the walkthrough uses are game
    /// components, so the registry must carry both halves (the "Game components round-trip" premise).</summary>
    private static ComponentSerializerRegistry NewRegistry()
    {
        var r = new ComponentSerializerRegistry();
        r.RegisterEngineComponents();
        r.RegisterGameComponents();
        return r;
    }

    // ─── The prefab workshop: the real writer/source/expander + the overlay's prefab ops, headless ──────

    /// <summary>Encapsulates the real prefab machinery over an <see cref="InMemoryPlatform"/> and a resolved
    /// project root, mirroring the <c>EditorOverlay</c> prefab ops (Save-Prefab, Create-Empty,
    /// Create-from-Selection, open-context) minus the graphics shell. Prefabs are written to the fake as
    /// real <c>.mdprefab</c> bytes and resolved back source-first through the REAL
    /// <see cref="PrefabFileSource"/> — so the boot step reads the same files a shipped game would.</summary>
    private sealed class PrefabWorkshop
    {
        public const string ProjectRoot = "/proj";

        public SceneSerializer Serializer { get; }
        public Func<string, PrefabData?> Source { get; }
        public PrefabExpander Expander { get; }

        public PrefabWorkshop(InMemoryPlatform fake)
        {
            Serializer = new SceneSerializer(NewRegistry());

            // A resolved project context anchored at ProjectRoot (a manifest at <root>/game.mdproj) so the
            // real PrefabFileSource reads the written prefabs source-first (the in-editor resolution path).
            fake.Files[Path.Combine(ProjectRoot, GameProject.FileName)] = "{}";
            var project = EditorProjectContext.Resolve(
                baseDirectory: ProjectRoot,
                getEnvironmentVariable: n => n == EditorProjectContext.ProjectRootVariable ? ProjectRoot : null,
                fileExists: fake.FileExists,
                readAllText: fake.ReadAllText);
            Assert.True(project.Resolved);
            Assert.Equal(ProjectRoot, project.ProjectRoot);

            Source = new PrefabFileSource(contentRoot: "Content", projectContext: project).Resolve;
            Expander = new PrefabExpander(Serializer, Source, loadTexture: _ => null);
        }

        public string PrefabPath(string id) =>
            Path.Combine(ProjectRoot, MgcbLevelBundle.PrefabsDirectoryName, id + PrefabWriter.PrefabFileExtension);

        public PrefabData? Resolve(string id) => Source(id);

        /// <summary>Writes a validated, normalized <c>.mdprefab</c> from a prefab-context world (the writer
        /// half of <c>EditorOverlay.SavePrefabCurrent</c>: one-root + origin-normalize + no-camera +
        /// cycle-refuse, then canonical write to the source tree).</summary>
        public string SavePrefab(World world, string id)
        {
            var scene = new PrefabWriter(new SceneWriter(Serializer, Source)).BuildPrefab(world, id, Source);
            var saved = new SceneWriter(Serializer, Source).Save(scene, PrefabPath(id));
            Assert.Equal(PrefabPath(id), saved);
            return saved!;
        }

        /// <summary>Create Empty Prefab (<c>EditorOverlay.CreateEmptyPrefab</c>): a minimal one-root
        /// <c>.mdprefab</c> — one empty root at origin — to assemble from scratch.</summary>
        public void CreateEmptyPrefab(string id)
        {
            using var tmp = new World();
            var root = tmp.CreateEntity();
            root.Set(new SceneObjectComponent());
            root.Set(new TransformComponent(Vector2.Zero));
            SavePrefab(tmp, id);
        }

        /// <summary>Opens a prefab into a fresh "prefab-context" world (the reader loads the prefab's content;
        /// its root is re-tagged a scene object as the reader's re-tag would). Returns the instance-editable
        /// root.</summary>
        public Entity OpenPrefabContext(World world, string id)
        {
            var data = Resolve(id) ?? throw new InvalidOperationException($"no prefab '{id}' resolved");
            var created = Serializer.Deserialize(world, data.Scene);
            var root = created[data.RootIndex];
            root.Set(new SceneObjectComponent()); // the prefab context's single root (the reader re-tags this)
            return root;
        }

        /// <summary>Create Prefab from Selection (<c>EditorOverlay.CreatePrefabFromSelection</c>): capture the
        /// single-root selection's subtree into an origin-normalized <c>.mdprefab</c>, then replace the
        /// selection with a linked instance preserving world position — ONE undoable composite (Delete +
        /// CreateInstance). Returns the created instance root.</summary>
        public Entity CreatePrefabFromSelection(World world, Entity root, string id, EditorHistory history)
        {
            var captured = Serializer.Serialize(EntitySubgraph.Collect(world, root));
            using (var tmp = new World())
            {
                var created = Serializer.Deserialize(tmp, captured);
                created[0].Set(new SceneObjectComponent());
                SavePrefab(tmp, id); // the file is written BEFORE the composite (durable; undo never touches it)
            }

            var worldPos = root.Has<TransformComponent>() ? root.Get<TransformComponent>().Position : Vector2.Zero;
            var delete = new DeleteEntityCommand(world, root, Serializer);
            var create = new CreateInstanceCommand(Expander, id, worldPos);
            history.Push(new CompositeCommand(new List<IEditorCommand> { delete, create }));
            return create.Root;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PrefabWalkthrough_NpcDialogueZonePlayer_BuildPlaceOverridePropagateBootPlay()
    {
        var fake = new InMemoryPlatform();
        WithPlatform(fake, () =>
        {
            var shop = new PrefabWorkshop(fake);
            var serializer = shop.Serializer;
            var propBand = new PaletteBand("Props", LayerDepth: 0.5f, YSorted: true);
            var npcFootprintV1 = new Rectangle(-20, -20, 40, 40);
            var npcFootprintV2 = new Rectangle(-30, -30, 60, 60); // the "re-edit": a larger footprint

            // ============ (a) Build an NPC prefab from a scene selection ============
            // Assemble in the scene: a sprite via the placement path (a file: AssetKey) + a child sprite,
            // then a Passive collider + a DialogueZone added through the Inspector's AddComponentCommand.
            using var authorWorld = new World();
            var authorHistory = new EditorHistory(authorWorld);

            var npcAsset = Whole("Island/npc/boldo.png", "boldo");
            var npcRoot = SpritePropFactory.Create(authorWorld, npcAsset, propBand, new Vector2(300, 300), texture: null);
            npcRoot.Set(new SceneObjectComponent()); // the placement command tags the root
            var npcHat = SpritePropFactory.Create(authorWorld,
                Whole("Island/npc/hat.png", "hat"), propBand, new Vector2(0, -16), texture: null);
            npcHat.SetParent(npcRoot); // a prefab-owned child (proves children never serialize in the scene)

            authorHistory.Push(new AddComponentCommand(npcRoot, typeof(BoxColliderComponent),
                new BoxColliderComponent(npcFootprintV1, passive: true)));
            authorHistory.Push(new AddComponentCommand(npcRoot, typeof(DialogueZoneComponent),
                new DialogueZoneComponent("npc_talk", npcName: "Boldo")));
            Assert.True(npcRoot.Get<BoxColliderComponent>().Passive);
            Assert.True(npcRoot.Has<DialogueZoneComponent>());

            npcRoot.Set(new SelectedComponent());
            var npcInstance0 = shop.CreatePrefabFromSelection(authorWorld, npcRoot, "npc", authorHistory);

            // The selection is replaced by a linked instance; the .mdprefab exists, one-root, origin-normalized.
            Assert.False(npcRoot.IsAlive);                                    // consumed by the composite delete
            Assert.True(npcInstance0.Has<PrefabInstanceComponent>());
            Assert.Equal("npc", npcInstance0.Get<PrefabInstanceComponent>().PrefabId);
            Assert.True(npcInstance0.Has<SceneObjectComponent>());
            Assert.NotEqual(default, ChildOf(authorWorld, npcInstance0));     // its prefab-owned child came back
            Assert.True(fake.Files.ContainsKey(shop.PrefabPath("npc")));      // the file exists
            var npcPrefab = shop.Resolve("npc")!;
            Assert.Equal(2, npcPrefab.Scene.Entities.Count);                  // root + child
            Assert.Equal(1, TopLevelCount(npcPrefab.Scene));                  // exactly ONE root
            Assert.Equal((0f, 0f), RootPos(npcPrefab));                       // origin-normalized
            Assert.Null(npcPrefab.Scene.Camera);                             // a prefab is a class — no camera
            AssertCanonicalFile(fake, shop.PrefabPath("npc"));               // canonical byte fixed point

            // ============ (b) Build a dialogue-zone prefab from EMPTY ============
            shop.CreateEmptyPrefab("dialogue-zone");
            using (var dzCtx = new World())
            {
                var dzHistory = new EditorHistory(dzCtx);
                var root = shop.OpenPrefabContext(dzCtx, "dialogue-zone");
                // Add a trigger collider (Passive) + its identity + the zone component — all via commands.
                dzHistory.Push(new AddComponentCommand(root, typeof(EntityInfoComponent),
                    new EntityInfoComponent("talkzone", "dz_talk_01")));
                dzHistory.Push(new AddComponentCommand(root, typeof(BoxColliderComponent),
                    new BoxColliderComponent(new Rectangle(-24, -24, 48, 48), passive: true)));
                dzHistory.Push(new AddComponentCommand(root, typeof(DialogueZoneComponent),
                    new DialogueZoneComponent("zone_talk", npcName: "Zone")));
                shop.SavePrefab(dzCtx, "dialogue-zone");
            }
            var dzPrefab = shop.Resolve("dialogue-zone")!;
            Assert.Single(dzPrefab.Scene.Entities);
            Assert.Null(dzPrefab.Scene.Camera);                              // no camera
            Assert.True(dzPrefab.Root.Components.ContainsKey(EngineComponentSerializers.BoxColliderKey));
            Assert.True(dzPrefab.Root.Components.ContainsKey(GameComponentSerializers.DialogueZoneKey));

            // ============ (c) Build a Player prefab ============
            using (var playerCtx = new World())
            {
                var playerHistory = new EditorHistory(playerCtx);
                var playerAsset = Whole("Island/player/pete.png", "pete");
                var sprite = SpritePropFactory.Create(playerCtx, playerAsset, propBand, Vector2.Zero, texture: null);
                sprite.Set(new SceneObjectComponent());
                playerHistory.Push(new AddComponentCommand(sprite, typeof(RigidBodyComponent), new RigidBodyComponent()));
                playerHistory.Push(new AddComponentCommand(sprite, typeof(PlayerState), new PlayerState()));
                // READ (never write) the guarded camera component; instantiate its default.
                playerHistory.Push(new AddComponentCommand(sprite, typeof(CameraFollowTargetComponent),
                    new CameraFollowTargetComponent()));
                shop.SavePrefab(playerCtx, "player");
            }
            var playerPrefab = shop.Resolve("player")!;
            Assert.Null(playerPrefab.Scene.Camera);
            Assert.True(playerPrefab.Root.Components.ContainsKey(EngineComponentSerializers.RigidBodyKey));
            Assert.True(playerPrefab.Root.Components.ContainsKey(GameComponentSerializers.PlayerStateKey));
            Assert.True(playerPrefab.Root.Components.ContainsKey(EngineComponentSerializers.CameraFollowTargetKey));

            // ============ (d) Place instances into the scene + override one NPC ============
            // The scene already holds npc #1 (from create-from-selection). Place npc #2 + the zone + the
            // player through the CreateInstanceCommand (the prefab:place path) at distinct positions.
            var npc2Cmd = new CreateInstanceCommand(shop.Expander, "npc", new Vector2(100, 100));
            authorHistory.Push(npc2Cmd);
            var npcInstance1 = npc2Cmd.Root;
            var dzCmd = new CreateInstanceCommand(shop.Expander, "dialogue-zone", new Vector2(100, -100));
            authorHistory.Push(dzCmd);
            var playerCmd = new CreateInstanceCommand(shop.Expander, "player", new Vector2(0, -100));
            authorHistory.Push(playerCmd);

            Assert.Equal(4, InstanceRootCount(authorWorld)); // 2 npc + 1 dialogue-zone + 1 player

            // Override ONE NPC's dialogue node through the Inspector's MemberEditCommand (a whole-component
            // override arises from the byte-diff at save).
            var overrideCmd = MemberEditCommand.FromCurrent(npcInstance1, typeof(DialogueZoneComponent),
                nameof(DialogueZoneComponent.YarnNodeName), "npc_talk_alt");
            Assert.NotNull(overrideCmd);
            authorHistory.Push(overrideCmd!);
            Assert.Equal("npc_talk_alt", npcInstance1.Get<DialogueZoneComponent>().YarnNodeName);
            Assert.Equal("npc_talk", npcInstance0.Get<DialogueZoneComponent>().YarnNodeName); // the other inherits

            // ============ (e) Save the scene ============
            new SceneWriter(serializer, shop.Source).Save(authorWorld, SceneFile, new GameCamera(800, 600), layers: null);
            var saved = CanonicalJson.Deserialize<SceneData>(fake.Files[SceneFile])!;

            Assert.Equal(4, saved.Entities.Count);                            // 4 compact entries, ZERO children
            Assert.All(saved.Entities, e => Assert.NotNull(e.Prefab));        // every entry is a linked instance
            Assert.All(saved.Entities, e => Assert.Null(e.Parent));           // no instance child serialized
            Assert.All(saved.Entities, e =>
                Assert.True(e.Components.ContainsKey(EngineComponentSerializers.TransformKey))); // Transform always kept

            var npcEntries = saved.Entities.Where(e => e.Prefab == "npc").ToList();
            Assert.Equal(2, npcEntries.Count);
            // ONLY the overridden NPC carries the DialogueZone override; the other inherits everything.
            Assert.Equal(1, npcEntries.Count(e => e.Components.ContainsKey(GameComponentSerializers.DialogueZoneKey)));
            var verbatimNpc = npcEntries.Single(e => !e.Components.ContainsKey(GameComponentSerializers.DialogueZoneKey));
            Assert.False(verbatimNpc.Components.ContainsKey(EngineComponentSerializers.SpriteInfoKey));  // inherited → omitted
            Assert.False(verbatimNpc.Components.ContainsKey(EngineComponentSerializers.BoxColliderKey)); // inherited → omitted

            // ============ (f) Re-edit the NPC prefab → propagation on the scene's restore ============
            using (var prefabCtx = new World())
            {
                var prefabHistory = new EditorHistory(prefabCtx);
                var root = shop.OpenPrefabContext(prefabCtx, "npc");
                Assert.Equal(npcFootprintV1, root.Get<BoxColliderComponent>().Bounds);
                // Change the collider footprint through the Inspector (remove + add — a whole-component edit).
                prefabHistory.Push(RemoveComponentCommand.Create(root, typeof(BoxColliderComponent))!);
                prefabHistory.Push(new AddComponentCommand(root, typeof(BoxColliderComponent),
                    new BoxColliderComponent(npcFootprintV2, passive: true)));
                shop.SavePrefab(prefabCtx, "npc"); // writes npc v2 (overwrites the file)
            }

            // "On the scene context's restore": reload the saved (compact) scene — both NPCs re-expand from v2.
            using var restoredWorld = new World();
            var restoredExpander = new PrefabExpander(serializer, shop.Source, loadTexture: _ => null);
            using (NewReader(restoredWorld, serializer, restoredExpander))
                restoredWorld.Publish(new LoadSceneRequest(SceneFile, fromContent: false));

            var restoredNpcs = InstanceRoots(restoredWorld, "npc");
            Assert.Equal(2, restoredNpcs.Count);
            Assert.All(restoredNpcs, n => Assert.Equal(npcFootprintV2, n.Get<BoxColliderComponent>().Bounds)); // propagated
            // The overridden yarn node SURVIVES on the overridden instance; the other still inherits.
            Assert.Equal(1, restoredNpcs.Count(n => n.Get<DialogueZoneComponent>().YarnNodeName == "npc_talk_alt"));
            Assert.Equal(1, restoredNpcs.Count(n => n.Get<DialogueZoneComponent>().YarnNodeName == "npc_talk"));

            // ============ (g) Byte-stable round-trip (instances present) ============
            var g1 = new SceneWriter(serializer, shop.Source).BuildScene(restoredWorld);
            var g1Json = CanonicalJson.Serialize(g1);
            Assert.Equal(4, g1.Entities.Count);
            Assert.All(g1.Entities, e => Assert.NotNull(e.Prefab));

            using var rtWorld = new World();
            var rtExpander = new PrefabExpander(serializer, shop.Source, loadTexture: _ => null);
            using (NewReader(rtWorld, serializer, rtExpander))
                rtWorld.Publish(new LoadSceneRequest(g1)); // in-memory restore through the same reader
            var g2Json = CanonicalJson.Serialize(new SceneWriter(serializer, shop.Source).BuildScene(rtWorld));
            Assert.Equal(g1Json, g2Json); // load → save reproduces the exact bytes

            // ============ (h) Boot the saved scene + Play ============
            using var bootWorld = new World();
            using var runner = new DefaultParallelRunner(1);
            var bootSerializer = new SceneSerializer(NewRegistry()); // the shipped game's standalone registry
            var bootExpander = new PrefabExpander(bootSerializer, shop.Source, loadTexture: _ => null);
            var detect = new TransformCollisionDetectionSystem<CollisionMessage>(bootWorld, MilestoneCollision.Create);
            using (NewReader(bootWorld, bootSerializer, bootExpander))
                bootWorld.Publish(new LoadSceneRequest(SceneFile, fromContent: false));

            // The player's physics components live after boot.
            var playerRoot = InstanceRoots(bootWorld, "player").Single();
            Assert.True(playerRoot.Has<RigidBodyComponent>());
            Assert.True(playerRoot.Has<PlayerState>());
            Assert.True(playerRoot.Has<CameraFollowTargetComponent>());

            // The dialogue zone's trigger collider participates.
            var dzRoot = InstanceRoots(bootWorld, "dialogue-zone").Single();
            Assert.True(dzRoot.Get<BoxColliderComponent>().Passive);
            Assert.True(dzRoot.Has<DialogueZoneComponent>());
            var dzIdentity = Identity(dzRoot);
            Assert.Equal(("talkzone", "dz_talk_01"), dzIdentity);

            // Drive the player (a mover + collider — a game movement system's job) into the zone; assert a
            // sensor collision fires carrying the zone's identity (the passive trigger participates).
            playerRoot.Get<TransformComponent>().Position = new Vector2(60, -100);
            playerRoot.Get<TransformComponent>().CommitPosition();
            playerRoot.Set(new BoxColliderComponent(new Rectangle(-8, -8, 16, 16))); // non-passive mover
            playerRoot.Set(new VelocityComponent(new Vector2(15, 0)));

            var hits = new List<CollisionMessage>();
            bootWorld.Subscribe((in CollisionMessage m) => hits.Add(m));
            var velocity = new TransformVelocitySystem(bootWorld, runner);
            var resolve = new TransformPhysicalCollisionResolutionSystem(bootWorld);
            var commit = new TransformCommitSystem(bootWorld, runner);
            var play = Play();
            for (var i = 0; i < 10; i++) { velocity.Update(play); detect.Update(play); resolve.Update(play); commit.Update(play); }
            Assert.Contains(hits, m => Identity(m.CollidingEntity) == dzIdentity);

            // ============ Restart-equivalent: reload returns the authored state ============
            using var restartWorld = new World();
            var restartExpander = new PrefabExpander(bootSerializer, shop.Source, loadTexture: _ => null);
            using (NewReader(restartWorld, bootSerializer, restartExpander))
                restartWorld.Publish(new LoadSceneRequest(SceneFile, fromContent: false));
            Assert.Equal(4, InstanceRootCount(restartWorld));
            Assert.Equal(2, InstanceRoots(restartWorld, "npc").Count);
            Assert.Single(InstanceRoots(restartWorld, "player"));
            Assert.Single(InstanceRoots(restartWorld, "dialogue-zone"));
            // The authored override returns exactly (one alt, one inherited).
            Assert.Equal(1, InstanceRoots(restartWorld, "npc")
                .Count(n => n.Get<DialogueZoneComponent>().YarnNodeName == "npc_talk_alt"));
        });
    }

    // ─── Unpack: an unpacked instance serializes EXPANDED (item 2) ──────────────────────────────────────

    [Fact]
    public void UnpackedNpcInstance_SerializesExpanded_NotAsACompactPrefabEntry()
    {
        var fake = new InMemoryPlatform();
        WithPlatform(fake, () =>
        {
            var shop = new PrefabWorkshop(fake);
            SeedNpcPrefab(shop, new Rectangle(-20, -20, 40, 40));

            using var world = new World();
            var history = new EditorHistory(world);
            var place = new CreateInstanceCommand(shop.Expander, "npc", new Vector2(120, 80));
            history.Push(place);
            var instance = place.Root;

            // Before unpack: the whole instance serializes as ONE compact prefab entry (child excluded).
            var compact = new SceneWriter(shop.Serializer, shop.Source).BuildScene(world);
            Assert.Single(compact.Entities);
            Assert.Equal("npc", compact.Entities[0].Prefab);

            // Unpack → drop the marker → ordinary scene entities.
            history.Push(new UnpackPrefabCommand(instance));
            Assert.False(instance.Has<PrefabInstanceComponent>());

            // After unpack: it serializes EXPANDED (root + child as full entities, no prefab field).
            var expanded = new SceneWriter(shop.Serializer, shop.Source).BuildScene(world);
            Assert.Equal(2, expanded.Entities.Count);                       // the child is closure-serialized again
            Assert.All(expanded.Entities, e => Assert.Null(e.Prefab));      // no compact reference remains
            Assert.Contains(expanded.Entities,
                e => e.Components.ContainsKey(EngineComponentSerializers.SpriteInfoKey)); // the full sprite is written

            // Undo re-links (restores the compact serialization).
            history.Undo();
            Assert.True(instance.Has<PrefabInstanceComponent>());
            Assert.Single(new SceneWriter(shop.Serializer, shop.Source).BuildScene(world).Entities);
        });
    }

    // ─── Factory: EntitySpawnRequest("prefab:npc") spawns an instance at runtime (item 2) ──────────────

    [Fact]
    public void PrefabFactory_SpawnsInstanceAtRuntime_ViaEntitySpawnRequestChannel()
    {
        var fake = new InMemoryPlatform();
        WithPlatform(fake, () =>
        {
            var shop = new PrefabWorkshop(fake);
            SeedNpcPrefab(shop, new Rectangle(-20, -20, 40, 40));

            using var world = new World();
            // Three placed instances (the authored scene) …
            var history = new EditorHistory(world);
            history.Push(new CreateInstanceCommand(shop.Expander, "npc", new Vector2(10, 10)));
            history.Push(new CreateInstanceCommand(shop.Expander, "npc", new Vector2(20, 20)));
            history.Push(new CreateInstanceCommand(shop.Expander, "npc", new Vector2(30, 30)));
            Assert.Equal(3, InstanceRootCount(world));

            // … then a FOURTH spawned at runtime through the code path the user asked for: the generic
            // PrefabFactory on the existing EntitySpawnRequest("prefab:<id>") channel (prefix dispatch).
            using var spawnSystem = new MonoDreams.System.EntitySpawn.EntitySpawnSystem(
                world, content: null, renderTargets: new Dictionary<RenderTargetID, RenderTarget2D>());
            spawnSystem.RegisterEntityFactoryPrefix(PrefabFactory.IdentifierPrefix, new PrefabFactory(shop.Expander));

            world.Publish(new EntitySpawnRequest("prefab:npc", new Vector2(500, 500)));

            Assert.Equal(4, InstanceRootCount(world)); // the fourth instance
            var spawned = InstanceRoots(world, "npc")
                .Single(r => r.Get<TransformComponent>().Position == new Vector2(500, 500));
            Assert.Equal("npc", spawned.Get<PrefabInstanceComponent>().PrefabId); // a linked instance …
            Assert.True(spawned.Has<SceneObjectComponent>());                     // … a first-class scene object …
            Assert.NotEqual(default, ChildOf(world, spawned));                    // … with its prefab-owned child
        });
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Builds + saves the "npc" prefab (a root sprite + a Passive collider + a DialogueZone + a
    /// child sprite) directly into the shop, for the sibling tests.</summary>
    private static void SeedNpcPrefab(PrefabWorkshop shop, Rectangle footprint)
    {
        using var w = new World();
        var band = new PaletteBand("Props", LayerDepth: 0.5f, YSorted: true);
        var root = SpritePropFactory.Create(w, Whole("Island/npc/boldo.png", "boldo"),
            band, new Vector2(200, 100), texture: null);
        root.Set(new SceneObjectComponent());
        root.Set(new BoxColliderComponent(footprint, passive: true));
        root.Set(new DialogueZoneComponent("npc_talk", npcName: "Boldo"));
        var child = SpritePropFactory.Create(w, Whole("Island/npc/hat.png", "hat"),
            band, new Vector2(0, -16), texture: null);
        child.SetParent(root);
        shop.SavePrefab(w, "npc");
    }

    /// <summary>A prefab-aware scene reader over a headless world (null content + null texture loaders —
    /// components + AssetKeys are asserted, not pixels). Centralizes the null-loader construction.</summary>
    private static SceneReaderSystem NewReader(World world, SceneSerializer serializer, PrefabExpander expander) =>
        new(world, serializer, content: null, loadTexture: _ => null, prefabExpander: expander);

    /// <summary>A whole-PNG catalog entry (the common placement-path case: a <c>file:</c> AssetKey, no
    /// sliced region).</summary>
    private static AssetCatalogEntry Whole(string path, string label) =>
        new(path, regionName: null, region: null, label: label, folder: "");

    private static int InstanceRootCount(World world)
    {
        using var set = world.GetEntities().With<PrefabInstanceComponent>().AsSet();
        return set.GetEntities().Length;
    }

    private static List<Entity> InstanceRoots(World world, string prefabId)
    {
        var list = new List<Entity>();
        using var set = world.GetEntities().With<PrefabInstanceComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.Get<PrefabInstanceComponent>().PrefabId == prefabId) list.Add(e);
        return list;
    }

    private static Entity ChildOf(World world, Entity parent)
    {
        using var set = world.GetEntities().With<ChildOfComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.Get<ChildOfComponent>().Parent.Equals(parent)) return e;
        return default;
    }

    private static (string, string) Identity(Entity e)
    {
        if (!e.IsAlive || !e.Has<EntityInfoComponent>()) return ("", "");
        var info = e.Get<EntityInfoComponent>();
        return (info.Type, info.Name);
    }

    private static int TopLevelCount(SceneData scene) => scene.Entities.Count(e => e.Parent == null);

    private static (float x, float y) RootPos(PrefabData prefab)
    {
        var t = prefab.Root.Components[EngineComponentSerializers.TransformKey];
        var pos = t.GetProperty("position");
        return (pos[0].GetSingle(), pos[1].GetSingle());
    }

    private static void AssertCanonicalFile(InMemoryPlatform fake, string path)
    {
        var bytes = fake.Files[path];
        var reparsed = CanonicalJson.Deserialize<SceneData>(bytes)!;
        Assert.Equal(bytes, CanonicalJson.Serialize(reparsed)); // the written file is a canonical fixed point
    }
}
