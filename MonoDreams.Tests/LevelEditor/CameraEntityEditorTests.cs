using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Renderer;
using MonoDreams.State;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the CM-B <b>camera-entity editor</b> invariants (the rig is gone; the camera is an ordinary
/// scene entity the editor visualizes + edits — level-editor premise "The editor visualizes + edits the
/// scene camera ENTITY"):
/// <list type="bullet">
///   <item>the frustum glyph is emitted from the camera ENTITY (WorldPosition + <c>CameraComponent.Zoom</c>)
///   while the free VIEW differs from it, hidden in Play / when the view matches;</item>
///   <item><c>view:camera</c> snaps the VIEW onto the camera entity's state;</item>
///   <item>the camera entity border-picks on its frustum; <c>G</c> moves it (one undo step); <c>S</c>
///   edits <c>CameraComponent.Zoom</c> via the standard <see cref="MemberEditCommand"/> (bigger frustum ⇒
///   lower zoom, clamped); <c>R</c> is LEGAL now (rotates its Transform);</item>
///   <item>deleting the LAST camera entity is refused (a scene needs a camera).</item>
/// </list>
/// Pure logic — hand-built worlds, no GraphicsDevice. (The save→load→save persistence of a zoom edit is
/// the acceptance test in <c>CameraZoomEditPersistsTests</c>.)
/// </summary>
public class CameraEntityEditorTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    private static ComponentSerializerRegistry NewEngineRegistry()
    {
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        return registry;
    }

    /// <summary>An ordinary <c>core.Camera</c> scene entity (CM): EntityInfo "Camera" + Transform +
    /// CameraComponent + SceneObjectComponent (a scene root, so it serializes + border-picks).</summary>
    private static Entity CameraEntity(World world, Vector2 pos, float zoom = 1f, float rotation = 0f)
    {
        var e = world.CreateEntity();
        e.Set(new SceneObjectComponent());
        e.Set(new EntityInfoComponent("Camera"));
        e.Set(new TransformComponent(pos, rotation));
        e.Set(new CameraComponent { Zoom = zoom });
        return e;
    }

    // ─────────────────────────── CameraEntityGlyph pure math ───────────────────────────

    [Fact]
    public void FrustumWorldCorners_AreVirtualSizeOverZoom_CenteredOnTheCamera()
    {
        // center (100,50), zoom 2, virtual 800×600 → half extents 200×150.
        var c = CameraEntityGlyph.FrustumWorldCorners(new Vector2(100, 50), zoom: 2f, virtualWidth: 800, virtualHeight: 600);
        Assert.Equal(new Vector2(-100, -100), c[0]); // TL
        Assert.Equal(new Vector2(300, -100), c[1]);  // TR
        Assert.Equal(new Vector2(300, 200), c[2]);   // BR
        Assert.Equal(new Vector2(-100, 200), c[3]);  // BL
    }

    [Fact]
    public void FrustumWorldCorners_NonPositiveZoom_DegradesToOne()
    {
        var c = CameraEntityGlyph.FrustumWorldCorners(Vector2.Zero, zoom: 0f, virtualWidth: 800, virtualHeight: 600);
        Assert.Equal(new Vector2(-400, -300), c[0]); // as if zoom 1
        Assert.Equal(new Vector2(400, 300), c[2]);
    }

    [Fact]
    public void ViewMatchesCamera_WithinEpsilon_True_BeyondEpsilon_False()
    {
        var camPos = new Vector2(100, 100);
        // Position: 0.4 away (< 0.5 epsilon) matches; 0.6 away un-matches.
        Assert.True(CameraEntityGlyph.ViewMatchesCamera(new Vector2(100.4f, 100f), 1f, camPos, 1f));
        Assert.False(CameraEntityGlyph.ViewMatchesCamera(new Vector2(100.6f, 100f), 1f, camPos, 1f));
        // Zoom: 0.0005 away (< 1e-3) matches; 0.002 away un-matches.
        Assert.True(CameraEntityGlyph.ViewMatchesCamera(camPos, 1.0005f, camPos, 1f));
        Assert.False(CameraEntityGlyph.ViewMatchesCamera(camPos, 1.002f, camPos, 1f));
    }

    // ─────────────────── Membership: the camera entity IS scene content now (CM) ───────────────────

    [Fact]
    public void CameraEntity_IsSceneMembership()
    {
        using var world = new World();
        // The camera is an ordinary SceneObjectComponent root now (unlike the old rig, which was excluded).
        var camera = CameraEntity(world, Vector2.Zero);

        var members = SceneWriter.CollectMembership(world);
        Assert.Contains(camera, members); // rides entities[] like everything else (CM)
    }

    // ─────────────────────────── Glyph visibility (epsilon-gated, Edit-only) ───────────────────────

    [Fact]
    public void Glyph_HiddenWhenViewMatchesCamera_And_InPlay_ShownWhenViewDiffers()
    {
        using var world = new World();
        var view = new GameCamera(800, 600);
        CameraEntity(world, Vector2.Zero); // camera at (0,0), zoom 1
        var overlay = new CameraEntityOverlay(world, view);
        ref readonly var draw = ref overlay.GlyphEntity.Get<DrawComponent>();

        // View == camera (both at (0,0), zoom 1) → "you ARE the camera" → glyph parked (empty mesh).
        overlay.EmitGlyph(Edit());
        Assert.Empty(draw.Vertices);

        // View navigated a little away (beyond the epsilon, frustum still overlaps the viewport) → glyph
        // shows the frustum (non-empty mesh) in Edit.
        view.Position = new Vector2(10, 0);
        overlay.EmitGlyph(Edit());
        Assert.NotEmpty(draw.Vertices);

        // In Play the glyph never draws (editing chrome), even with the view off the camera.
        overlay.EmitGlyph(Play());
        Assert.Empty(draw.Vertices);
    }

    [Fact]
    public void Glyph_NoCameraEntity_IsInert()
    {
        // A prefab context has no camera entity → the emitter finds none → the glyph stays parked.
        using var world = new World();
        var view = new GameCamera(800, 600) { Position = new Vector2(50, 50) };
        var overlay = new CameraEntityOverlay(world, view);

        overlay.EmitGlyph(Edit());
        Assert.Empty(overlay.GlyphEntity.Get<DrawComponent>().Vertices);
    }

    [Fact]
    public void Glyph_DprAndInsetProjection_ClipsToTheGameViewport()
    {
        using var world = new World();
        var view = new GameCamera(800, 600);
        var vm = new ViewportManager(null, 800, 600) { ScreenWidth = 1600, ScreenHeight = 900, DevicePixelRatio = 2f };
        vm.SetViewportInset(240, 84, 280, 168); // Blender-style chrome margins (device pixels)
        CameraEntity(world, Vector2.Zero, zoom: 0.8f); // zoomed out → frustum exceeds the viewport
        var overlay = new CameraEntityOverlay(world, view, vm);

        view.Position = new Vector2(60, 40); // view ≠ camera → glyph shows, spills past the viewport

        overlay.EmitGlyph(Edit());
        var draw = overlay.GlyphEntity.Get<DrawComponent>();
        Assert.NotEmpty(draw.Vertices);

        var dest = vm.DestinationRectangle;
        foreach (var v in draw.Vertices)
        {
            Assert.InRange(v.Position.X, (float)dest.Left, (float)dest.Right);
            Assert.InRange(v.Position.Y, (float)dest.Top, (float)dest.Bottom);
        }
    }

    // ─────────────────────────── view:camera snaps the view onto the camera entity ─────────────────

    [Fact]
    public void SnapViewToCameraEntity_CopiesCameraStateOntoTheView()
    {
        using var world = new World();
        var view = new GameCamera(800, 600) { Position = new Vector2(1000, 2000) };
        view.Zoom = 0.5f;
        CameraEntity(world, new Vector2(-40, 60), zoom: 2.5f, rotation: 0.1f);
        var overlay = new CameraEntityOverlay(world, view);

        overlay.SnapViewToCameraEntity();

        Assert.Equal(new Vector2(-40, 60), view.Position);
        Assert.Equal(2.5f, view.Zoom);
        Assert.Equal(0.1f, view.Rotation);
        Assert.True(CameraEntityGlyph.ViewMatchesCamera(view.Position, view.Zoom, new Vector2(-40, 60), 2.5f));
    }

    // ─────────────────────────── Border-pick + move-drag (one undo step) ────────────────────────────

    [Fact]
    public void CameraBorderPick_SelectsTheCameraEntity_ThroughTheSamePickPath()
    {
        using var world = new World();
        var view = new GameCamera(800, 600); // camera at (0,0) z1 → frustum TL(-400,-300)..BR(400,300)
        var camera = CameraEntity(world, Vector2.Zero);
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
        Assert.True(camera.Has<SelectedComponent>());
    }

    [Fact]
    public void CameraMoveDrag_IsOneUndoStep_UndoRestores()
    {
        using var world = new World();
        var view = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, view, history);

        var camera = CameraEntity(world, Vector2.Zero);
        camera.Set(new SelectedComponent()); // border-pick already selected it

        // Press on the move handle at the camera pivot (0,0).
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent
        {
            WorldPosition = Vector2.Zero, VirtualPosition = Vector2.Zero,
            LeftButton = true, LeftButtonPressed = true,
        });
        gizmo.Update(Edit());

        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(30, 20);
        gizmo.Update(Edit());
        input.WorldPosition = new Vector2(60, 40);
        gizmo.Update(Edit());

        Assert.Equal(new Vector2(60, 40), camera.Get<TransformComponent>().Position);
        Assert.Equal(0, history.Count);

        input.LeftButton = false;
        input.LeftButtonReleased = true;
        gizmo.Update(Edit());
        Assert.Equal(1, history.Count);

        history.Undo();
        Assert.Equal(Vector2.Zero, camera.Get<TransformComponent>().Position);
        history.Redo();
        Assert.Equal(new Vector2(60, 40), camera.Get<TransformComponent>().Position);
    }

    // ─────────────────────────── Scale-drag edits ZOOM via MemberEditCommand ────────────────────────

    [Fact]
    public void CameraScaleDrag_EditsZoom_NotTransformScale_OneUndoStep_UndoRestores_Dirties()
    {
        using var world = new World();
        var view = new GameCamera(800, 600); // camera at (0,0)
        view.Zoom = 1f;                       // invZoom 1 → the scale handle sits at pivot + (48,-48)
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, view, history);
        Assert.False(history.IsDirty);        // fresh history

        var camera = CameraEntity(world, Vector2.Zero, zoom: 2f); // authored zoom 2×
        camera.Set(new SelectedComponent());

        // The Scale tool is active (CM: Move / Rotate / Scale are all legal; Scale routes to zoom).
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

        Assert.Equal(1f, camera.Get<CameraComponent>().Zoom, 3);                 // zoom halved
        Assert.Equal(Vector2.One, camera.Get<TransformComponent>().Scale);       // Transform.Scale untouched
        Assert.Equal(0, history.Count);                                          // inside the coalescing txn

        input.LeftButton = false;
        input.LeftButtonReleased = true;
        gizmo.Update(Edit());
        Assert.Equal(1, history.Count);
        Assert.True(history.IsDirty); // a zoom edit dirties the scene like any edit

        history.Undo();
        Assert.Equal(2f, camera.Get<CameraComponent>().Zoom, 3);
        history.Redo();
        Assert.Equal(1f, camera.Get<CameraComponent>().Zoom, 3);
    }

    [Fact]
    public void CameraScaleDrag_ClampsZoomToTheCameraNavRange()
    {
        using var world = new World();
        var view = new GameCamera(800, 600);
        view.Zoom = 1f;
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, view, history);

        var camera = CameraEntity(world, Vector2.Zero, zoom: 1f);
        camera.Set(new SelectedComponent());

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

        // Drag hugely LEFT → a tiny factor → a very large 1/factor → zoom clamps at the nav MAX (4.0).
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(48 - GizmoTransform.ScaleDragUnit * 100f, -48);
        gizmo.Update(Edit());

        Assert.Equal(4.0f, camera.Get<CameraComponent>().Zoom, 3); // clamped to CameraNavSystem.DefaultMaxZoom
    }

    // ─────────────────────────── Rotate is LEGAL on the camera entity (CM, pre-mortem #1) ───────────

    [Fact]
    public void CameraRotate_IsLegal_RotatesTheTransform_OneUndoStep()
    {
        using var world = new World();
        var view = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, view, history);

        var camera = CameraEntity(world, Vector2.Zero);
        camera.Set(new SelectedComponent());

        var gizmoState = world.CreateEntity();
        gizmoState.Set(new EditorInfrastructureComponent());
        gizmoState.Set(GizmoStateComponent.Default with { Tool = GizmoTool.Rotate });

        // Press on the rotate ring (40px at invZoom 1 → world point (40,0)).
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent
        {
            WorldPosition = new Vector2(40, 0), VirtualPosition = new Vector2(40, 0),
            LeftButton = true, LeftButtonPressed = true,
        });
        gizmo.Update(Edit());

        // Sweep the cursor a quarter-turn (to (0,40)) → the Transform rotates (NOT refused as it was for the rig).
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(0, 40);
        gizmo.Update(Edit());

        Assert.NotEqual(0f, camera.Get<TransformComponent>().Rotation); // R is legal now — one rotation on the Transform

        input.LeftButton = false;
        input.LeftButtonReleased = true;
        gizmo.Update(Edit());
        Assert.Equal(1, history.Count); // one undo step

        history.Undo();
        Assert.Equal(0f, camera.Get<TransformComponent>().Rotation, 4);
    }

    [Fact]
    public void ModalRotate_OnTheCamera_IsAccepted()
    {
        using var world = new World();
        var view = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var modal = new ModalTransformSystem(world, view, history,
            getKeyboardState: () => default);

        var camera = CameraEntity(world, Vector2.Zero);
        camera.Set(new SelectedComponent());

        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent { WorldPosition = new Vector2(50, 0) });

        // R on the camera entity enters (legal now) — the old rig refused it.
        Assert.True(modal.Enter(EditorModalMode.Rotate, Edit()));
        Assert.True(modal.IsActive);
        modal.Cancel(Edit());
    }

    // ─────────────────────────── Delete the LAST camera is refused ──────────────────────────────────

    [Fact]
    public void DeleteLastCamera_IsRefused_TheCameraSurvives_NoCommandPushed()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var camera = CameraEntity(world, Vector2.Zero);
        camera.Set(new SelectedComponent());

        using var commands = new EditorCommandSystem(
            world, history, new SceneSerializer(NewEngineRegistry()), layers: null);

        commands.DeleteSelection(Edit());

        Assert.True(camera.IsAlive);    // the only camera is untouched
        Assert.Equal(0, history.Count); // no DeleteEntityCommand was pushed
    }

    [Fact]
    public void DeleteCamera_WhenAnotherExists_IsAllowed()
    {
        // The guard refuses only the LAST camera — a second one may be deleted (the writer's one-camera
        // rule keeps two from ever persisting, so in practice this just leaves a scene with one).
        using var world = new World();
        var history = new EditorHistory(world);
        var camA = CameraEntity(world, Vector2.Zero);
        CameraEntity(world, new Vector2(10, 10));
        camA.Set(new SelectedComponent());

        using var commands = new EditorCommandSystem(
            world, history, new SceneSerializer(NewEngineRegistry()), layers: null);

        commands.DeleteSelection(Edit());

        Assert.False(camA.IsAlive);     // deleted (another camera remains)
        Assert.Equal(1, history.Count); // a delete command was pushed
    }
}
