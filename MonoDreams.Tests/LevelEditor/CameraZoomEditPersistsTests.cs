using System.Linq;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Message;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// THE acceptance test — the user's original bug, repro-first (the third camera defect in three days:
/// a zoom edit that never reached the persisted camera state). Now the camera is a scene ENTITY, so a
/// Scale gesture (gizmo AND modal S) edits <see cref="CameraComponent.Zoom"/> through the standard
/// <see cref="MemberEditCommand"/>, the Inspector reflects it, Save writes it into <c>entities[]</c>,
/// <c>save → load → save</c> is a byte fixed point, and it survives a tab-switch (Game-mode) round-trip.
/// Lineage: this is the "if the Inspector shows it, Save persists it; if Save persists it, the round-trip
/// owns it" tenet, applied to the camera.
/// </summary>
public class CameraZoomEditPersistsTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };

    private static ComponentSerializerRegistry Registry()
    {
        var r = new ComponentSerializerRegistry();
        r.RegisterEngineComponents();
        return r;
    }

    private static Entity CameraEntity(World world, Vector2 pos, float zoom)
    {
        var e = world.CreateEntity();
        e.Set(new SceneObjectComponent());
        e.Set(new EntityInfoComponent("Camera"));
        e.Set(new TransformComponent(pos));
        e.Set(new CameraComponent { Zoom = zoom });
        return e;
    }

    /// <summary>The zoom the file's camera entry carries — the "Save persists it" assertion.</summary>
    private static float SavedZoom(World world)
    {
        var scene = new SceneWriter(new SceneSerializer(Registry())).BuildScene(world);
        var cam = scene.Entities.Single(e => e.Components.ContainsKey(EngineComponentSerializers.CameraKey));
        return cam.Components[EngineComponentSerializers.CameraKey].GetProperty("zoom").GetSingle();
    }

    /// <summary>The zoom the Inspector shows — read through the SAME reflection the editable Inspector
    /// uses to display + edit a member (PF-A).</summary>
    private static float InspectorZoom(Entity camera)
    {
        Assert.True(MemberEditCommand.TryReadMember(
            camera, typeof(CameraComponent), nameof(CameraComponent.Zoom), out var boxed));
        return (float)boxed!;
    }

    // ─────────────── Gizmo Scale drag → Zoom edits, Inspector-visible, persisted ───────────────

    [Fact]
    public void GizmoScaleDrag_EditsZoom_InspectorVisible_AndSavesIntoEntities()
    {
        using var world = new World();
        var view = new GameCamera(800, 600) { Zoom = 1f };
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, view, history);

        var camera = CameraEntity(world, Vector2.Zero, zoom: 2f);
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

        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(48 + GizmoTransform.ScaleDragUnit, -48); // factor 2 → zoom 2/2 = 1
        gizmo.Update(Edit());
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        gizmo.Update(Edit());

        Assert.Equal(1f, camera.Get<CameraComponent>().Zoom, 3); // the component value changed
        Assert.Equal(1f, InspectorZoom(camera), 3);              // the Inspector-visible value changed
        Assert.Equal(1f, SavedZoom(world), 3);                  // Save writes it into entities[]
    }

    // ─────────────── Modal S → Zoom edits, persisted ───────────────

    [Fact]
    public void ModalScale_EditsZoom_AndSavesIntoEntities()
    {
        using var world = new World();
        var view = new GameCamera(800, 600) { Zoom = 1f };
        var history = new EditorHistory(world);
        using var modal = new ModalTransformSystem(world, view, history, getKeyboardState: () => default);

        var camera = CameraEntity(world, Vector2.Zero, zoom: 2f);
        camera.Set(new SelectedComponent());

        // The entry cursor sits off the pivot (100,0) so the scale factor is well-defined.
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent { WorldPosition = new Vector2(100, 0) });

        Assert.True(modal.Enter(EditorModalMode.Scale, Edit()));
        modal.OpCursor(100, 0);   // cursor → (200,0): factor |200|/|100| = 2 → zoom 2/2 = 1
        modal.Confirm(Edit());

        Assert.Equal(1f, camera.Get<CameraComponent>().Zoom, 3);
        Assert.Equal(1f, InspectorZoom(camera), 3);
        Assert.Equal(1f, SavedZoom(world), 3);
        Assert.Equal(1, history.Count); // one modal session = one undo step
    }

    // ─────────────── save → load → save is a byte fixed point after a zoom edit ───────────────

    [Fact]
    public void ZoomEdit_SaveLoadSave_IsByteFixedPoint()
    {
        using var world1 = new World();
        var camera = CameraEntity(world1, new Vector2(208, 108), zoom: 2f);
        camera.Get<CameraComponent>().Zoom = 1.75f; // the edit

        var json1 = CanonicalJson.Serialize(new SceneWriter(new SceneSerializer(Registry())).BuildScene(world1));

        var scene = CanonicalJson.Deserialize<SceneData>(json1)!;
        using var world2 = new World();
        using var reader = new SceneReaderSystem(world2, new SceneSerializer(Registry()), content: null!,
            loadTexture: _ => null!, ensureSingleCamera: true); // idempotent — a camera is present
        world2.Publish(new LoadSceneRequest(scene));

        var json2 = CanonicalJson.Serialize(new SceneWriter(new SceneSerializer(Registry())).BuildScene(world2));

        Assert.Equal(json1, json2);                 // save → load → save is a byte fixed point
        Assert.Contains("1.75", json1);             // the edited zoom is in the bytes
        using var cams = world2.GetEntities().With<CameraComponent>().AsSet();
        Assert.Equal(1.75f, cams.GetEntities().ToArray()[0].Get<CameraComponent>().Zoom, 3);
    }

    // ─────────────── the zoom survives a tab-switch (Game-mode) round-trip ───────────────

    [Fact]
    public void ZoomEdit_SurvivesTabSwitchRoundTrip_AndSandboxChurnDoesNotLeak()
    {
        using var world = new World();
        var registry = Registry();
        var serializer = new SceneSerializer(registry);
        var view = new GameCamera(800, 600);
        using var reader = new SceneReaderSystem(world, serializer, content: null!,
            loadTexture: _ => null!, camera: view, ensureSingleCamera: true);
        var history = new EditorHistory(world);
        var transport = new EditorTransport(world, history) { Reload = () => { } };
        transport.CaptureSnapshot = () => new SceneWriter(serializer).BuildScene(world, layers: null);
        transport.RestoreSnapshot = snapshot => world.Publish(new LoadSceneRequest(snapshot));
        transport.CaptureView = () => new CameraViewSnapshot(view.Position, view.Zoom, view.Rotation);
        transport.RestoreView = v => { view.Position = v.Position; view.Zoom = v.Zoom; view.Rotation = v.Rotation; };
        transport.SnapViewToCameraEntity = () => { };

        var camera = CameraEntity(world, Vector2.Zero, zoom: 2f);
        camera.Get<CameraComponent>().Zoom = 1.5f; // the scene-tab edit (captured in the snapshot)

        transport.EnterGameMode(Edit()); // snapshots the scene (incl. zoom 1.5)

        // In the sandbox, "Play" churns the camera zoom (as a live effect would) — must NOT leak.
        CameraOf(world).Get<CameraComponent>().Zoom = 9f;

        transport.ExitToSceneMode(Edit()); // restore the scene from the snapshot

        var restored = CameraOf(world);
        Assert.Equal(1.5f, restored.Get<CameraComponent>().Zoom, 3); // the edit survived the round-trip
        using var cams = world.GetEntities().With<CameraComponent>().AsSet();
        Assert.Single(cams.GetEntities().ToArray());                 // still exactly one camera
    }

    private static Entity CameraOf(World world)
    {
        using var set = world.GetEntities().With<CameraComponent>().AsSet();
        foreach (var e in set.GetEntities()) return e;
        return default;
    }
}
