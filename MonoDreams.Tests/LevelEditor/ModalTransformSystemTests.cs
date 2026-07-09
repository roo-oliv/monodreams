using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Renderer;
using MonoDreams.State;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the UX3-F <see cref="ModalTransformSystem"/> (design §5): G enters with a selection /
/// no-ops without; mouse motion edits live; axis lock + typed value apply through the system; LMB/Enter
/// commit = ONE undo step that undo fully reverts; RMB/Esc cancel restores the start; the confirm-click
/// over another entity does NOT re-pick (pre-mortem #4); selection is inert while the modal owns the
/// pointer; Escape cancels the modal; and the camera-rig composition (Grab moves the rig, Scale edits
/// zoom, Rotate refused). Systems built headless (an injected empty keyboard seam; a real in-memory world).
/// </summary>
public class ModalTransformSystemTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };
    private static KeyboardState NoKeys() => new();

    private static Entity MakeCursor(World world, Vector2 worldPos)
    {
        var c = world.CreateEntity();
        c.Set(new CursorInputComponent { WorldPosition = worldPos, VirtualPosition = worldPos });
        return c;
    }

    private static void MoveCursor(Entity cursor, Vector2 worldPos)
    {
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.WorldPosition = worldPos;
        input.VirtualPosition = worldPos;
        cursor.NotifyChanged<CursorInputComponent>();
    }

    private static void PressLeft(Entity cursor)
    {
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButton = input.LeftButtonPressed = true;
        cursor.NotifyChanged<CursorInputComponent>();
    }

    private static void PressRight(Entity cursor)
    {
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.RightButton = input.RightButtonPressed = true;
        cursor.NotifyChanged<CursorInputComponent>();
    }

    private static Entity MakeSelected(World world, Vector2 pos)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(pos));
        e.Set(new SelectedComponent());
        return e;
    }

    private static ModalTransformSystem NewModal(World world, EditorHistory history, GameCamera? camera = null) =>
        new(world, camera ?? new GameCamera(800, 600), history, () => NoKeys());

    // ── Enter gating ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Enter_WithSelection_Activates_WithoutSelection_NoOps()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        MakeCursor(world, new Vector2(50, 50));
        var modal = NewModal(world, history);

        Assert.False(modal.Enter(EditorModalMode.Grab, Edit())); // no selection
        Assert.False(modal.IsActive);

        MakeSelected(world, new Vector2(10, 20));
        Assert.True(modal.Enter(EditorModalMode.Grab, Edit()));
        Assert.True(modal.IsActive);
        Assert.True(history.InTransaction); // the coalescing transaction opened
    }

    [Fact]
    public void Enter_InPlay_IsRefused()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        MakeCursor(world, Vector2.Zero);
        MakeSelected(world, Vector2.Zero);
        var modal = NewModal(world, history);

        Assert.False(modal.Enter(EditorModalMode.Grab, Play()));
        Assert.False(modal.IsActive);
    }

    // ── Live edit + commit / cancel ───────────────────────────────────────────────────────────────

    [Fact]
    public void Grab_MouseMotion_EditsLive_LmbCommitsOneUndoStep()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var target = MakeSelected(world, new Vector2(10, 20));
        var cursor = MakeCursor(world, new Vector2(50, 50));
        var modal = NewModal(world, history);

        modal.Enter(EditorModalMode.Grab, Edit()); // entry cursor (50,50)

        MoveCursor(cursor, new Vector2(62, 46.5f)); // delta (12, -3.5)
        modal.Update(Edit());
        Assert.Equal(new Vector2(22f, 16.5f), target.Get<TransformComponent>().Position); // live
        Assert.Equal(0, history.Count); // still in the open transaction

        PressLeft(cursor); // confirm
        modal.Update(Edit());
        Assert.False(modal.IsActive);
        Assert.Equal(new Vector2(22f, 16.5f), target.Get<TransformComponent>().Position);
        Assert.Equal(1, history.Count); // ONE undo step for the whole session

        history.Undo();
        Assert.Equal(new Vector2(10f, 20f), target.Get<TransformComponent>().Position); // fully reverted
    }

    [Fact]
    public void Grab_Rmb_CancelsAndRestoresTheStart()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var target = MakeSelected(world, new Vector2(10, 20));
        var cursor = MakeCursor(world, new Vector2(50, 50));
        var modal = NewModal(world, history);

        modal.Enter(EditorModalMode.Grab, Edit());
        MoveCursor(cursor, new Vector2(90, 90));
        modal.Update(Edit()); // moved live

        PressRight(cursor);
        modal.Update(Edit()); // cancel
        Assert.False(modal.IsActive);
        Assert.Equal(new Vector2(10f, 20f), target.Get<TransformComponent>().Position); // start restored
        Assert.Equal(0, history.Count); // no undo entry recorded
    }

    [Fact]
    public void Escape_CancelsTheModal()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var target = MakeSelected(world, new Vector2(10, 20));
        var cursor = MakeCursor(world, new Vector2(50, 50));
        var keys = new[] { NoKeys() };
        var modal = new ModalTransformSystem(world, new GameCamera(800, 600), history, () => keys[0]);

        modal.Enter(EditorModalMode.Grab, Edit());
        MoveCursor(cursor, new Vector2(90, 90));
        modal.Update(Edit());

        keys[0] = new KeyboardState(Keys.Escape);
        modal.Update(Edit());
        Assert.False(modal.IsActive);
        Assert.Equal(new Vector2(10f, 20f), target.Get<TransformComponent>().Position);
    }

    // ── Axis lock + typed value through the system ─────────────────────────────────────────────────

    [Fact]
    public void AxisLock_ConstrainsTheGrab()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var target = MakeSelected(world, new Vector2(10, 20));
        var cursor = MakeCursor(world, new Vector2(50, 50));
        var modal = NewModal(world, history);

        modal.Enter(EditorModalMode.Grab, Edit());
        modal.SetAxis(ModalAxis.X);
        MoveCursor(cursor, new Vector2(62, 46.5f)); // (12, -3.5)
        modal.Update(Edit());
        Assert.Equal(new Vector2(22f, 20f), target.Get<TransformComponent>().Position); // Y frozen
    }

    [Fact]
    public void TypedValue_AppliesExactly_AlongTheLockedAxis()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var target = MakeSelected(world, new Vector2(10, 20));
        var cursor = MakeCursor(world, new Vector2(50, 50));
        var modal = NewModal(world, history);

        modal.Enter(EditorModalMode.Grab, Edit());
        modal.SetAxis(ModalAxis.X);
        modal.TypeDigits("24");
        MoveCursor(cursor, new Vector2(999, 999)); // mouse ignored when a value is typed
        modal.Update(Edit());
        Assert.Equal(new Vector2(34f, 20f), target.Get<TransformComponent>().Position); // 10 + 24
    }

    [Fact]
    public void OpCursor_DrivesTheLiveEdit_Headlessly()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var target = MakeSelected(world, new Vector2(10, 20));
        MakeCursor(world, new Vector2(50, 50));
        var modal = NewModal(world, history);

        modal.Enter(EditorModalMode.Grab, Edit());
        modal.OpCursor(12f, -3.5f); // motion from the entry cursor, applied immediately
        modal.Confirm(Edit());
        Assert.Equal(new Vector2(22f, 16.5f), target.Get<TransformComponent>().Position);
        Assert.Equal(1, history.Count);
    }

    // ── Pre-mortem #4: the confirm-click over another entity does NOT re-pick ─────────────────────

    private static Entity MakeSprite(World world, Vector2 pos, float depth)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(pos));
        e.Set(new SpriteInfoComponent
        {
            Source = new Rectangle(0, 0, 40, 40),
            Size = new Vector2(40, 40),
            Target = RenderTargetID.Main,
        });
        e.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main, LayerDepth = depth });
        e.Set(new VisibleComponent());
        return e;
    }

    [Fact]
    public void ConfirmClickOverAnotherEntity_DoesNotRepick_NorClear()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        world.CreateEntity().Set(GizmoStateComponent.Default); // SelectTransform, snap off

        var a = MakeSprite(world, new Vector2(100, 100), 0.5f);
        a.Set(new SelectedComponent());
        var b = MakeSprite(world, new Vector2(300, 100), 0.9f); // frontmost; the confirm click lands over it

        var cursor = MakeCursor(world, new Vector2(100, 100)); // grab starts over A
        var modal = NewModal(world, history, camera);
        using var selection = new SelectionSystem(world, camera);

        modal.Enter(EditorModalMode.Grab, Edit());

        // Move A toward B, then LMB-press exactly over B (which sits under the cursor). The modal must
        // consume the press so the same-frame selection pass never re-picks B or clears A.
        MoveCursor(cursor, new Vector2(300, 100));
        PressLeft(cursor);
        modal.Update(Edit());     // update pipeline: applies + consumes + confirms
        selection.Update(Edit()); // draw pipeline (same frame): sees the consumed press

        Assert.True(a.Has<SelectedComponent>());   // A stayed selected
        Assert.False(b.Has<SelectedComponent>());  // B was NOT re-picked
        Assert.False(cursor.Get<CursorInputComponent>().LeftButtonPressed); // the press was consumed
    }

    // ── The camera rig composes (UX2-G mapping) ────────────────────────────────────────────────────

    private static Entity MakeRig(World world, Vector2 pos, float zoom)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(pos));
        e.Set(new CameraRigComponent(zoom, 0f));
        e.Set(new SelectedComponent());
        return e;
    }

    [Fact]
    public void Rig_Grab_MovesTheRigTransform_OneUndoStep()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var rig = MakeRig(world, Vector2.Zero, zoom: 1f);
        var cursor = MakeCursor(world, new Vector2(50, 50));
        var modal = NewModal(world, history);

        modal.Enter(EditorModalMode.Grab, Edit());
        MoveCursor(cursor, new Vector2(62, 46.5f));
        PressLeft(cursor);
        modal.Update(Edit());

        Assert.Equal(new Vector2(12f, -3.5f), rig.Get<TransformComponent>().Position);
        Assert.Equal(1f, rig.Get<CameraRigComponent>().Zoom); // zoom untouched by Grab
        Assert.Equal(1, history.Count);
    }

    [Fact]
    public void Rig_Scale_EditsZoom_NotTransformScale_OneUndoStep_UndoRestores()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var rig = MakeRig(world, Vector2.Zero, zoom: 1f);
        var cursor = MakeCursor(world, new Vector2(10, 0)); // 10 units from the pivot
        var modal = NewModal(world, history);

        modal.Enter(EditorModalMode.Scale, Edit());
        MoveCursor(cursor, new Vector2(20, 0)); // 20 units out → factor 2 → zoom 1/2 = 0.5
        modal.Update(Edit());
        Assert.Equal(0.5f, rig.Get<CameraRigComponent>().Zoom, 3);
        Assert.Equal(Vector2.One, rig.Get<TransformComponent>().Scale); // Transform.Scale untouched

        PressLeft(cursor);
        modal.Update(Edit());
        Assert.Equal(1, history.Count);
        history.Undo();
        Assert.Equal(1f, rig.Get<CameraRigComponent>().Zoom, 3);
    }

    [Fact]
    public void Rig_Rotate_IsRefused()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        MakeRig(world, Vector2.Zero, zoom: 1f);
        MakeCursor(world, new Vector2(10, 0));
        var modal = NewModal(world, history);

        Assert.False(modal.Enter(EditorModalMode.Rotate, Edit()));
        Assert.False(modal.IsActive);
        Assert.False(history.InTransaction);
    }
}
