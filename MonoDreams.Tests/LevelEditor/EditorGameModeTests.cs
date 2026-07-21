using System;
using System.Collections.Generic;
using System.IO;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.UI;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the Game-mode sandbox SEMANTICS, now the Game tab consuming the <c>ViewportContextStack</c>
/// (PF-B; level-editor premise "The viewport context stack …"): spawning the Game tab snapshots the scene
/// in memory, Play/Pause/edit freely, and leaving it DISCARDS the sandbox by restoring the snapshot
/// <b>through the reader</b> (the ONE restore path — pre-mortem #2). The transport owns <see cref="RunMode"/>
/// and drives the stack (<see cref="ViewportContextKind"/> is the active-context kind); the snapshot is
/// taken BEFORE Play flips RunMode (pre-mortem #7); undo after leaving is a no-op (pre-mortem #3); Save is
/// blocked while the Game tab is active; Restart lands the Scene tab; a scene switch leaves the Game tab
/// first.
/// </summary>
public class EditorGameModeTests
{
    private static GameState Paused() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Playing() => new(new GameTime()) { RunMode = RunMode.Play };

    /// <summary>A resolved project context (env var → an in-memory manifest), mirroring ToolbarTests —
    /// so the Save guard sees a real project root and GameMode/None are the distinguishing causes.</summary>
    private static EditorProjectContext ResolvedContext()
    {
        const string root = "/proj";
        var manifestPath = Path.Combine(root, "Content", GameProject.FileName);
        var manifestJson = CanonicalJson.Serialize(new GameProject { StartScene = "island" });
        return EditorProjectContext.Resolve(
            baseDirectory: Path.Combine("/somewhere", "bin") + Path.DirectorySeparatorChar,
            getEnvironmentVariable: name => name == EditorProjectContext.ProjectRootVariable ? root : null,
            fileExists: p => p == manifestPath,
            readAllText: _ => manifestJson);
    }

    /// <summary>The full editor stack a Game-mode round-trip needs, wired EXACTLY as
    /// <c>EditorOverlay</c> wires the transport's Game-mode seams (so the test exercises the real
    /// composition, not a re-implementation). The reader is held alive to receive the in-memory
    /// restore <c>LoadSceneRequest</c>.</summary>
    private sealed class Stack : IDisposable
    {
        public readonly World World;
        public readonly GameCamera Camera;
        public readonly EditorCameraRig Rig;
        public readonly EditorHistory History;
        public readonly EditorTransport Transport;
        public readonly SceneReaderSystem Reader;
        public readonly List<string> RehydratedKeys = new();
        public int SnapshotCaptures;

        public Stack()
        {
            World = new World();
            var registry = new ComponentSerializerRegistry();
            registry.RegisterEngineComponents();
            var serializer = new SceneSerializer(registry);

            Camera = new GameCamera(800, 600);
            Rig = new EditorCameraRig(World, Camera);
            Reader = new SceneReaderSystem(World, serializer, content: null,
                loadTexture: _ => null,
                fileTextureLoader: key => { RehydratedKeys.Add(key); return null; },
                camera: Camera, applyCameraToRig: Rig.SyncFromScene);
            History = new EditorHistory(World);
            Transport = new EditorTransport(World, History) { Reload = () => { } };

            // The SAME seams the overlay wires (EditorOverlay ctor, UX2-F block).
            Transport.CaptureSnapshot = () =>
            {
                SnapshotCaptures++;
                return new SceneWriter(serializer).BuildScene(World, layers: null);
            };
            Transport.RestoreSnapshot = snapshot => World.Publish(new LoadSceneRequest(snapshot));
            Transport.CaptureView = () => new CameraViewSnapshot(Camera.Position, Camera.Zoom, Camera.Rotation);
            Transport.RestoreView = view =>
            {
                Camera.Position = view.Position;
                Camera.Zoom = view.Zoom;
                Camera.Rotation = view.Rotation;
            };
            Transport.SnapViewToRig = Rig.SnapViewToRig;
        }

