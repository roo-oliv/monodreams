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
    // reads the cursor's VirtualPosition and draws its overlays on the entity's own target ----

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

        // The overlay entities draw on the entity's own target (HUD), not Main.
        using var overlays = world.GetEntities().With<GizmoOverlayComponent>().AsSet();
        var overlayCount = 0;
        foreach (var overlay in overlays.GetEntities())
        {
            overlayCount++;
            Assert.Equal(RenderTargetID.HUD, overlay.Get<DrawComponent>().Target);
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
}
