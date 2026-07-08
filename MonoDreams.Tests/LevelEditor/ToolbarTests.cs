using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DefaultEcs;
using Microsoft.Xna.Framework;
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
using MonoDreams.Platform;
using MonoDreams.State;
using MonoDreams.UI;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the Wave 4b toolbar wiring (item 14): each <see cref="EditorToolbarAction"/> drives the
/// SAME shared instances the editor uses — Save writes through <see cref="SceneWriter"/> (via the
/// <see cref="IPlatformServices"/> export seam), Load publishes a <see cref="LoadSceneRequest"/>,
/// Undo/Redo drive the shared <see cref="EditorHistory"/>, snap-toggle flips
/// <see cref="GizmoStateComponent.SnapEnabled"/>, and tool-select sets <see cref="GizmoStateComponent.Tool"/>.
///
/// Tests at the handler/command level (a full UI render is not required): a dispatch closure built
/// exactly like <c>LevelEditorScreen.DispatchToolbarAction</c> — same shared <see cref="EditorHistory"/>,
/// <see cref="SceneSerializer"/>/<see cref="SceneWriter"/>, gizmo-state entity, and
/// <see cref="LoadSceneRequest"/> publish — and asserts each action's observable effect. The Save test
/// uses a fake <see cref="IPlatformServices"/> (no disk).
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class ToolbarTests
{
    private const string SceneFileName = "toolbar-test.scene.json";

    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    /// <summary>In-memory platform capturing the Save write (PS3 writes via WriteAllText) so the Save
    /// test asserts the writer ran without a disk.</summary>
    private sealed class InMemoryPlatformServices : IPlatformServices
    {
        public Dictionary<string, string> Files { get; } = new();
        public int WriteCount { get; private set; }
        public StringWriter LogWriter { get; } = new();
        public string BaseDirectory => "/scene/";
        public string GetEnvironmentVariable(string name) => null;
        public string CombinePath(params string[] paths) => string.Join("/", paths);
        public bool FileExists(string path) => Files.ContainsKey(path);
        public string ReadAllText(string path) =>
            Files.TryGetValue(path, out var v) ? v : throw new FileNotFoundException(path);
        public void WriteAllText(string path, string contents) { Files[path] = contents; WriteCount++; }
        public void WriteAllBytes(string path, byte[] bytes) { }
        public string ExportScene(string suggestedFileName, string contents)
        {
            Files[suggestedFileName] = contents;
            return suggestedFileName;
        }
        public void CreateDirectory(string path) { }
        public TextWriter OpenLogWriter(string directory, string fileName) => LogWriter;
        public void WriteLineToConsole(string line) { }
        public void RunBackground(Action work) => work();
    }

    private static void WithPlatform(InMemoryPlatformServices fake, Action body)
    {
        var previous = PlatformServices.Current;
        try { PlatformServices.Current = fake; body(); }
        finally { PlatformServices.Current = previous; }
    }

    private static ComponentSerializerRegistry NewEngineRegistry()
    {
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        return registry;
    }

    /// <summary>
    /// Builds the toolbar dispatch closure mirroring <c>LevelEditorScreen.DispatchToolbarAction</c>:
    /// same shared history + serializer + gizmo-state entity, Load publishing a LoadSceneRequest.
    /// </summary>
    private static Action<EditorToolbarAction> BuildDispatch(
        World world, EditorHistory history, SceneSerializer serializer, Entity gizmoState)
    {
        void SetTool(GizmoTool t) { ref var s = ref gizmoState.Get<GizmoStateComponent>(); s.Tool = t; }
        void ToggleSnap() { ref var s = ref gizmoState.Get<GizmoStateComponent>(); s.SnapEnabled = !s.SnapEnabled; }

        return action =>
        {
            switch (action)
            {
                case EditorToolbarAction.ToolMove: SetTool(GizmoTool.Move); break;
                case EditorToolbarAction.ToolRotate: SetTool(GizmoTool.Rotate); break;
                case EditorToolbarAction.ToolScale: SetTool(GizmoTool.Scale); break;
                case EditorToolbarAction.ToggleSnap: ToggleSnap(); break;
                case EditorToolbarAction.Save:
                    new SceneWriter(serializer).Save(world, SceneFileName, camera: null, layers: null);
                    break;
                case EditorToolbarAction.Undo: history.Undo(); break;
                case EditorToolbarAction.Redo: history.Redo(); break;
            }
        };
    }

    /// <summary>A counter-mutating command (DATA + apply/revert) so Undo/Redo have something to drive.</summary>
    private sealed class IncrementCommand : IEditorCommand
    {
        private readonly int[] _box;
        public IncrementCommand(int[] box) { _box = box; }
        public void Apply(World world) => _box[0]++;
        public void Revert(World world) => _box[0]--;
    }

    [Fact]
    public void ToolbarWiringTest()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            using var world = new World();
            var registry = NewEngineRegistry();
            var serializer = new SceneSerializer(registry);
            var history = new EditorHistory(world);

            // The single shared gizmo-state entity (defaults: Move tool, snap off).
            var gizmoState = world.CreateEntity();
            gizmoState.Set(GizmoStateComponent.Default);

            // Capture LoadSceneRequest publications.
            var loadRequests = new List<LoadSceneRequest>();
            world.Subscribe((in LoadSceneRequest r) => loadRequests.Add(r));

            // A tagged save-root so Save has something to serialize and export.
            var root = world.CreateEntity();
            root.Set(new SceneObjectComponent());
            root.Set(new EntityInfoComponent("Player", "Hero"));
            root.Set(new TransformComponent(new Vector2(1, 2)));

            var dispatch = BuildDispatch(world, history, serializer, gizmoState);

            // ---- tool-select sets the active tool ----
            dispatch(EditorToolbarAction.ToolRotate);
            Assert.Equal(GizmoTool.Rotate, gizmoState.Get<GizmoStateComponent>().Tool);
            dispatch(EditorToolbarAction.ToolScale);
            Assert.Equal(GizmoTool.Scale, gizmoState.Get<GizmoStateComponent>().Tool);
            dispatch(EditorToolbarAction.ToolMove);
            Assert.Equal(GizmoTool.Move, gizmoState.Get<GizmoStateComponent>().Tool);

            // ---- snap-toggle flips the flag ----
            Assert.False(gizmoState.Get<GizmoStateComponent>().SnapEnabled);
            dispatch(EditorToolbarAction.ToggleSnap);
            Assert.True(gizmoState.Get<GizmoStateComponent>().SnapEnabled);
            dispatch(EditorToolbarAction.ToggleSnap);
            Assert.False(gizmoState.Get<GizmoStateComponent>().SnapEnabled);

            // ---- Save invokes SceneWriter (PS3 writes through IPlatformServices.WriteAllText) ----
            Assert.Equal(0, fake.WriteCount);
            dispatch(EditorToolbarAction.Save);
            Assert.Equal(1, fake.WriteCount);
            Assert.True(fake.Files.ContainsKey(SceneFileName));

            // ---- (There is no Load action — a scene is opened via the Scenes panel, UX-C/UX-D. The
            //      LoadSceneRequest subscription below stays wired but no toolbar action publishes one.) ----
            Assert.Empty(loadRequests);

            // ---- Undo / Redo drive the shared history ----
            var box = new int[] { 0 };
            history.Push(new IncrementCommand(box)); // box = 1, one undo entry
            Assert.Equal(1, box[0]);
            Assert.Equal(1, history.Count);

            dispatch(EditorToolbarAction.Undo);
            Assert.Equal(0, box[0]);
            Assert.Equal(0, history.Count);
            Assert.Equal(1, history.RedoCount);

            dispatch(EditorToolbarAction.Redo);
            Assert.Equal(1, box[0]);
            Assert.Equal(1, history.Count);

            // ---- empty-stack undo/redo are no-ops (the toolbar wires the buttons unconditionally) ----
            dispatch(EditorToolbarAction.Undo); // box = 0
            dispatch(EditorToolbarAction.Undo); // no-op (nothing left)
            Assert.Equal(0, box[0]);
            Assert.Equal(0, history.Count);
        });
    }

    // ---- SaveGuardTest: Save is blocked while Playing (island-authoring Slice 1) OR when no
    // project root is resolved (PS2). The two causes are distinguishable (SaveBlockReason). ----

    /// <summary>A resolved project context (env var → an in-memory manifest), for the guard tests.</summary>
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

    /// <summary>
    /// The save-guard reasons (<see cref="EditorOverlay.SaveBlock"/> — the exact check
    /// <c>EditorOverlay.DispatchToolbarAction</c>'s Save case and the toolbar dim run): Playing takes
    /// precedence; Paused + resolved is allowed; Paused + unresolved is blocked with the distinct
    /// <see cref="SaveBlockReason.NoProjectRoot"/> cause.
    /// </summary>
    [Fact]
    public void SaveGuardTest_BlockedWhilePlayingOrWithoutAProjectRoot()
    {
        var resolved = ResolvedContext();
        Assert.True(resolved.Resolved);
        var playing = new GameState(new GameTime()) { RunMode = RunMode.Play };
        var paused = new GameState(new GameTime()) { RunMode = RunMode.Edit };

        // Playing takes precedence regardless of project state (existing behaviour).
        Assert.Equal(SaveBlockReason.Playing, EditorOverlay.SaveBlock(playing, resolved));
        Assert.Equal(SaveBlockReason.Playing, EditorOverlay.SaveBlock(playing, EditorProjectContext.Unresolved));
        Assert.Equal(SaveBlockReason.Playing, EditorOverlay.SaveBlock(playing, null));

        // Paused + resolved = allowed.
        Assert.Equal(SaveBlockReason.None, EditorOverlay.SaveBlock(paused, resolved));
        Assert.False(EditorOverlay.IsSaveBlocked(paused, resolved));

        // Paused + no project root = blocked, with the distinguishable reason (PS2).
        Assert.Equal(SaveBlockReason.NoProjectRoot, EditorOverlay.SaveBlock(paused, EditorProjectContext.Unresolved));
        Assert.Equal(SaveBlockReason.NoProjectRoot, EditorOverlay.SaveBlock(paused, null));
        Assert.True(EditorOverlay.IsSaveBlocked(paused, null));
    }

    /// <summary>
    /// The guarded dispatch: a Save arriving while blocked (Playing, or Paused with no project root —
    /// through any dispatch path; the toolbar button also renders dimmed) is a loud no-op; a Save
    /// while Paused with a resolved project exports. Mirrors the overlay's Save case, including the
    /// REAL <see cref="EditorOverlay.IsSaveBlocked"/> guard.
    /// </summary>
    [Fact]
    public void SaveGuardTest_DispatchNoOpsWhileBlockedAndSavesWhenAllowed()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            using var world = new World();
            var serializer = new SceneSerializer(NewEngineRegistry());
            var resolved = ResolvedContext();

            var root = world.CreateEntity();
            root.Set(new SceneObjectComponent());
            root.Set(new TransformComponent(new Vector2(1, 2)));

            // The overlay's Save case shape: guard first, then SceneWriter.Save.
            void DispatchSave(GameState state, EditorProjectContext ctx)
            {
                if (EditorOverlay.IsSaveBlocked(state, ctx)) return; // (the overlay also logs a warning)
                new SceneWriter(serializer).Save(world, SceneFileName, camera: null, layers: null);
            }

            // Playing (even with a resolved project): blocked.
            DispatchSave(new GameState(new GameTime()) { RunMode = RunMode.Play }, resolved);
            Assert.Equal(0, fake.WriteCount);

            // Paused but no project root: blocked (PS2 cause).
            DispatchSave(new GameState(new GameTime()) { RunMode = RunMode.Edit }, EditorProjectContext.Unresolved);
            Assert.Equal(0, fake.WriteCount);

            // Paused + resolved project: saves normally.
            DispatchSave(new GameState(new GameTime()) { RunMode = RunMode.Edit }, resolved);
            Assert.Equal(1, fake.WriteCount);
        });
    }

    // ---- UX2-B/-C: transport + tool relocation to the Scene panel header ----------

    /// <summary>The window top bar slimmed further in UX2-C: the transform-tool cluster
    /// (Move/Rotate/Scale/Boundary/Snap) left it for the Scene panel header (joining the UX2-B
    /// transport), leaving Save/Undo/Redo/Refresh plus the still-text selection-context actions.</summary>
    [Fact]
    public void WindowBar_IsSlimmed_ToolsRelocatedToTheHeader()
    {
        var windowActions = EditorChromeBuilder.DefaultButtons.Select(b => b.action).ToArray();
        var headerActions = EditorChromeBuilder.HeaderButtons.Select(b => b.action).ToArray();

        // The transport left the window bar (UX2-B); the transform tools left it too (UX2-C).
        Assert.DoesNotContain(EditorToolbarAction.PlayPause, windowActions);
        Assert.DoesNotContain(EditorToolbarAction.Restart, windowActions);
        Assert.DoesNotContain(EditorToolbarAction.ToolMove, windowActions);
        Assert.DoesNotContain(EditorToolbarAction.ToolRotate, windowActions);
        Assert.DoesNotContain(EditorToolbarAction.ToolScale, windowActions);
        Assert.DoesNotContain(EditorToolbarAction.ToolBoundary, windowActions);
        Assert.DoesNotContain(EditorToolbarAction.ToggleSnap, windowActions);

        // The header leads with the transport cluster, then the tool cluster.
        Assert.Equal(EditorToolbarAction.PlayPause, headerActions[0]);
        Assert.Equal(EditorToolbarAction.Restart, headerActions[1]);
        Assert.Contains(EditorToolbarAction.ToolMove, headerActions);
        Assert.Contains(EditorToolbarAction.ToolRotate, headerActions);
        Assert.Contains(EditorToolbarAction.ToolScale, headerActions);
        Assert.Contains(EditorToolbarAction.ToolBoundary, headerActions);
        Assert.Contains(EditorToolbarAction.ToggleSnap, headerActions);

        // The remaining editing actions stay on the window bar this wave.
        Assert.Contains(EditorToolbarAction.Save, windowActions);
        Assert.Contains(EditorToolbarAction.Undo, windowActions);
        Assert.Contains(EditorToolbarAction.Redo, windowActions);
        Assert.Contains(EditorToolbarAction.RefreshCatalog, windowActions);
        // UX2-D: the within-band Order buttons left the TOOLBAR ENTIRELY (into the entity context
        // menus); neither bar carries them. The collider/vertex authoring text buttons remain.
        Assert.DoesNotContain(EditorToolbarAction.OrderForward, windowActions);
        Assert.DoesNotContain(EditorToolbarAction.OrderBack, windowActions);
        Assert.DoesNotContain(EditorToolbarAction.OrderForward, headerActions);
        Assert.DoesNotContain(EditorToolbarAction.OrderBack, headerActions);
        Assert.Contains(EditorToolbarAction.ColliderAddBox, windowActions);
        // UX2-D: the fixed "Entity ▾" dropdown lives in the Scene-panel header.
        Assert.Contains(EditorToolbarAction.EntityMenu, headerActions);
    }

    /// <summary>UX2-C icon reality: the icon buttons (transport, tools, Save/Undo/Redo/Refresh) render a
    /// glyph MESH tinted by state — <c>Accent</c> for the active radio tool, <c>Success</c> for the Snap
    /// toggle when on, <c>TextDisabled</c> while inert (Playing) — and carry no text label; the
    /// selection-context actions stay TEXT buttons (a label, no icon).</summary>
    [Fact]
    public void IconButtons_BakeGlyphMeshes_TintedByState()
    {
        using var world = new World();
        var chrome = new EditorChromeBuilder(world, label => label.Length * 8f);
        chrome.Build(1600, 900);
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent());
        var gizmo = world.CreateEntity();
        gizmo.Set(GizmoStateComponent.Default); // Move tool active, snap off

        using var toolbar = new ToolbarSystem(world, (_, _) => { });

        Entity ButtonOf(EditorToolbarAction action)
        {
            using var set = world.GetEntities().With<ToolbarButtonComponent>().AsSet();
            foreach (var e in set.GetEntities())
                if (e.Get<ToolbarButtonComponent>().Action == action) return e;
            return default;
        }

        var move = ButtonOf(EditorToolbarAction.ToolMove);
        var snap = ButtonOf(EditorToolbarAction.ToggleSnap);
        // A still-text selection-context button (UX2-D relocated Order into menus, so use a collider one).
        var textButton = ButtonOf(EditorToolbarAction.ColliderAddBox);

        // Icon buttons carry an IconEntity and no label; the text button is the reverse.
        Assert.NotNull(move.Get<ToolbarButtonComponent>().IconEntity);
        Assert.Null(move.Get<SimpleButtonComponent>().TextEntity);
        Assert.Null(textButton.Get<ToolbarButtonComponent>().IconEntity);
        Assert.NotNull(textButton.Get<SimpleButtonComponent>().TextEntity);

        // Paused: the Move tool is active → its glyph is baked in Accent; Snap is off → not Success.
        toolbar.Update(Edit());
        var moveIcon = move.Get<ToolbarButtonComponent>().IconEntity!.Value;
        Assert.True(moveIcon.Get<DrawComponent>().Vertices!.Length > 0);
        Assert.Equal(EditorTheme.Accent, moveIcon.Get<DrawComponent>().Vertices![0].Color);
        var snapIcon = snap.Get<ToolbarButtonComponent>().IconEntity!.Value;
        Assert.NotEqual(EditorTheme.Success, snapIcon.Get<DrawComponent>().Vertices![0].Color);

        // Turn snap on → the Snap glyph tints Success.
        ref var gs = ref gizmo.Get<GizmoStateComponent>();
        gs.SnapEnabled = true;
        toolbar.Update(Edit());
        Assert.Equal(EditorTheme.Success, snapIcon.Get<DrawComponent>().Vertices![0].Color);

        // Playing: the tool is an editing action → inert → its glyph dims to TextDisabled.
        toolbar.Update(Play());
        Assert.Equal(EditorTheme.TextDisabled, moveIcon.Get<DrawComponent>().Vertices![0].Color);
    }

    /// <summary>The relocated transport dispatches through the SAME <c>ToolbarSystem</c> machinery from
    /// the Scene header — and does so while Playing (transport is live in both states), unlike the
    /// window-bar editing buttons which are inert in Play.</summary>
    [Fact]
    public void HeaderTransport_DispatchesFromTheHeader_WhilePlaying_WindowEditingInert()
    {
        using var world = new World();
        var chrome = new EditorChromeBuilder(world, label => label.Length * 8f);
        chrome.Build(1600, 900);
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent());

        var dispatched = new List<EditorToolbarAction>();
        using var toolbar = new ToolbarSystem(world, (a, _) => dispatched.Add(a));

        Rectangle BoundsOf(EditorToolbarAction action)
        {
            using var set = world.GetEntities().With<ToolbarButtonComponent>().AsSet();
            foreach (var e in set.GetEntities())
                if (e.Get<ToolbarButtonComponent>().Action == action) return e.Get<ToolbarButtonComponent>().Bounds;
            return Rectangle.Empty;
        }

        var playing = new GameState(new GameTime()) { RunMode = RunMode.Play };
        ref var input = ref cursor.Get<CursorInputComponent>();

        // The header PlayPause button lives in the Scene header and dispatches while Playing.
        var play = BoundsOf(EditorToolbarAction.PlayPause);
        Assert.True(EditorChromeLayout.SceneHeader(1600, 900).Contains(play), "PlayPause is not in the Scene header");
        input.ScreenPosition = new Vector2(play.Center.X, play.Center.Y);
        input.LeftButtonReleased = true;
        toolbar.Update(playing);
        Assert.Contains(EditorToolbarAction.PlayPause, dispatched);

        // A window-bar editing button (Save) is inert while Playing — a click belongs to the game.
        dispatched.Clear();
        var save = BoundsOf(EditorToolbarAction.Save);
        Assert.True(EditorChromeLayout.TopBar(1600).Contains(save), "Save is not in the window top bar");
        input.ScreenPosition = new Vector2(save.Center.X, save.Center.Y);
        input.LeftButtonReleased = true;
        toolbar.Update(playing);
        Assert.DoesNotContain(EditorToolbarAction.Save, dispatched);
    }
}
