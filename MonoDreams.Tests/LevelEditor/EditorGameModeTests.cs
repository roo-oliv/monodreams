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
/// Protects the UX2-F <b>Scene / Game mode sandbox</b> (design §5; level-editor premise "The Game-mode
/// sandbox …"): entering Game mode snapshots the scene in memory, Play/Pause/edit freely, and exiting
/// DISCARDS the sandbox by restoring the snapshot <b>through the reader</b> (the ONE restore path —
/// pre-mortem #2). The transport owns both <see cref="RunMode"/> and <see cref="EditorViewMode"/>; the
/// snapshot is taken BEFORE Play flips RunMode (pre-mortem #7); undo after exit is a no-op
/// (pre-mortem #3); Save is blocked in Game mode; Restart lands Scene mode; a switch exits first.
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
                return new SceneWriter(serializer).BuildScene(World, Rig.AsCamera(), layers: null);
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
        Assert.Equal(EditorViewMode.Game, s.Transport.ViewMode);

        // Poke the entity in the sandbox (a direct mutation stands in for a gizmo drag).
        s.TaggedRoot().Get<TransformComponent>().Position = new Vector2(99, 99);

        s.Transport.ExitToSceneMode(Paused());

        // The sandbox edit vanished — the scene is back at its pre-entry position (a NEW entity, restored).
        Assert.Equal(EditorViewMode.Scene, s.Transport.ViewMode);
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
        Assert.Equal(EditorViewMode.Game, s.Transport.ViewMode); // auto-entered Game
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
            EditorOverlay.SaveBlock(Paused(), resolved, EditorViewMode.Game));
        // Playing takes precedence over GameMode (the existing "Playing first" rule holds).
        Assert.Equal(SaveBlockReason.Playing,
            EditorOverlay.SaveBlock(Playing(), resolved, EditorViewMode.Game));
        // Back in Scene mode (after exit) Save works again.
        Assert.Equal(SaveBlockReason.None,
            EditorOverlay.SaveBlock(Paused(), resolved, EditorViewMode.Scene));
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
        var viewMode = EditorViewMode.Game;

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
        viewMode = EditorViewMode.Scene;
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

        Assert.Equal(EditorViewMode.Scene, s.Transport.ViewMode); // reset to Scene
        Assert.Equal(RunMode.Edit, state.RunMode);                // lands Paused
        Assert.Equal(1, loads);                                    // reloaded from the recorded load (disk)
        // The snapshot was dropped: entering Game again snapshots the reloaded world afresh.
        s.SnapshotCaptures = 0;
        s.Transport.EnterGameMode(Paused());
        Assert.Equal(1, s.SnapshotCaptures);
    }

    // ─────────────── Camera: enter adopts the rig; exit restores the Scene view; rig untouched ────────

    [Fact]
    public void Camera_Enter_AdoptsRig_Exit_RestoresSceneView_RigUntouched()
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
        // The rig itself is untouched throughout (the snapshot re-synced it to its enter-time state).
        Assert.Equal(new Vector2(100, 50), s.Rig.Position);
        Assert.Equal(2f, s.Rig.Zoom);
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

    // ─────────────── Toggle UI: segments render + hit-test (DPR-2), ops drive the same paths ─────────

    [Fact]
    public void ModeToggleSegments_AreInTheSceneHeader_HitTestAndDispatch_DprScaled()
    {
        foreach (var (w, h, scale) in new[] { (1600, 900, 1f), (3840, 2160, 2f) })
        {
            using var world = new World();
            var chrome = new EditorChromeBuilder(world, label => label.Length * 8f);
            chrome.Build(1600, 900);
            if (scale != 1f) chrome.Relayout(w, h, scale);
            var cursor = MakeCursor(world);

            var sceneHeader = EditorChromeLayout.SceneHeader(w, h, scale);
            var scene = SegmentBounds(world, EditorToolbarAction.ModeScene);
            var game = SegmentBounds(world, EditorToolbarAction.ModeGame);
            Assert.True(sceneHeader.Contains(scene), $"Scene segment {scene} escapes the header at DPR {scale}");
            Assert.True(sceneHeader.Contains(game), $"Game segment {game} escapes the header at DPR {scale}");
            // Equal-width segments, DPR-scaled and adjacent (Game sits immediately right of Scene).
            Assert.Equal(EditorChromeLayout.Px(EditorChromeLayout.ModeSegmentWidth, scale), scene.Width);
            Assert.Equal(scene.Width, game.Width);
            Assert.Equal(scene.Right, game.Left);

            // A click on the Game segment dispatches ModeGame; on Scene, ModeScene — the same action the
            // mode:game / mode:scene ops route through (both live in both transport states).
            var dispatched = new List<EditorToolbarAction>();
            var view = EditorViewMode.Scene;
            using var toolbar = new ToolbarSystem(world, (a, _) => dispatched.Add(a),
                viewMode: () => view);
            Click(cursor, new Vector2(game.Center.X, game.Center.Y));
            toolbar.Update(Playing()); // live even while Playing (exit-the-sandbox affordance)
            Assert.Equal(new[] { EditorToolbarAction.ModeGame }, dispatched);
        }
    }

    [Fact]
    public void ModeToggleSegment_RendersTabStyle_ActiveSegmentUnderlined()
    {
        using var world = new World();
        var chrome = new EditorChromeBuilder(world, label => label.Length * 8f);
        chrome.Build(1600, 900);
        MakeCursor(world);

        var view = EditorViewMode.Scene;
        using var toolbar = new ToolbarSystem(world, (_, _) => { }, viewMode: () => view);
        toolbar.Update(Paused());

        // Scene mode: the Scene segment is active (Bg1 fill + non-empty accent underline); Game is not.
        var scene = SegmentEntity(world, EditorToolbarAction.ModeScene);
        var game = SegmentEntity(world, EditorToolbarAction.ModeGame);
        Assert.Equal(EditorTheme.Bg1, scene.Get<SimpleButtonComponent>().FillColor);
        Assert.NotEmpty(scene.Get<ToolbarButtonComponent>().UnderlineEntity!.Value.Get<DrawComponent>().Vertices);
        Assert.Empty(game.Get<ToolbarButtonComponent>().UnderlineEntity!.Value.Get<DrawComponent>().Vertices);

        // Switch the view mode to Game → the underline follows to the Game segment.
        view = EditorViewMode.Game;
        toolbar.Update(Paused());
        Assert.Empty(scene.Get<ToolbarButtonComponent>().UnderlineEntity!.Value.Get<DrawComponent>().Vertices);
        Assert.NotEmpty(game.Get<ToolbarButtonComponent>().UnderlineEntity!.Value.Get<DrawComponent>().Vertices);
        Assert.Equal(EditorTheme.Bg1, game.Get<SimpleButtonComponent>().FillColor);
    }

    // ─────────────── UX3-A ask 2: explicit labels + auto-play on Game-mode entry ──────────────────────

    [Fact]
    public void ModeToggleSegments_ReadExplicitModeLabels()
    {
        using var world = new World();
        var chrome = new EditorChromeBuilder(world, label => label.Length * 8f);
        chrome.Build(1600, 900);

        // UX3-A: the toggle segments read the explicit "Scene mode" / "Game mode" (not bare "Scene" /
        // "Game"), and the segment width is recomputed to fit them within the Scene header.
        Assert.Equal("Scene mode", SegmentLabel(world, EditorToolbarAction.ModeScene));
        Assert.Equal("Game mode", SegmentLabel(world, EditorToolbarAction.ModeGame));

        var sceneHeader = EditorChromeLayout.SceneHeader(1600, 900);
        Assert.True(sceneHeader.Contains(SegmentBounds(world, EditorToolbarAction.ModeScene)));
        Assert.True(sceneHeader.Contains(SegmentBounds(world, EditorToolbarAction.ModeGame)));
    }

    [Fact]
    public void GameModeToggleClick_EntersGameAndAutoPlays_SnapshotBeforeTheFlip()
    {
        using var s = new Stack();
        s.AddSpriteRoot(new Vector2(10, 20));

        // The real Scene-header mode segments + the ONE ToolbarSystem, its dispatch wired to the shared
        // transport EXACTLY as EditorOverlay.DispatchToolbarAction wires the [Scene mode | Game mode]
        // toggle (UX3-A): ModeGame → Transport.Play (enter Game + auto-play), ModeScene → ExitToSceneMode.
        var chrome = new EditorChromeBuilder(s.World, label => label.Length * 8f);
        chrome.Build(1600, 900);
        var cursor = MakeCursor(s.World);

        var state = Paused(); // boot: Scene + Edit
        var runModeAtCapture = RunMode.Play;
        var baseCapture = s.Transport.CaptureSnapshot!;
        s.Transport.CaptureSnapshot = () => { runModeAtCapture = state.RunMode; return baseCapture(); };

        using var toolbar = new ToolbarSystem(s.World,
            (a, st) =>
            {
                if (a == EditorToolbarAction.ModeGame) s.Transport.Play(st);
                else if (a == EditorToolbarAction.ModeScene) s.Transport.ExitToSceneMode(st);
            },
            viewMode: () => s.Transport.ViewMode);

        var game = SegmentBounds(s.World, EditorToolbarAction.ModeGame);
        Click(cursor, new Vector2(game.Center.X, game.Center.Y));
        toolbar.Update(state);

        Assert.Equal(EditorViewMode.Game, s.Transport.ViewMode); // entered the sandbox
        Assert.Equal(RunMode.Play, state.RunMode);               // AND auto-played (UX3-A ask 2)
        Assert.Equal(RunMode.Edit, runModeAtCapture);            // snapshot taken BEFORE the flip (pre-mortem #7)
        Assert.Equal(1, s.SnapshotCaptures);                     // exactly one snapshot for the session
    }

    [Fact]
    public void GameModeEntry_LandsPlaying_ExitLandsPaused()
    {
        using var s = new Stack();
        s.AddSpriteRoot(new Vector2(10, 20));
        var state = Paused();

        // Entering via Play (the composition the toggle reuses) lands Playing in Game mode; exit lands
        // Paused in Scene mode (unchanged) — the exit is still Edit.
        s.Transport.Play(state);
        Assert.Equal(EditorViewMode.Game, s.Transport.ViewMode);
        Assert.Equal(RunMode.Play, state.RunMode);

        s.Transport.ExitToSceneMode(state);
        Assert.Equal(EditorViewMode.Scene, s.Transport.ViewMode);
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

    private static string SegmentLabel(World world, EditorToolbarAction action)
    {
        var label = SegmentEntity(world, action).Get<SimpleButtonComponent>().TextEntity!.Value;
        return label.Get<DynamicTextComponent>().TextContent;
    }

    private static Entity SegmentEntity(World world, EditorToolbarAction action)
    {
        using var set = world.GetEntities().With<ToolbarButtonComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.Get<ToolbarButtonComponent>().Action == action) return e;
        return default;
    }

    private static Rectangle SegmentBounds(World world, EditorToolbarAction action) =>
        SegmentEntity(world, action).Get<ToolbarButtonComponent>().Bounds;

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