        /// <summary>Creates one tagged sprite scene root at <paramref name="pos"/> (with an asset key so
        /// it round-trips through the reader's texture rehydration + DrawComponent restore).</summary>
        public Entity AddSpriteRoot(Vector2 pos, string assetKey = "Atlas/TX Tree")
        {
            var e = World.CreateEntity();
            e.Set(new SceneObjectComponent());
            e.Set(new EntityInfoComponent("Prop", "Tree"));
            e.Set(new TransformComponent(pos));
            e.Set(new SpriteInfoComponent
            {
                AssetKey = assetKey,
                Source = new Rectangle(0, 0, 16, 16),
                Size = new Vector2(16, 16),
                Color = Color.White,
                Target = RenderTargetID.Main,
                LayerDepth = 0.5f,
            });
            e.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main });
            return e;
        }

        /// <summary>The single live <see cref="SceneObjectComponent"/> root (re-queried after a restore
        /// re-creates the entity).</summary>
        public Entity TaggedRoot()
        {
            using var set = World.GetEntities().With<SceneObjectComponent>().AsSet();
            foreach (var e in set.GetEntities()) return e;
            return default;
        }

        public void Dispose() { Reader.Dispose(); World.Dispose(); }
    }

    // ─────────────── Enter/exit round-trip: position + undo + dirty + view all restored ───────────────

    [Fact]
    public void EnterGameMove_Exit_RestoresPositionExactly_UndoNoOp_DirtyAndViewRestored()
    {
        using var s = new Stack();
        s.AddSpriteRoot(new Vector2(10, 20));
        s.Camera.Position = new Vector2(5, 5); // the Scene-mode VIEW to return to

        s.Transport.EnterGameMode(Paused());
        Assert.Equal(ViewportContextKind.Game, s.Transport.ActiveContextKind);

        // Poke the entity in the sandbox (a direct mutation stands in for a gizmo drag).
        s.TaggedRoot().Get<TransformComponent>().Position = new Vector2(99, 99);

        s.Transport.ExitToSceneMode(Paused());

        // The sandbox edit vanished — the scene is back at its pre-entry position (a NEW entity, restored).
        Assert.Equal(ViewportContextKind.Scene, s.Transport.ActiveContextKind);
        Assert.Equal(new Vector2(10, 20), s.TaggedRoot().Get<TransformComponent>().Position);
        // Undo after exit is a no-op (pre-mortem #3): the restored entities have no live commands.
        Assert.Equal(0, s.History.Count);
        s.History.Undo(); // must not throw / must not move anything
        Assert.Equal(new Vector2(10, 20), s.TaggedRoot().Get<TransformComponent>().Position);
        // Dirty == the pre-entry value (clean here), and the VIEW is back where it was pre-entry.
        Assert.False(s.History.IsDirty);
        Assert.Equal(new Vector2(5, 5), s.Camera.Position);
    }

    [Fact]
    public void Exit_RestoresTheCapturedDirtyState_NotSandboxChurn()
    {
        using var s = new Stack();
        var root = s.AddSpriteRoot(new Vector2(1, 1));

        // Pre-entry: a real unsaved edit → dirty. Enter Game mode capturing that dirty state.
        s.History.Push(TransformEditCommand.FromCurrent(root, new Vector2(2, 2), 0f, Vector2.One, Vector2.Zero));
        Assert.True(s.History.IsDirty);
        s.Transport.EnterGameMode(Paused());

        // Sandbox churn (more edits) must not change what exit restores.
        s.History.Push(TransformEditCommand.FromCurrent(s.TaggedRoot(), new Vector2(3, 3), 0f, Vector2.One, Vector2.Zero));

        s.Transport.ExitToSceneMode(Paused());

        // The dirty gate sees the RESTORED (captured) dirty state — dirty, because it was dirty pre-entry —
        // while undo/redo are empty (a switch's dirty gate reads this, not sandbox churn; design §5 item 7).
        Assert.True(s.History.IsDirty);
        Assert.Equal(0, s.History.Count);
        Assert.Equal(0, s.History.RedoCount);
    }

    // ── TD: a code-built screen's Game-tab exit rebuilds the code UI (report-2 blank-screen fix) ──────

    /// <summary>
    /// The report-2 fix, engine-level and host-agnostic — it models the Examples menu / the Demos launcher /
    /// the physics demo Play → close-Game-tab story identically (every editor screen wires the SAME split
    /// seam). A code-built screen's UI is NOT <see cref="SceneObjectComponent"/>-tagged, so it is never
    /// snapshot-captured. Before TD the Game-tab exit swept it and restored only the (empty) snapshot → a
    /// blank screen. Now the stack runs the screen's <see cref="EditorTransport.RebuildCodeContent"/> BETWEEN
    /// the sweep and the reader restore, so the code UI comes back and any scene-owned content restores on top.
    /// </summary>
    [Fact]
    public void ExitGameTab_OnACodeBuiltScreen_RebuildsTheCodeUi_NotBlank_SceneRestoredOnTop()
    {
        using var s = new Stack();
        s.AddSpriteRoot(new Vector2(10, 20)); // scene-owned (snapshot-captured + reader-restored)

        // The screen's code-owned UI: untagged (never captured), swept on a tab switch. The screen's Load
        // built it once and registered RebuildCodeContent to re-run its builder.
        void BuildCodeUi() => s.World.CreateEntity().Set(new EntityInfoComponent("CodeButton"));
        BuildCodeUi();
        s.Transport.RebuildCodeContent = BuildCodeUi;
        Assert.Equal(1, CountInfo(s.World, "CodeButton"));

        s.Transport.EnterGameMode(Paused()); // keeps the live world — the code UI is still there
        Assert.Equal(1, CountInfo(s.World, "CodeButton"));

        s.Transport.ExitToSceneMode(Paused());

        // NOT blank: exactly one code button rebuilt, and the scene root restored at its authored position.
        Assert.Equal(1, CountInfo(s.World, "CodeButton"));
        Assert.Equal(new Vector2(10, 20), s.TaggedRoot().Get<TransformComponent>().Position);
    }

    /// <summary>The contrast that pins the seam as the fix: with no <c>RebuildCodeContent</c> (the pre-TD
    /// behaviour) a code-built screen's Game-tab exit sweeps the code UI and restores only the empty
    /// snapshot — the blank screen report 2 described.</summary>
    [Fact]
    public void ExitGameTab_WithoutRebuildCodeContent_LeavesTheCodeUiSwept_TheReport2Blank()
    {
        using var s = new Stack();
        s.World.CreateEntity().Set(new EntityInfoComponent("CodeButton"));
        s.Transport.RebuildCodeContent = null; // pre-TD (clears the Stack's {Reload=()=>{}} default)

        s.Transport.EnterGameMode(Paused());
        s.Transport.ExitToSceneMode(Paused());

        Assert.Equal(0, CountInfo(s.World, "CodeButton")); // swept, never rebuilt → blank
    }

    private static int CountInfo(World world, string type)
    {
        var n = 0;
        using var set = world.GetEntities().With<EntityInfoComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.Get<EntityInfoComponent>().Type == type) n++;
        return n;
    }

    // ─────────────── Order: Play in Scene mode snapshots BEFORE RunMode flips (pre-mortem #7) ─────────

    [Fact]
    public void PlayInSceneMode_SnapshotsBeforeRunModeFlipsToPlay_AndAutoEntersGame()
    {
        using var s = new Stack();
        s.AddSpriteRoot(new Vector2(10, 20));
        var state = Paused();

        var observedRunModeAtCapture = RunMode.Play;
        var baseCapture = s.Transport.CaptureSnapshot!;
        s.Transport.CaptureSnapshot = () => { observedRunModeAtCapture = state.RunMode; return baseCapture(); };

        s.Transport.Play(state);

        Assert.Equal(RunMode.Edit, observedRunModeAtCapture); // snapshot taken BEFORE the flip
        Assert.Equal(RunMode.Play, state.RunMode);            // then Play
        Assert.Equal(ViewportContextKind.Game, s.Transport.ActiveContextKind); // auto-entered Game
    }

    [Fact]
    public void PlayInGameMode_DoesNotReSnapshot_OneSnapshotPerSession()
    {
        using var s = new Stack();
        s.AddSpriteRoot(new Vector2(10, 20));
        var state = Paused();

        s.Transport.EnterGameMode(state); // via the toggle → snapshot #1
        Assert.Equal(1, s.SnapshotCaptures);

        s.Transport.Play(state);   // already in Game — no re-snapshot
        s.Transport.Pause(state);
        s.Transport.Play(state);

        Assert.Equal(1, s.SnapshotCaptures);
    }

    // ─────────────── Save blocked in Game mode, with a distinguishable reason ─────────────────────────

    [Fact]
    public void SaveBlock_GameMode_IsDistinguishable_PlayingWins_SceneModeSavesAgain()
    {
        var resolved = ResolvedContext();

        // Paused + Game mode + resolved → blocked specifically as GameMode.
        Assert.Equal(SaveBlockReason.GameMode,
            EditorOverlay.SaveBlock(Paused(), resolved, ViewportContextKind.Game));
        // Playing takes precedence over GameMode (the existing "Playing first" rule holds).
        Assert.Equal(SaveBlockReason.Playing,
            EditorOverlay.SaveBlock(Playing(), resolved, ViewportContextKind.Game));
        // Back in Scene mode (after exit) Save works again.
        Assert.Equal(SaveBlockReason.None,
            EditorOverlay.SaveBlock(Paused(), resolved, ViewportContextKind.Scene));
        // The default view-mode overload is Scene — byte-identical to the pre-UX2-F callers.
        Assert.Equal(SaveBlockReason.None, EditorOverlay.SaveBlock(Paused(), resolved));
    }

    [Fact]
    public void ToolbarSaveButton_IsInertInGameMode_ViaTheSharedGuard()
    {
        using var world = new World();
        var save = MakeButton(world, EditorToolbarAction.Save, new Rectangle(0, 0, 60, 30));
        var cursor = MakeCursor(world);
        var resolved = ResolvedContext();
        var viewMode = ViewportContextKind.Game;

        var dispatched = new List<EditorToolbarAction>();
        // The SAME dim predicate the overlay wires: Save is blocked (inert) in Game mode.
        using var toolbar = new ToolbarSystem(world, (a, _) => dispatched.Add(a),
            isEditingActionBlocked: (action, state) => action == EditorToolbarAction.Save
                && EditorOverlay.SaveBlock(state, resolved, viewMode)
                    is SaveBlockReason.NoProjectRoot or SaveBlockReason.GameMode);

        Click(cursor, new Vector2(30, 15));
        toolbar.Update(Paused());
        Assert.Empty(dispatched); // Game mode → Save click suppressed

        // Exit to Scene mode → Save dispatches again.
        viewMode = ViewportContextKind.Scene;
        Click(cursor, new Vector2(30, 15));
        toolbar.Update(Paused());
        Assert.Equal(new[] { EditorToolbarAction.Save }, dispatched);
    }

    // ─────────────── Restart in Game mode → Scene mode, disk state, snapshot dropped ─────────────────

    [Fact]
    public void RestartInGameMode_LandsSceneMode_ReloadsDiskState_DropsSnapshot()
    {
        using var s = new Stack();
        s.AddSpriteRoot(new Vector2(10, 20));

        var loads = 0;
        s.Transport.Reload = () =>
        {
            loads++;
            var e = s.World.CreateEntity();
            e.Set(new SceneObjectComponent());
            e.Set(new TransformComponent(new Vector2(10, 20))); // the on-disk truth
        };

        var state = Playing();
        s.Transport.EnterGameMode(state); // (Playing is fine; the toggle can enter while paused, this asserts the reset)
        s.Transport.Restart(state);

        Assert.Equal(ViewportContextKind.Scene, s.Transport.ActiveContextKind); // reset to Scene
        Assert.Equal(RunMode.Edit, state.RunMode);                // lands Paused
        Assert.Equal(1, loads);                                    // reloaded from the recorded load (disk)
        // The snapshot was dropped: entering Game again snapshots the reloaded world afresh.
        s.SnapshotCaptures = 0;
        s.Transport.EnterGameMode(Paused());
        Assert.Equal(1, s.SnapshotCaptures);
    }

    // ─────────────── Camera view: enter adopts the rig; exit restores the captured Scene view ──────────
    // NOTE (CM-A): the "rig untouched across the Game round-trip" assertion was DROPPED — the rig no longer
    // round-trips through the snapshot (the snapshot carries no camera block; the camera is a scene ENTITY
    // now), so on exit-restore the vestigial rig adopts the reader's auto-framed view. The enter-adopts-rig
    // + exit-restores-view VIEW behaviors below still hold this wave; the camera-entity's own Game-tab
    // round-trip (it does NOT leak Play movement into the scene tab) is covered by CameraEntityTests.

    [Fact]
    public void Camera_Enter_AdoptsRigView_Exit_RestoresCapturedSceneView()
    {
        using var s = new Stack();
        s.AddSpriteRoot(new Vector2(200, 100));

        // The authored rig differs from the free view.
        s.Rig.Entity.Get<TransformComponent>().Position = new Vector2(100, 50);
        s.Rig.Entity.Get<CameraRigComponent>() = new CameraRigComponent(2f, 0f);
        s.Camera.Position = new Vector2(5, 5);
        s.Camera.Zoom = 1f;

        s.Transport.EnterGameMode(Paused());
        // Entry adopts the game-camera view (Camera := rig).
        Assert.Equal(new Vector2(100, 50), s.Camera.Position);
        Assert.Equal(2f, s.Camera.Zoom);

        s.Transport.ExitToSceneMode(Paused());
        // Exit restores the captured Scene view (overriding the reader's auto-frame).
        Assert.Equal(new Vector2(5, 5), s.Camera.Position);
        Assert.Equal(1f, s.Camera.Zoom);
    }

    // ─────────────── CM pre-mortem #4: a Play session that moved the camera ENTITY does not leak ────────

    [Fact]
    public void CameraEntity_MovedInPlay_DoesNotLeakIntoTheSceneTab()
    {
        using var s = new Stack();
        s.AddSpriteRoot(new Vector2(10, 20));

        // A scene camera ENTITY at its authored position (SceneObjectComponent so it is captured/restored).
        var cam = s.World.CreateEntity();
        cam.Set(new SceneObjectComponent());
        cam.Set(new EntityInfoComponent("Camera"));
        cam.Set(new TransformComponent(new Vector2(30, 40)));
        cam.Set(new CameraComponent { Zoom = 1f });

        s.Transport.EnterGameMode(Paused()); // snapshots the scene (incl. the camera entity at (30,40))

        // In the sandbox, "Play" moves the camera entity (as CameraFollowSystem would).
        CameraOf(s.World).Get<TransformComponent>().Position = new Vector2(999, 888);

        s.Transport.ExitToSceneMode(Paused());

        // The scene tab is restored from the snapshot — the sandbox's camera movement did NOT leak.
        var restored = CameraOf(s.World);
        Assert.Equal(new Vector2(30, 40), restored.Get<TransformComponent>().Position);
        // Still exactly one camera entity (no duplication across the round-trip).
        using var cams = s.World.GetEntities().With<CameraComponent>().AsSet();
        Assert.Single(cams.GetEntities().ToArray());
    }

    private static Entity CameraOf(World world)
    {
        using var set = world.GetEntities().With<CameraComponent>().AsSet();
        foreach (var e in set.GetEntities()) return e;
        return default;
    }

    // ─────────────── The restore shares the reader: a file: sprite survives with DrawComponent ────────

    [Fact]
    public void GameModeRoundTrip_SharesTheReader_FileKeySpriteKeepsTextureRehydrationAndDrawComponent()
    {
        using var s = new Stack();
        s.AddSpriteRoot(new Vector2(3, 4), assetKey: "file:props/rock.png");

        s.Transport.EnterGameMode(Paused());
        s.RehydratedKeys.Clear();
        s.TaggedRoot().Get<TransformComponent>().Position = new Vector2(50, 50); // churn
        s.Transport.ExitToSceneMode(Paused());

        var restored = s.TaggedRoot();
        // The file: key round-tripped and the reader's file-asset rehydration ran (shared path).
        Assert.Equal("file:props/rock.png", restored.Get<SpriteInfoComponent>().AssetKey);
        Assert.Contains("file:props/rock.png", s.RehydratedKeys);
        // The reader restored the transient DrawComponent — WITHOUT it the reloaded sprite is invisible
        // (the pre-mortem #2 blank-screen regression a forked restore path would reintroduce).
        Assert.True(restored.Has<DrawComponent>());
        Assert.Equal(DrawElementType.Sprite, restored.Get<DrawComponent>().Type);
    }

    // ─────────────── Auto-play on Game-tab entry (PF-B: the mode toggle is retired) ───────────────────
    // The retired [Scene | Game] mode-toggle SEGMENT UI tests moved to ViewportTabStripTests (the strip
    // that replaced the toggle). The auto-play + snapshot-before-flip SEMANTICS they also covered survive
    // here (PlayInSceneMode_SnapshotsBeforeRunModeFlipsToPlay_AndAutoEntersGame) and in the tab-strip
    // "Play spawns + activates the Game tab" test.

    [Fact]
    public void GameModeEntry_LandsPlaying_ExitLandsPaused()
    {
        using var s = new Stack();
        s.AddSpriteRoot(new Vector2(10, 20));
        var state = Paused();

        // Entering via Play (the composition the toggle reuses) lands Playing in Game mode; exit lands
        // Paused in Scene mode (unchanged) — the exit is still Edit.
        s.Transport.Play(state);
        Assert.Equal(ViewportContextKind.Game, s.Transport.ActiveContextKind);
        Assert.Equal(RunMode.Play, state.RunMode);

        s.Transport.ExitToSceneMode(state);
        Assert.Equal(ViewportContextKind.Scene, s.Transport.ActiveContextKind);
        Assert.Equal(RunMode.Edit, state.RunMode);
    }

    // ─────────────── UX3-A pre-mortem #2: a zeroed/unwired CaptureView must never blank the view ──────

    [Fact]
    public void Exit_WithZeroedOrUnwiredCaptureView_KeepsTheAutoFramedView_NeverBlanks()
    {
        using var s = new Stack();
        s.AddSpriteRoot(new Vector2(200, 100)); // off-origin content

        // Simulate an unwired/zeroed CaptureView: the snapshot view is default(CameraViewSnapshot),
        // whose Zoom == 0 (not IsValid). Without the guard, exit-restore applies it and Camera.Zoom
        // clamps the 0 → 0.1f, silently blanking the view at the origin.
        s.Transport.CaptureView = () => default;

        s.Transport.EnterGameMode(Paused());
        s.Transport.ExitToSceneMode(Paused());

        // The zeroed snapshot was NOT applied: exit kept the reader's post-restore auto-frame, which sits
        // on the restored off-origin content — a usable, positive-zoom view, not origin/0.1.
        Assert.True(s.Camera.Zoom >= 0.1f);
        Assert.True(s.Camera.Position.X > 100f,
            $"view X {s.Camera.Position.X} should sit on the ~200 content, not the zeroed-snapshot origin");
        Assert.True(s.TaggedRoot().IsAlive); // and the world is intact
    }

    [Fact]
    public void CameraViewSnapshot_Default_IsInvalid_RealCapture_IsValid()
    {
        Assert.False(default(CameraViewSnapshot).IsValid);                          // Zoom == 0 → invalid
        Assert.True(new CameraViewSnapshot(new Vector2(3, 4), 1f, 0f).IsValid);     // a real capture is valid
        Assert.False(new CameraViewSnapshot(new Vector2(3, 4), 0f, 0f).IsValid);    // zero zoom → invalid
    }

    // ─────────────── helpers ───────────────

    private static Entity MakeButton(World world, EditorToolbarAction action, Rectangle bounds)
    {
        var button = world.CreateEntity();
        button.Set(new TransformComponent(new Vector2(bounds.X, bounds.Y)));
        button.Set(new SimpleButtonComponent { Size = new Vector2(bounds.Width, bounds.Height) });
        button.Set(new ToolbarButtonComponent { Action = action, Bounds = bounds });
        return button;
    }

    private static Entity MakeCursor(World world)
    {
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent());
        return cursor;
    }

    private static void Click(Entity cursor, Vector2 screenPoint)
    {
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScreenPosition = screenPoint;
        input.LeftButtonReleased = true;
    }
}
