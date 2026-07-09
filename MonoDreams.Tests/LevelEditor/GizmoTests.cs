using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the Wave 4b gizmo invariants: the transform math (move / rotate / scale with snap off =
/// raw delta, snap on = quantized, rotate/scale honoring Origin) and drag-coalescing (one gizmo drag
/// of N intermediate edits = exactly ONE undo step that a single undo reverses to the pre-drag
/// transform). Pure logic — the math is tested directly on <see cref="GizmoTransform"/>, and the
/// coalescing is tested by driving the SAME <see cref="EditorHistory"/> transaction API +
/// <see cref="TransformEditCommand"/> the gizmo uses, over an in-memory world (no GraphicsDevice, no
/// cursor).
///
/// Names the live premises "Bounded undo with drag-coalescing" (<c>DragCoalescingTest</c>) and the
/// new gizmo snap premise (<c>GizmoTransformSnapTest</c>).
/// </summary>
public class GizmoTests
{
    private const float Tol = 1e-4f;

    // ---- GizmoTransformSnapTest: move/rotate/scale, snap off (raw) vs on (quantized), Origin honored ----

    [Fact]
    public void GizmoTransformSnapTest()
    {
        var beforePos = new Vector2(10f, 20f);
        var beforeRot = 0.5f;
        var beforeScale = new Vector2(2f, 3f);
        var beforeOrigin = new Vector2(7f, 8f);
        var pivot = new Vector2(100f, 100f);

        // ---- MOVE, snap OFF: raw delta applied; rotation/scale/origin untouched ----
        {
            var start = new Vector2(0f, 0f);
            var current = new Vector2(13f, -27f); // delta = (13, -27)
            var (pos, rot, scale, origin) = GizmoTransform.Compute(
                GizmoTool.Move, beforePos, beforeRot, beforeScale, beforeOrigin,
                pivot, start, current, snapStep: 0f, rotationSnapStep: 0f);

            Assert.Equal(beforePos + new Vector2(13f, -27f), pos);
            Assert.Equal(beforeRot, rot);
            Assert.Equal(beforeScale, scale);
            Assert.Equal(beforeOrigin, origin); // Origin preserved
        }

        // ---- MOVE, snap ON (step 16): result quantized to the grid ----
        {
            var start = Vector2.Zero;
            var current = new Vector2(13f, -27f); // before+delta = (23, -7) → snap16 → (16, 0)... rounded
            var (pos, _, _, origin) = GizmoTransform.Compute(
                GizmoTool.Move, beforePos, beforeRot, beforeScale, beforeOrigin,
                pivot, start, current, snapStep: 16f, rotationSnapStep: 0f);

            // (10+13, 20-27) = (23, -7) → nearest multiple of 16 = (16, -0) → (16, 0).
            Assert.Equal(16f, pos.X, Tol);
            Assert.Equal(0f, pos.Y, Tol);
            Assert.Equal(beforeOrigin, origin);
        }

        // ---- ROTATE, snap OFF: rotation increases by the swept angle about the pivot; Origin honored ----
        {
            // Start ray points +X from pivot; current ray points +Y → +90° (π/2) sweep.
            var start = pivot + new Vector2(40f, 0f);
            var current = pivot + new Vector2(0f, 40f);
            var (pos, rot, scale, origin) = GizmoTransform.Compute(
                GizmoTool.Rotate, beforePos, beforeRot, beforeScale, beforeOrigin,
                pivot, start, current, snapStep: 0f, rotationSnapStep: 0f);

            Assert.Equal(beforeRot + MathHelper.PiOver2, rot, 3);
            Assert.Equal(beforePos, pos);       // rotate does not move position
            Assert.Equal(beforeScale, scale);
            Assert.Equal(beforeOrigin, origin); // rotate pivots about the origin; the Origin field is preserved
        }

        // ---- ROTATE, snap ON (step = 15°): swept angle snapped to the nearest step ----
        {
            var step = MathHelper.ToRadians(15f);
            // Sweep ~40° from +X; before rotation 0 → snapped result is nearest multiple of 15° = 45°.
            var start = pivot + new Vector2(40f, 0f);
            var current = pivot + new Vector2(40f * 0.766f, 40f * 0.643f); // ~40°
            var (_, rot, _, _) = GizmoTransform.Compute(
                GizmoTool.Rotate, beforePos, beforeRotation: 0f, beforeScale, beforeOrigin,
                pivot, start, current, snapStep: 0f, rotationSnapStep: step);

            Assert.Equal(MathHelper.ToRadians(45f), rot, 3);
        }

        // ---- SCALE, snap OFF: uniform factor from the X drag distance; Origin honored ----
        {
            var start = Vector2.Zero;
            var current = new Vector2(GizmoTransform.ScaleDragUnit, 0f); // +1 unit drag → factor 2
            var (pos, rot, scale, origin) = GizmoTransform.Compute(
                GizmoTool.Scale, beforePos, beforeRot, beforeScale, beforeOrigin,
                pivot, start, current, snapStep: 0f, rotationSnapStep: 0f);

            Assert.Equal(beforeScale * 2f, scale);
            Assert.Equal(beforePos, pos);
            Assert.Equal(beforeRot, rot);
            Assert.Equal(beforeOrigin, origin); // scale pivots about the origin; the Origin field is preserved
        }

        // ---- SCALE, snap ON: resulting scale quantized to whole steps ----
        {
            var start = Vector2.Zero;
            var current = new Vector2(GizmoTransform.ScaleDragUnit * 0.6f, 0f); // factor 1.6
            // before (2,3) * 1.6 = (3.2, 4.8) → snap to step 1 → (3, 5).
            var (_, _, scale, _) = GizmoTransform.Compute(
                GizmoTool.Scale, beforePos, beforeRot, beforeScale, beforeOrigin,
                pivot, start, current, snapStep: 1f, rotationSnapStep: 0f);

            Assert.Equal(3f, scale.X, Tol);
            Assert.Equal(5f, scale.Y, Tol);
        }
    }

    // ---- ScaleFactor: the pure drag→factor mapping shared by sprite scale AND rig zoom (UX2-G) ----
    [Fact]
    public void ScaleFactor_MapsDragXToAUniformFactor_FlooredAboveZero()
    {
        // 1 + dx/ScaleDragUnit, floored at MinScaleFactor so it never hits zero/negative — the camera
        // rig's zoom-drag divides by this factor (a bigger frustum ⇒ a lower zoom), so a zero/negative
        // factor would blow up or invert the zoom.
        Assert.Equal(1f, GizmoTransform.ScaleFactor(Vector2.Zero), Tol);                                   // no drag
        Assert.Equal(2f, GizmoTransform.ScaleFactor(new Vector2(GizmoTransform.ScaleDragUnit, 0f)), Tol);  // +1 unit → ×2
        Assert.Equal(1.5f, GizmoTransform.ScaleFactor(new Vector2(GizmoTransform.ScaleDragUnit * 0.5f, 0f)), Tol);
        Assert.Equal(GizmoTransform.MinScaleFactor,
            GizmoTransform.ScaleFactor(new Vector2(-GizmoTransform.ScaleDragUnit * 100f, 0f)), Tol);
    }

    // ---- DragCoalescingTest: one gizmo drag of N intermediate edits = ONE undo step ----

    [Fact]
    public void DragCoalescingTest()
    {
        using var world = new World();
        var history = new EditorHistory(world);

        // The entity being dragged. Start at the origin; the drag will move it across several frames.
        var entity = world.CreateEntity();
        entity.Set(new TransformComponent(new Vector2(0f, 0f)));

        var startPos = entity.Get<TransformComponent>().Position;
        var startRot = entity.Get<TransformComponent>().Rotation;
        var startScale = entity.Get<TransformComponent>().Scale;
        var startOrigin = entity.Get<TransformComponent>().Origin;

        // ---- Begin the drag transaction, push N intermediate transform edits (one per "frame") ----
        history.BeginTransaction();
        Assert.True(history.InTransaction);

        // Drag the entity to a sequence of targets, exactly as GizmoSystem.ApplyDragEdit does:
        // each frame pushes TransformEditCommand.FromCurrent (before = live transform, after = target).
        var targets = new[]
        {
            new Vector2(5f, 0f), new Vector2(12f, 3f), new Vector2(20f, 7f),
            new Vector2(31f, 10f), new Vector2(40f, 15f),
        };
        foreach (var target in targets)
            history.Push(TransformEditCommand.FromCurrent(entity, target, startRot, startScale, startOrigin));

        // The live edit shows during the drag (last target applied), but NO history entry yet.
        Assert.Equal(targets[^1], entity.Get<TransformComponent>().Position);
        Assert.Equal(0, history.Count);

        // ---- Commit: the whole drag collapses into EXACTLY ONE undo entry ----
        history.CommitTransaction();
        Assert.False(history.InTransaction);
        Assert.Equal(1, history.Count);

        // ---- A single undo restores the pre-drag transform whole ----
        history.Undo();
        Assert.Equal(startPos, entity.Get<TransformComponent>().Position);
        Assert.Equal(0, history.Count);
        Assert.Equal(1, history.RedoCount);

        // ---- A single redo re-applies the whole drag to its final target ----
        history.Redo();
        Assert.Equal(targets[^1], entity.Get<TransformComponent>().Position);
        Assert.Equal(1, history.Count);
    }

    // ---- GizmoUiTargetTest (Wave 8a): a UI/HUD-target entity lives in virtual space — the gizmo
    // reads the cursor's VirtualPosition; its overlay VISUALS land on the native Editor target ----

    [Fact]
    public void GizmoUiTargetTest_MoveDragsInVirtualSpace_AndOverlaysFollowTheTarget()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        // The camera is looking somewhere else entirely with a non-1 zoom: a virtual-space drag
        // must be unaffected by it (screen-space passes have no camera).
        var camera = new GameCamera(800, 600) { Zoom = 2f, Position = new Vector2(9000, 9000) };

        // A selected HUD-target sprite at virtual (100, 100).
        var entity = world.CreateEntity();
        entity.Set(new TransformComponent(new Vector2(100, 100)));
        entity.Set(new SpriteInfoComponent
        {
            Source = new Rectangle(0, 0, 10, 10),
            Size = new Vector2(10, 10),
            Target = RenderTargetID.HUD,
        });
        entity.Set(new SelectedComponent());

        // The cursor: the WORLD position is far away (the camera moved); only the VIRTUAL
        // position addresses the entity. Press exactly on the pivot = the move handle's centre.
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent
        {
            WorldPosition = new Vector2(9000, 9000),
            VirtualPosition = new Vector2(100, 100),
            LeftButton = true,
            LeftButtonPressed = true,
        });

        using var gizmo = new GizmoSystem(world, camera, history);
        var edit = new GameState(new GameTime()) { RunMode = RunMode.Edit };
        gizmo.Update(edit); // press: grabs the move handle at the virtual pivot
        gizmo.EmitOverlays(edit); // the draw-phase visual emission (editor.overlayPrep)

        // The overlay VISUALS land on the native-resolution Editor target (screen-baked meshes,
        // no VisibleComponent — the chrome rule); the entity's HUD space only selected the
        // virtual→screen projection.
        using var overlays = world.GetEntities().With<GizmoOverlayComponent>().AsSet();
        var overlayCount = 0;
        foreach (var overlay in overlays.GetEntities())
        {
            overlayCount++;
            Assert.Equal(RenderTargetID.Editor, overlay.Get<DrawComponent>().Target);
            Assert.False(overlay.Has<VisibleComponent>());
        }
        Assert.Equal(2, overlayCount); // outline + handle

        // Drag +30/+10 in VIRTUAL coordinates (the world position stays wherever the camera is).
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.VirtualPosition = new Vector2(130, 110);
        gizmo.Update(edit);

        // Release → the move applied the raw virtual delta, one undo step.
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        gizmo.Update(edit);

        Assert.Equal(new Vector2(130, 110), entity.Get<TransformComponent>().Position);
        Assert.Equal(1, history.Count);
        history.Undo();
        Assert.Equal(new Vector2(100, 100), entity.Get<TransformComponent>().Position);
    }

    // ---- ClickOwnershipTest (bugfix): the gizmo claims its presses, so a handle press that lands
    // OUTSIDE the selected sprite's bounds (rotate ring, scale handle) must NOT be treated as
    // click-empty (clearing the selection and killing the drag) or as a click on another sprite
    // (re-picking it mid-drag) by the same frame's selection pass. Frames are driven in the real
    // pipeline order: GizmoSystem (update pipeline) BEFORE SelectionSystem (end of draw pipeline).

    private static Entity MakeSprite(World world, Vector2 position, float finalDepth, int size = 10)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(position));
        e.Set(new SpriteInfoComponent
        {
            Source = new Rectangle(0, 0, size, size),
            Size = new Vector2(size, size),
            Target = RenderTargetID.Main,
        });
        e.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main, LayerDepth = finalDepth });
        e.Set(new VisibleComponent());
        return e;
    }

    private static Entity MakeCursor(World world, Vector2 worldPoint, bool pressed)
    {
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent
        {
            WorldPosition = worldPoint,
            VirtualPosition = worldPoint,
            LeftButton = pressed,
            LeftButtonPressed = pressed,
        });
        return cursor;
    }

    private static void MakeGizmoState(World world, GizmoTool tool)
    {
        var state = GizmoStateComponent.Default;
        state.Tool = tool;
        world.CreateEntity().Set(state);
    }

    private static Entity? Selected(World world)
    {
        using var set = world.GetEntities().With<SelectedComponent>().AsSet();
        foreach (var e in set.GetEntities()) return e;
        return null;
    }

    /// <summary>One editor frame in the real pipeline order: the gizmo runs in the UPDATE pipeline,
    /// selection at the END of the DRAW pipeline — the same frame sees both.</summary>
    private static void Frame(GameState state, GizmoSystem gizmo, SelectionSystem selection)
    {
        gizmo.Update(state);
        selection.Update(state);
    }

    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };

    [Fact]
    public void ClickOwnershipTest_RotateHandlePressOutsideSpriteBounds_SelectionSurvivesAndDragCompletes()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, camera, history);
        using var selection = new SelectionSystem(world, camera);
        MakeGizmoState(world, GizmoTool.Rotate);

        // A selected 10x10 sprite at (100,100). The rotate ring (radius 40) lies far OUTSIDE it.
        var entity = MakeSprite(world, new Vector2(100, 100), finalDepth: 0.5f);
        entity.Set(new SelectedComponent());

        // Press exactly on the ring, at pivot + (40, 0) = (140, 100) — empty space, no sprite there.
        var cursor = MakeCursor(world, new Vector2(140, 100), pressed: true);
        var edit = Edit();
        Frame(edit, gizmo, selection);

        // THE BUG: the same frame's selection pass treated the handle press as click-empty and
        // cleared the selection, killing the drag the gizmo had just begun.
        Assert.Equal(entity, Selected(world));

        // Held frame: sweep 90 degrees about the pivot (ray +X -> ray +Y).
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(100, 140);
        input.VirtualPosition = input.WorldPosition;
        Frame(edit, gizmo, selection);
        Assert.Equal(entity, Selected(world));

        // Release: the drag completed as ONE undo step and the rotation stuck.
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        Frame(edit, gizmo, selection);

        Assert.Equal(entity, Selected(world));
        Assert.Equal(MathHelper.PiOver2, entity.Get<TransformComponent>().Rotation, 3);
        Assert.Equal(1, history.Count);
    }

    [Fact]
    public void ClickOwnershipTest_ScaleHandlePressOutsideSpriteBounds_SelectionSurvives()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, camera, history);
        using var selection = new SelectionSystem(world, camera);
        MakeGizmoState(world, GizmoTool.Scale);

        var entity = MakeSprite(world, new Vector2(100, 100), finalDepth: 0.5f);
        entity.Set(new SelectedComponent());

        // The scale handle sits diagonally at pivot + (48, -48) = (148, 52) — outside the sprite.
        var cursor = MakeCursor(world, new Vector2(148, 52), pressed: true);
        var edit = Edit();
        Frame(edit, gizmo, selection);
        Assert.Equal(entity, Selected(world));

        // Drag +1 scale unit along X -> uniform factor 2; release -> one undo step.
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(148 + GizmoTransform.ScaleDragUnit, 52);
        input.VirtualPosition = input.WorldPosition;
        Frame(edit, gizmo, selection);
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        Frame(edit, gizmo, selection);

        Assert.Equal(entity, Selected(world));
        Assert.Equal(new Vector2(2f, 2f), entity.Get<TransformComponent>().Scale);
        Assert.Equal(1, history.Count);
    }

    [Fact]
    public void ClickOwnershipTest_HandlePressOverAnotherSprite_DoesNotRepick()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, camera, history);
        using var selection = new SelectionSystem(world, camera);
        MakeGizmoState(world, GizmoTool.Rotate);

        // A is selected; sprite B happens to sit exactly under A's rotate ring at (140, 100).
        var a = MakeSprite(world, new Vector2(100, 100), finalDepth: 0.5f);
        var b = MakeSprite(world, new Vector2(135, 95), finalDepth: 0.5f);
        a.Set(new SelectedComponent());

        var cursor = MakeCursor(world, new Vector2(140, 100), pressed: true);
        var edit = Edit();
        Frame(edit, gizmo, selection);

        // The handle press must not re-select B mid-drag (the second-order variant of the bug:
        // the drag would retarget to B with A's drag-start snapshot).
        Assert.Equal(a, Selected(world));
        Assert.False(b.Has<SelectedComponent>());

        // The drag proceeds on A; B is untouched.
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(100, 140);
        input.VirtualPosition = input.WorldPosition;
        Frame(edit, gizmo, selection);
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        Frame(edit, gizmo, selection);

        Assert.Equal(a, Selected(world));
        Assert.Equal(MathHelper.PiOver2, a.Get<TransformComponent>().Rotation, 3);
        Assert.Equal(0f, b.Get<TransformComponent>().Rotation);
        Assert.Equal(new Vector2(135, 95), b.Get<TransformComponent>().Position);
    }

    [Fact]
    public void ClickOwnershipTest_DragContinuation_NeverRepicksOrClears()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, camera, history);
        using var selection = new SelectionSystem(world, camera);
        MakeGizmoState(world, GizmoTool.Move);

        var a = MakeSprite(world, new Vector2(100, 100), finalDepth: 0.5f);
        var b = MakeSprite(world, new Vector2(295, 295), finalDepth: 0.5f);
        a.Set(new SelectedComponent());

        // Grab the move handle at A's pivot.
        var cursor = MakeCursor(world, new Vector2(100, 100), pressed: true);
        var edit = Edit();
        Frame(edit, gizmo, selection);
        Assert.Equal(a, Selected(world));

        // Held frame: the cursor crosses onto sprite B — no press edge, nothing may change.
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(300, 300);
        input.VirtualPosition = input.WorldPosition;
        Frame(edit, gizmo, selection);
        Assert.Equal(a, Selected(world));

        // A spurious press edge mid-drag (injected channels can produce one) over sprite B: the
        // in-progress drag owns it — no re-pick, no clear.
        input.LeftButtonPressed = true;
        Frame(edit, gizmo, selection);
        Assert.Equal(a, Selected(world));
        Assert.False(b.Has<SelectedComponent>());

        // Release over empty space: a release must NEVER clear the selection.
        input.LeftButtonPressed = false;
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        input.WorldPosition = new Vector2(500, 500);
        input.VirtualPosition = input.WorldPosition;
        Frame(edit, gizmo, selection);

        Assert.Equal(a, Selected(world));
        Assert.Equal(new Vector2(500, 500), a.Get<TransformComponent>().Position);
        Assert.Equal(new Vector2(295, 295), b.Get<TransformComponent>().Position);
        Assert.Equal(1, history.Count); // the whole drag is still exactly one undo step
    }

    [Fact]
    public void ClickOwnershipTest_GenuineClickEmpty_StillClears()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, camera, history);
        using var selection = new SelectionSystem(world, camera);
        MakeGizmoState(world, GizmoTool.Move);

        var entity = MakeSprite(world, new Vector2(100, 100), finalDepth: 0.5f);
        entity.Set(new SelectedComponent());

        // (500,500) is on no handle (move handle radius 9 at the pivot) and on no sprite: the
        // claim must not over-reach — a genuine click on empty space still clears.
        MakeCursor(world, new Vector2(500, 500), pressed: true);
        Frame(Edit(), gizmo, selection);

        Assert.Null(Selected(world));
    }

    [Fact]
    public void ClickOwnershipTest_ClickAnotherSpriteAwayFromHandles_Reselects()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, camera, history);
        using var selection = new SelectionSystem(world, camera);
        MakeGizmoState(world, GizmoTool.Move);

        var a = MakeSprite(world, new Vector2(100, 100), finalDepth: 0.5f);
        var b = MakeSprite(world, new Vector2(300, 300), finalDepth: 0.5f);
        a.Set(new SelectedComponent());

        // A press over sprite B, far from any of A's handles, is an ordinary re-pick.
        MakeCursor(world, new Vector2(305, 305), pressed: true);
        Frame(Edit(), gizmo, selection);

        Assert.Equal(b, Selected(world));
        Assert.False(a.Has<SelectedComponent>());
    }
}
