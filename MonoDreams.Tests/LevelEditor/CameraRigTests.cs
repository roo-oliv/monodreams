using System;
using System.Collections.Generic;
using System.IO;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Platform;
using MonoDreams.Renderer;
using MonoDreams.State;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the UX2-E <b>camera rig</b> invariants (level-editor premise "The editor splits the free
/// VIEW from the authored camera rig; Save serializes the rig"):
/// <list type="bullet">
///   <item>the shared <see cref="GameCamera"/> is the free VIEW (unchanged); the rig is a standalone
///   editor entity holding the AUTHORED game-camera state (position on its transform, zoom/rotation on
///   <see cref="CameraRigComponent"/>);</item>
///   <item>a load re-syncs the rig from <c>scene.camera</c>; Save reads <c>scene.camera</c> FROM the rig
///   (moving the VIEW never changes what Save writes); the rig never enters <c>entities[]</c>;</item>
///   <item>the glyph shows the frustum only while the view differs from the rig (epsilon); the
///   <c>view:camera</c> op snaps the view onto the rig; the rig is border-picked + gizmo-moved (one undo
///   step) and is NOT deletable; it survives + re-syncs across a transport Restart;</item>
///   <item>a SHIPPED reader (no rig seam) applies <c>scene.camera</c> to the live camera directly.</item>
/// </list>
/// The file-backed tests route through the process-global <see cref="PlatformServices.Current"/>, so the
/// class is in the non-parallel collection and restores the default.
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class CameraRigTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    private const string SceneFileName = "camera-rig.mdscene";

    private sealed class InMemoryPlatformServices : IPlatformServices
    {
        public Dictionary<string, string> Files { get; } = new();
        public string BaseDirectory => "/scene/";
        public string GetEnvironmentVariable(string name) => null;
        public string CombinePath(params string[] paths) => string.Join("/", paths);
        public bool FileExists(string path) => Files.ContainsKey(path);
        public string ReadAllText(string path) =>
            Files.TryGetValue(path, out var v) ? v : throw new FileNotFoundException(path);
        public void WriteAllText(string path, string contents) => Files[path] = contents;
        public void WriteAllBytes(string path, byte[] bytes) { }
        public string ExportScene(string suggestedFileName, string contents) { Files[suggestedFileName] = contents; return suggestedFileName; }
        public void CreateDirectory(string path) { }
        public TextWriter OpenLogWriter(string directory, string fileName) => TextWriter.Null;
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

    private static Texture2D StubTexture(string _) => null;

    /// <summary>Writes a scene (one tagged sprite root at <paramref name="content"/> so the reader has
    /// content to frame) carrying the given camera, into the in-memory store.</summary>
    private static void WriteScene(InMemoryPlatformServices fake, Vector2 content, GameCamera camera)
    {
        using var world = new World();
        var root = world.CreateEntity();
        root.Set(new SceneObjectComponent());
        root.Set(new EntityInfoComponent("Prop", "Tree"));
        root.Set(new TransformComponent(content));
        root.Set(new SpriteInfoComponent
        {
            AssetKey = "Atlas/TX Tree",
            Source = new Rectangle(0, 0, 16, 16),
            Size = new Vector2(16, 16),
            Color = Color.White,
            Target = RenderTargetID.Main,
            LayerDepth = 0.5f,
        });
        new SceneWriter(new SceneSerializer(NewEngineRegistry())).Save(world, SceneFileName, camera, layers: null);
    }

    // ─────────────────────────── CameraRigGlyph pure math ───────────────────────────

    [Fact]
    public void FrustumWorldCorners_AreVirtualSizeOverZoom_CenteredOnTheRig()
    {
        // center (100,50), zoom 2, virtual 800×600 → half extents 200×150.
        var c = CameraRigGlyph.FrustumWorldCorners(new Vector2(100, 50), zoom: 2f, virtualWidth: 800, virtualHeight: 600);
        Assert.Equal(new Vector2(-100, -100), c[0]); // TL
        Assert.Equal(new Vector2(300, -100), c[1]);  // TR
        Assert.Equal(new Vector2(300, 200), c[2]);   // BR
        Assert.Equal(new Vector2(-100, 200), c[3]);  // BL
    }

    [Fact]
    public void FrustumWorldCorners_NonPositiveZoom_DegradesToOne()
    {
        var c = CameraRigGlyph.FrustumWorldCorners(Vector2.Zero, zoom: 0f, virtualWidth: 800, virtualHeight: 600);
        Assert.Equal(new Vector2(-400, -300), c[0]); // as if zoom 1
        Assert.Equal(new Vector2(400, 300), c[2]);
    }

    [Fact]
    public void ViewMatchesRig_WithinEpsilon_True_BeyondEpsilon_False()
    {
        var rigPos = new Vector2(100, 100);
        // Position: 0.4 away (< 0.5 epsilon) matches; 0.6 away un-matches.
        Assert.True(CameraRigGlyph.ViewMatchesRig(new Vector2(100.4f, 100f), 1f, rigPos, 1f));
        Assert.False(CameraRigGlyph.ViewMatchesRig(new Vector2(100.6f, 100f), 1f, rigPos, 1f));
        // Zoom: 0.0005 away (< 1e-3) matches; 0.002 away un-matches.
        Assert.True(CameraRigGlyph.ViewMatchesRig(rigPos, 1.0005f, rigPos, 1f));
        Assert.False(CameraRigGlyph.ViewMatchesRig(rigPos, 1.002f, rigPos, 1f));
    }

    // ─────────────────────────── Rig materialization from a load ───────────────────────────

    [Fact]
    public void RigMaterializesFromLoad_FileCameraBecomesRigState_ViewFramesContent()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            var authored = new GameCamera(800, 600) { Position = new Vector2(300, -200), Rotation = 0.5f };
            authored.Zoom = 2f;
            WriteScene(fake, content: new Vector2(1275, -530), camera: authored);

            using var world = new World();
            var view = new GameCamera(800, 600); // the free VIEW starts at (0,0)
            var rig = new EditorCameraRig(world, view);
            using var reader = new SceneReaderSystem(world, new SceneSerializer(NewEngineRegistry()),
                content: null, loadTexture: StubTexture, camera: view, applyCameraToRig: rig.SyncFromScene);

            world.Publish(new LoadSceneRequest(SceneFileName, fromContent: false));

            // The RIG holds the authored camera state (scene.camera), verbatim.
            Assert.Equal(new Vector2(300, -200), rig.Position);
            Assert.Equal(2f, rig.Zoom);
            Assert.Equal(0.5f, rig.Rotation);

            // The VIEW auto-framed the off-origin content (it is NOT the authored camera — split proven).
            Assert.True(view.Position.X > 1000f, $"view X {view.Position.X} should sit on the ~1275 content");
            Assert.NotEqual(rig.Position, view.Position);
        });
    }

    // ─────────────────────────── Save reads the rig, never the view ───────────────────────────

    [Fact]
    public void SaveReadsRig_NotView()
    {
        using var world = new World();
        var view = new GameCamera(800, 600);
        var rig = new EditorCameraRig(world, view);

        // Move the RIG (what a gizmo drag does — the rig's own transform + component).
        rig.Entity.Get<TransformComponent>().Position = new Vector2(500, 500);
        rig.Entity.Get<CameraRigComponent>() = new CameraRigComponent(3f, 0.25f);

        // Move the VIEW somewhere else entirely.
        view.Position = new Vector2(9999, 9999);
        view.Zoom = 0.3f;

        var scene = new SceneWriter(new SceneSerializer(NewEngineRegistry())).BuildScene(world, rig.AsCamera());

        Assert.NotNull(scene.Camera);
        Assert.Equal(500f, scene.Camera.Position[0]);
        Assert.Equal(500f, scene.Camera.Position[1]);
        Assert.Equal(3f, scene.Camera.Zoom);
        Assert.Equal(0.25f, scene.Camera.Rotation);
    }

    [Fact]
    public void MovingTheView_DoesNotChangeWhatSaveWrites_NorDirtyTheHistory()
    {
        using var world = new World();
        var view = new GameCamera(800, 600) { Position = new Vector2(10, 20) };
        var rig = new EditorCameraRig(world, view);
        var history = new EditorHistory(world); // fresh → clean

        var before = new SceneWriter(new SceneSerializer(NewEngineRegistry())).BuildScene(world, rig.AsCamera()).Camera;

        // Pan/zoom the free VIEW (as CameraNavSystem does — it never touches the history).
        view.Position = new Vector2(4321, -876);
        view.Zoom = 0.5f;

        var after = new SceneWriter(new SceneSerializer(NewEngineRegistry())).BuildScene(world, rig.AsCamera()).Camera;

        Assert.Equal(before.Position[0], after.Position[0]);
        Assert.Equal(before.Position[1], after.Position[1]);
        Assert.Equal(before.Zoom, after.Zoom);
        Assert.False(history.IsDirty); // moving the view is not an edit
    }

    // ─────────────────────────── Membership: the rig never enters entities[] (pre-mortem #4) ────────

    [Fact]
    public void CameraRig_IsNeverSceneMembership()
    {
        using var world = new World();
        var view = new GameCamera(800, 600);
        var rig = new EditorCameraRig(world, view);

        // A real tagged root, to prove the filter keeps content but excludes the rig.
        var root = world.CreateEntity();
        root.Set(new SceneObjectComponent());
        root.Set(new TransformComponent(Vector2.Zero));

        var members = SceneWriter.CollectMembership(world);
        Assert.Contains(root, members);
        Assert.DoesNotContain(rig.Entity, members); // never SceneObjectComponent-tagged → never serialized
    }

    // ─────────────────────────── Glyph visibility (epsilon-gated, Edit-only) ───────────────────────

    [Fact]
    public void Glyph_HiddenWhenViewMatchesRig_And_InPlay_ShownWhenViewDiffers()
    {
        using var world = new World();
        var view = new GameCamera(800, 600);
        var rig = new EditorCameraRig(world, view);
        ref readonly var draw = ref rig.Entity.Get<DrawComponent>();

        // View == rig (both fresh at (0,0), zoom 1) → "you ARE the camera" → glyph parked (empty mesh).
        rig.EmitGlyph(Edit());
        Assert.Empty(draw.Vertices);

        // View navigated a little away (beyond the epsilon, but the rig's frustum still overlaps the
        // viewport) → glyph shows the frustum (non-empty mesh) in Edit. (A LARGE pan scrolls the frustum
        // fully off-screen, where OverlayMeshClip correctly clips it to nothing — "pan back to see it".)
        view.Position = new Vector2(10, 0);
        rig.EmitGlyph(Edit());
        Assert.NotEmpty(draw.Vertices);

        // In Play the glyph never draws (editing chrome), even with the view off the rig.
        rig.EmitGlyph(Play());
        Assert.Empty(draw.Vertices);
    }

    [Fact]
    public void Glyph_DprAndInsetProjection_ClipsToTheGameViewport()
    {
        using var world = new World();
        var view = new GameCamera(800, 600);
        var vm = new ViewportManager(null, 800, 600) { ScreenWidth = 1600, ScreenHeight = 900, DevicePixelRatio = 2f };
        vm.SetViewportInset(240, 84, 280, 168); // Blender-style chrome margins (device pixels)
        var rig = new EditorCameraRig(world, view, vm);

        // Zoom the rig out so its frustum world-rect exceeds the viewport, and offset the view a little
        // (view ≠ rig → glyph shows) so the frustum overlaps the viewport but spills past it → the glyph
        // must clip to the game-viewport rect.
        rig.Entity.Get<CameraRigComponent>() = new CameraRigComponent(0.8f);
        view.Position = new Vector2(60, 40);

        rig.EmitGlyph(Edit());
        var draw = rig.Entity.Get<DrawComponent>();
        Assert.NotEmpty(draw.Vertices);

        var dest = vm.DestinationRectangle;
        foreach (var v in draw.Vertices)
        {
            Assert.InRange(v.Position.X, (float)dest.Left, (float)dest.Right);
            Assert.InRange(v.Position.Y, (float)dest.Top, (float)dest.Bottom);
        }
    }

    // ─────────────────────────── view:camera snaps the view onto the rig ───────────────────────────

    [Fact]
    public void SnapViewToRig_CopiesRigStateOntoTheView()
    {
        using var world = new World();
        var view = new GameCamera(800, 600) { Position = new Vector2(1000, 2000) };
        view.Zoom = 0.5f;
        var rig = new EditorCameraRig(world, view);
        rig.Entity.Get<TransformComponent>().Position = new Vector2(-40, 60);
        rig.Entity.Get<CameraRigComponent>() = new CameraRigComponent(2.5f, 0.1f);

        rig.SnapViewToRig();

        Assert.Equal(new Vector2(-40, 60), view.Position);
        Assert.Equal(2.5f, view.Zoom);
        Assert.Equal(0.1f, view.Rotation);
        Assert.True(CameraRigGlyph.ViewMatchesRig(view.Position, view.Zoom, rig.Position, rig.Zoom));
    }

    // ─────────────────────────── Border-pick + move-drag (one undo step) ───────────────────────────

    [Fact]
    public void RigBorderPick_SelectsTheRig_ThroughTheSamePickPath()
    {
        using var world = new World();
        var view = new GameCamera(800, 600); // rig at (0,0) z1 → frustum TL(-400,-300)..BR(400,300)
        var rig = new EditorCameraRig(world, view);
        using var selection = new SelectionSystem(world, view);
        using var selected = world.GetEntities().With<SelectedComponent>().AsSet();

        // Click ON the frustum's top border (y == -300) — not the fill — with the left button pressed.
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent
        {
            WorldPosition = new Vector2(0, -300),
            VirtualPosition = new Vector2(0, -300),
            LeftButton = true,
            LeftButtonPressed = true,
        });

        selection.Update(Edit());

        Assert.Equal(1, selected.Count);
        Assert.True(rig.Entity.Has<SelectedComponent>());
    }

    [Fact]
    public void RigMoveDrag_IsOneUndoStep_UndoRestores()
    {
        using var world = new World();
        var view = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, view, history);

        var rig = new EditorCameraRig(world, view);
        rig.Entity.Get<TransformComponent>().Position = new Vector2(0, 0);
        rig.Entity.Set(new SelectedComponent()); // border-pick already selected it

        // Press on the move handle at the rig pivot (0,0).
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent
        {
            WorldPosition = Vector2.Zero, VirtualPosition = Vector2.Zero,
            LeftButton = true, LeftButtonPressed = true,
        });
        gizmo.Update(Edit());

        // Drag by (60, 40) across two held frames — the edit applies live, coalesced (no entry yet).
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(30, 20);
        gizmo.Update(Edit());
        input.WorldPosition = new Vector2(60, 40);
        gizmo.Update(Edit());

        Assert.Equal(new Vector2(60, 40), rig.Position); // the rig's OWN transform moved
        Assert.Equal(0, history.Count);                  // still inside the coalescing transaction

        // Release → exactly ONE undo step; undo restores the rig to its start.
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        gizmo.Update(Edit());
        Assert.Equal(1, history.Count);

        history.Undo();
        Assert.Equal(Vector2.Zero, rig.Position);
        history.Redo();
        Assert.Equal(new Vector2(60, 40), rig.Position);
    }

    // ─────────────────────────── Scale-drag edits ZOOM (UX2-G): one undo step, dirties ──────────────

    [Fact]
    public void RigScaleDrag_EditsZoom_NotTransformScale_OneUndoStep_UndoRestores_Dirties()
    {
        using var world = new World();
        var view = new GameCamera(800, 600); // rig at (0,0)
        view.Zoom = 1f;                       // invZoom 1 → the scale handle sits at pivot + (48,-48)
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, view, history);
        Assert.False(history.IsDirty);        // fresh history

        var rig = new EditorCameraRig(world, view);
        rig.Entity.Get<TransformComponent>().Position = Vector2.Zero;
        rig.Entity.Get<CameraRigComponent>() = new CameraRigComponent(2f); // authored zoom 2×
        rig.Entity.Set(new SelectedComponent()); // border-pick / tree-row already selected it

        // The Scale tool is active (UX2-G: Move AND Scale are legal for the rig; Scale routes to zoom).
        var gizmoState = world.CreateEntity();
        gizmoState.Set(new EditorInfrastructureComponent());
        gizmoState.Set(GizmoStateComponent.Default with { Tool = GizmoTool.Scale });

        // Press on the scale handle (pivot + (48,-48) at invZoom 1).
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent
        {
            WorldPosition = new Vector2(48, -48), VirtualPosition = new Vector2(48, -48),
            LeftButton = true, LeftButtonPressed = true,
        });
        gizmo.Update(Edit());

        // Drag right by one ScaleDragUnit → factor 2 → a BIGGER frustum → LOWER zoom = 2 / 2 = 1.
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(48 + GizmoTransform.ScaleDragUnit, -48);
        gizmo.Update(Edit());

        Assert.Equal(1f, rig.Zoom, 3);                                          // zoom halved
        Assert.Equal(Vector2.One, rig.Entity.Get<TransformComponent>().Scale);  // Transform.Scale untouched
        Assert.Equal(0, history.Count);                                         // inside the coalescing txn

        // Release → exactly ONE undo step; undo restores the authored zoom; the edit dirtied the scene.
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        gizmo.Update(Edit());
        Assert.Equal(1, history.Count);
        Assert.True(history.IsDirty); // a zoom edit dirties the scene like any edit

        history.Undo();
        Assert.Equal(2f, rig.Zoom, 3);
        history.Redo();
        Assert.Equal(1f, rig.Zoom, 3);
    }

    [Fact]
    public void RigScaleDrag_ClampsZoomToTheCameraNavRange()
    {
        using var world = new World();
        var view = new GameCamera(800, 600);
        view.Zoom = 1f;
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, view, history);

        var rig = new EditorCameraRig(world, view);
        rig.Entity.Get<TransformComponent>().Position = Vector2.Zero;
        rig.Entity.Get<CameraRigComponent>() = new CameraRigComponent(1f);
        rig.Entity.Set(new SelectedComponent());

        var gizmoState = world.CreateEntity();
        gizmoState.Set(new EditorInfrastructureComponent());
        gizmoState.Set(GizmoStateComponent.Default with { Tool = GizmoTool.Scale });

        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent
        {
            WorldPosition = new Vector2(48, -48), VirtualPosition = new Vector2(48, -48),
            LeftButton = true, LeftButtonPressed = true,
        });
        gizmo.Update(Edit());

        // Drag hugely LEFT → a tiny factor → a very large 1/factor → zoom clamps at the nav MAX (4.0),
        // never runs away past the sane editor range.
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(48 - GizmoTransform.ScaleDragUnit * 100f, -48);
        gizmo.Update(Edit());

        Assert.Equal(4.0f, rig.Zoom, 3); // clamped to CameraNavSystem.DefaultMaxZoom
    }

    // ─────────────────────────── The rig is NOT deletable (loud refusal) ───────────────────────────

    [Fact]
    public void RigDelete_IsRefused_TheRigSurvives_NoCommandPushed()
    {
        using var world = new World();
        var view = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        var rig = new EditorCameraRig(world, view);
        rig.Entity.Set(new SelectedComponent());

        using var commands = new EditorCommandSystem(
            world, history, new SceneSerializer(NewEngineRegistry()),
            deleteRequested: _ => false, undoRequested: _ => false, redoRequested: _ => false, layers: null);

        commands.DeleteSelection(Edit());

        Assert.True(rig.Entity.IsAlive);   // the rig entity is untouched
        Assert.Equal(0, history.Count);    // no DeleteEntityCommand was pushed (undoable-nothing)
    }

    // ─────────────────────────── Rig survives Restart and re-syncs from the file ───────────────────

    [Fact]
    public void RigSurvivesRestart_AndReSyncsFromTheFile()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            var authored = new GameCamera(800, 600) { Position = new Vector2(300, -200) };
            authored.Zoom = 2f;
            WriteScene(fake, content: new Vector2(1275, -530), camera: authored);

            using var world = new World();
            var view = new GameCamera(800, 600);
            var history = new EditorHistory(world);
            var rig = new EditorCameraRig(world, view);
            var transport = new EditorTransport(world, history);
            using var reader = new SceneReaderSystem(world, new SceneSerializer(NewEngineRegistry()),
                content: null, loadTexture: StubTexture, camera: view, applyCameraToRig: rig.SyncFromScene);
            transport.Reload = () => world.Publish(new LoadSceneRequest(SceneFileName, fromContent: false));

            transport.Reload(); // initial load
            Assert.Equal(new Vector2(300, -200), rig.Position);
            var rigEntityBefore = rig.Entity;

            // The designer drags the rig somewhere else (an unsaved edit).
            rig.Entity.Get<TransformComponent>().Position = new Vector2(999, 999);

            transport.Restart(Edit());

            // The rig entity's IDENTITY survives (EditorInfrastructureComponent), and its STATE re-synced
            // from the file — unsaved edits discarded, exactly like every other scene rebuild on Restart.
            Assert.True(rig.Entity.IsAlive);
            Assert.Equal(rigEntityBefore, rig.Entity);
            Assert.Equal(new Vector2(300, -200), rig.Position);
            Assert.Equal(2f, rig.Zoom);
        });
    }

    // ─────────────────────────── Shipped reader (no rig seam) applies scene.camera ─────────────────

    [Fact]
    public void ShippedReader_NoRigSeam_AppliesSceneCameraToTheLiveCamera()
    {
        var fake = new InMemoryPlatformServices();
        WithPlatform(fake, () =>
        {
            var authored = new GameCamera(800, 600) { Position = new Vector2(150, 250) };
            authored.Zoom = 1.5f;
            WriteScene(fake, content: new Vector2(150, 250), camera: authored);

            using var world = new World();
            var camera = new GameCamera(800, 600); // starts at (0,0)
            // NO applyCameraToRig → the shipped path: the live camera IS the authored camera.
            using var reader = new SceneReaderSystem(world, new SceneSerializer(NewEngineRegistry()),
                content: null, loadTexture: StubTexture, camera: camera);

            world.Publish(new LoadSceneRequest(SceneFileName, fromContent: false));

            Assert.Equal(new Vector2(150, 250), camera.Position);
            Assert.Equal(1.5f, camera.Zoom);
        });
    }
}
