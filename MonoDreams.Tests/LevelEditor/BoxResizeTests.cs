using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Cursor;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Proxy;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the island-authoring Slice 2 box collider RESIZE handles: eight handles on the box
/// proxy's world rect (corners + edge midpoints, pure <see cref="BoxResize"/> math) adjust
/// exactly the grabbed edge(s) of the Transform-relative <c>Bounds</c> — opposite edges
/// anchored, sides clamped at <see cref="BoxResize.MinSize"/> — through the same
/// one-drag-one-undo <see cref="ColliderEditCommand"/> path and the same click-ownership claim
/// as every gizmo handle.
/// </summary>
public class BoxResizeTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };

    private static Entity CreateCursor(World world, Vector2 worldPoint, bool pressed)
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

    // ---- Pure edge math ----

    [Fact]
    public void Apply_MovesExactlyTheGrabbedEdges()
    {
        var before = new Rectangle(10, 20, 30, 40);

        // Edge midpoints move ONE edge; the perpendicular delta is ignored.
        Assert.Equal(new Rectangle(10, 20, 38, 40),
            BoxResize.Apply(before, BoxResizeHandle.Right, new Vector2(8, 5)));
        Assert.Equal(new Rectangle(18, 20, 22, 40),
            BoxResize.Apply(before, BoxResizeHandle.Left, new Vector2(8, 5)));
        Assert.Equal(new Rectangle(10, 14, 30, 46),
            BoxResize.Apply(before, BoxResizeHandle.Top, new Vector2(8, -6)));
        Assert.Equal(new Rectangle(10, 20, 30, 46),
            BoxResize.Apply(before, BoxResizeHandle.Bottom, new Vector2(8, 6)));

        // Corners move two edges; the opposite corner stays anchored.
        Assert.Equal(new Rectangle(14, 26, 26, 34),
            BoxResize.Apply(before, BoxResizeHandle.TopLeft, new Vector2(4, 6)));
        Assert.Equal(new Rectangle(10, 20, 40, 46),
            BoxResize.Apply(before, BoxResizeHandle.BottomRight, new Vector2(10, 6)));
        Assert.Equal(new Rectangle(10, 26, 34, 34),
            BoxResize.Apply(before, BoxResizeHandle.TopRight, new Vector2(4, 6)));
        Assert.Equal(new Rectangle(14, 20, 26, 46),
            BoxResize.Apply(before, BoxResizeHandle.BottomLeft, new Vector2(4, 6)));
    }

    [Fact]
    public void Apply_ClampsAtMinSize_NeverInverts()
    {
        var before = new Rectangle(10, 20, 30, 40);

        // Dragging the right edge far past the left one clamps at MinSize, anchored left.
        Assert.Equal(new Rectangle(10, 20, BoxResize.MinSize, 40),
            BoxResize.Apply(before, BoxResizeHandle.Right, new Vector2(-100, 0)));
        // Dragging the left edge far past the right one clamps anchored right.
        Assert.Equal(new Rectangle(before.Right - BoxResize.MinSize, 20, BoxResize.MinSize, 40),
            BoxResize.Apply(before, BoxResizeHandle.Left, new Vector2(100, 0)));
        // Same vertically, via a corner.
        var clamped = BoxResize.Apply(before, BoxResizeHandle.TopLeft, new Vector2(100, 100));
        Assert.Equal(BoxResize.MinSize, clamped.Width);
        Assert.Equal(BoxResize.MinSize, clamped.Height);
        Assert.Equal(before.Right, clamped.Right);
        Assert.Equal(before.Bottom, clamped.Bottom);
    }

    [Fact]
    public void HandleWorld_And_HitTest_AgreeOnTheEightPoints()
    {
        var min = new Vector2(110, 120);
        var max = new Vector2(140, 160);

        Assert.Equal(new Vector2(110, 120), BoxResize.HandleWorld(min, max, BoxResizeHandle.TopLeft));
        Assert.Equal(new Vector2(125, 120), BoxResize.HandleWorld(min, max, BoxResizeHandle.Top));
        Assert.Equal(new Vector2(140, 140), BoxResize.HandleWorld(min, max, BoxResizeHandle.Right));
        Assert.Equal(new Vector2(140, 160), BoxResize.HandleWorld(min, max, BoxResizeHandle.BottomRight));

        // Every handle point hit-tests back to itself; the box centre hits nothing.
        foreach (var handle in BoxResize.Handles)
            Assert.Equal(handle, BoxResize.HitTest(min, max, BoxResize.HandleWorld(min, max, handle), 4f));
        Assert.Equal(BoxResizeHandle.None, BoxResize.HitTest(min, max, new Vector2(125, 140), 4f));
    }

    // ---- System-level: a resize drag through the REAL gizmo path — one undo step ----

    private static (World world, EditorHistory history, ProxySyncSystem sync, GizmoSystem gizmo,
        Entity owner, Entity proxy) Arrange()
    {
        var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        var sync = new ProxySyncSystem(world, camera);
        var gizmo = new GizmoSystem(world, camera, history);

        var owner = world.CreateEntity();
        owner.Set(new TransformComponent(new Vector2(100, 100)));
        owner.Set(new BoxColliderComponent(new Rectangle(10, 20, 30, 40))); // world (110,120)-(140,160)
        owner.Set(new SelectedComponent());
        sync.Update(Edit());

        Entity proxy = default;
        using (var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet())
        {
            foreach (var p in proxies.GetEntities()) proxy = p;
        }
        owner.Remove<SelectedComponent>();
        proxy.Set(new SelectedComponent());
        sync.Update(Edit());
        return (world, history, sync, gizmo, owner, proxy);
    }

    [Fact]
    public void RightEdgeDrag_GrowsWidthOnly_OneUndoStep_UndoExact()
    {
        var (world, history, sync, gizmo, owner, _) = Arrange();
        using (world)
        using (sync)
        using (gizmo)
        {
            // Press ON the right edge midpoint (world (140,140)) — 15px from the centre move
            // handle, inside the resize grab radius.
            var cursor = CreateCursor(world, new Vector2(140, 140), pressed: true);
            gizmo.Update(Edit());

            // Drag +20 in X (and a stray +3 in Y the Right handle must ignore); release.
            ref var input = ref cursor.Get<CursorInputComponent>();
            input.LeftButtonPressed = false;
            input.WorldPosition = new Vector2(160, 143);
            gizmo.Update(Edit());
            input.LeftButton = false;
            input.LeftButtonReleased = true;
            gizmo.Update(Edit());

            Assert.Equal(new Rectangle(10, 20, 50, 40), owner.Get<BoxColliderComponent>().Bounds);
            Assert.Equal(1, history.Count); // one drag = one undo step
            // The write-back lands in Bounds, never the owner's transform.
            Assert.Equal(new Vector2(100, 100), owner.Get<TransformComponent>().Position);

            history.Undo();
            Assert.Equal(new Rectangle(10, 20, 30, 40), owner.Get<BoxColliderComponent>().Bounds);
            history.Redo();
            Assert.Equal(new Rectangle(10, 20, 50, 40), owner.Get<BoxColliderComponent>().Bounds);
        }
    }

    [Fact]
    public void CornerDrag_AdjustsBothEdges()
    {
        var (world, history, sync, gizmo, owner, _) = Arrange();
        using (world)
        using (sync)
        using (gizmo)
        {
            // Press the top-left corner (world (110,120)); drag by (+6, -8): x/y move, w/h grow
            // and shrink accordingly, the bottom-right corner stays anchored.
            var cursor = CreateCursor(world, new Vector2(110, 120), pressed: true);
            gizmo.Update(Edit());

            ref var input = ref cursor.Get<CursorInputComponent>();
            input.LeftButtonPressed = false;
            input.WorldPosition = new Vector2(116, 112);
            gizmo.Update(Edit());
            input.LeftButton = false;
            input.LeftButtonReleased = true;
            gizmo.Update(Edit());

            Assert.Equal(new Rectangle(16, 12, 24, 48), owner.Get<BoxColliderComponent>().Bounds);
            Assert.Equal(1, history.Count);

            history.Undo();
            Assert.Equal(new Rectangle(10, 20, 30, 40), owner.Get<BoxColliderComponent>().Bounds);
        }
    }

    [Fact]
    public void CentrePress_StillMovesTheWholeBox()
    {
        var (world, history, sync, gizmo, owner, _) = Arrange();
        using (world)
        using (sync)
        using (gizmo)
        {
            // The centre move handle keeps its Wave-8b behavior: the whole rect shifts.
            var cursor = CreateCursor(world, new Vector2(125, 140), pressed: true);
            gizmo.Update(Edit());

            ref var input = ref cursor.Get<CursorInputComponent>();
            input.LeftButtonPressed = false;
            input.WorldPosition = new Vector2(135, 145);
            gizmo.Update(Edit());
            input.LeftButton = false;
            input.LeftButtonReleased = true;
            gizmo.Update(Edit());

            Assert.Equal(new Rectangle(20, 25, 30, 40), owner.Get<BoxColliderComponent>().Bounds);
            Assert.Equal(1, history.Count);
        }
    }

    [Fact]
    public void ResizeHandlePress_IsClaimed_SelectionKeepsTheProxy()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var sync = new ProxySyncSystem(world, camera);
        using var gizmo = new GizmoSystem(world, camera, history);
        using var selection = new SelectionSystem(world, camera);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();
        world.CreateEntity().Set(GizmoStateComponent.Default); // the shared claim carrier

        var owner = world.CreateEntity();
        owner.Set(new TransformComponent(new Vector2(100, 100)));
        owner.Set(new BoxColliderComponent(new Rectangle(10, 20, 30, 40)));
        owner.Set(new SelectedComponent());

        var cursor = CreateCursor(world, Vector2.Zero, pressed: false);

        void Frame(Entity cursorEntity)
        {
            gizmo.Update(Edit());
            sync.Update(Edit());
            selection.Update(Edit());
            ref var input = ref cursorEntity.Get<CursorInputComponent>();
            input.LeftButtonPressed = false;
            input.LeftButtonReleased = false;
        }

        Frame(cursor);
        Entity proxy = default;
        foreach (var p in proxies.GetEntities()) proxy = p;
        owner.Remove<SelectedComponent>();
        proxy.Set(new SelectedComponent());
        Frame(cursor);

        // Press the BOTTOM-RIGHT resize handle (world (140,160)) — it lies ON the proxy border,
        // so without the gizmo's claim the same frame's selection pass would re-pick/clear.
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.WorldPosition = new Vector2(140, 160);
        input.LeftButton = true;
        input.LeftButtonPressed = true;
        Frame(cursor);
        Assert.True(proxy.Has<SelectedComponent>());

        // Drag and release: the resize lands, the proxy stays selected, one undo step.
        input.WorldPosition = new Vector2(150, 166);
        Frame(cursor);
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        Frame(cursor);

        Assert.Equal(new Rectangle(10, 20, 40, 46), owner.Get<BoxColliderComponent>().Bounds);
        Assert.Equal(1, history.Count);
        Assert.True(proxy.Has<SelectedComponent>());
    }
}
