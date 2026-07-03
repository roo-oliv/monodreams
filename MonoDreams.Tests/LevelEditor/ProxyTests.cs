using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Proxy;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;
using MonoDreams.System;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the Wave 8b collider gizmo-proxy invariants: colliders are component-local spatial
/// data (NOT entities), so the editor materializes standalone proxy entities bound to
/// (entity, component-field) via <see cref="GizmoProxyComponent"/> — spawned by
/// <see cref="ProxySyncSystem"/> when the owning entity is selected in Edit, kept in sync with
/// the collider every frame, despawned on deselect / mode exit / target death — and dragging a
/// proxy through the REAL gizmo path writes back into the bound component field
/// (<see cref="ColliderEditCommand"/>) through the coalescing undo transaction: one drag = ONE
/// undo step, undo restores the exact prior Bounds / ModelVertices, redo re-applies.
///
/// Names the live premise "Collider shapes are edited through standalone gizmo proxies" in
/// MonoDreams/level-editor/docs/premises.md.
/// </summary>
public class ProxyTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    private static Entity CreateOwnerWithBothColliders(World world, Vector2 position)
    {
        var owner = world.CreateEntity();
        owner.Set(new TransformComponent(position));
        owner.Set(new BoxColliderComponent(new Rectangle(10, 20, 30, 40)));
        owner.Set(new ConvexColliderComponent(new[]
        {
            new Vector2(0, 0), new Vector2(20, 0), new Vector2(10, 15),
        }));
        return owner;
    }

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

    // ---- ProxyLifecycleTest: select spawns one proxy per collider; deselect / mode exit /
    // target death despawn; proxies are standalone (no ChildOf) and survive a hierarchy frame ----

    [Fact]
    public void ProxyLifecycleTest_SelectSpawns_DeselectAndModeExitDespawn()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        using var sync = new ProxySyncSystem(world, camera);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();

        var owner = CreateOwnerWithBothColliders(world, new Vector2(100, 100));
        owner.Set(new SelectedComponent());

        // Selecting an entity with a Box + a Convex collider spawns ONE proxy per collider.
        sync.Update(Edit());
        Assert.Equal(2, proxies.Count);

        var sawBox = false;
        var sawConvex = false;
        foreach (var proxy in proxies.GetEntities())
        {
            var binding = proxy.Get<GizmoProxyComponent>();
            Assert.Equal(owner, binding.Target);
            if (binding.Kind == ProxyBindingKind.BoxColliderBounds) sawBox = true;
            if (binding.Kind == ProxyBindingKind.ConvexColliderShape) sawConvex = true;

            // Standalone overlay rules: never ChildOf-parented; the VISUAL is native-resolution
            // chrome — a screen-baked mesh on the Editor target, NO VisibleComponent (the chrome
            // rule: its presence would pull the mesh into MeshPrepSystem, which would overwrite
            // the identity WorldMatrix the screen-baked vertices require).
            Assert.False(proxy.Has<ChildOfComponent>());
            Assert.False(proxy.Has<VisibleComponent>());
            Assert.True(proxy.Has<TransformComponent>());
            Assert.Equal(RenderTargetID.Editor, proxy.Get<DrawComponent>().Target);
        }
        Assert.True(sawBox);
        Assert.True(sawConvex);

        // Proxies survive a HierarchySystem frame (DisposeOrphans is live in Edit).
        using (var hierarchy = new HierarchySystem(world))
        {
            hierarchy.Update(Edit());
        }
        Assert.Equal(2, proxies.Count);

        // Deselect → despawn.
        owner.Remove<SelectedComponent>();
        sync.Update(Edit());
        Assert.Equal(0, proxies.Count);

        // Reselect → respawn; mode exit (Play) → despawn.
        owner.Set(new SelectedComponent());
        sync.Update(Edit());
        Assert.Equal(2, proxies.Count);
        sync.Update(Play());
        Assert.Equal(0, proxies.Count);
    }

    [Fact]
    public void ProxyLifecycleTest_TargetDeathDespawns()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        using var sync = new ProxySyncSystem(world, camera);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();

        var owner = CreateOwnerWithBothColliders(world, new Vector2(0, 0));
        owner.Set(new SelectedComponent());
        sync.Update(Edit());
        Assert.Equal(2, proxies.Count);

        owner.Dispose();
        sync.Update(Edit());
        Assert.Equal(0, proxies.Count);
    }

    [Fact]
    public void ProxyLifecycleTest_SelectingTheProxyItselfKeepsTheFamilyAlive()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        using var sync = new ProxySyncSystem(world, camera);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();

        var owner = CreateOwnerWithBothColliders(world, new Vector2(100, 100));
        owner.Set(new SelectedComponent());
        sync.Update(Edit());
        Assert.Equal(2, proxies.Count);

        // The designer clicks a proxy: single-select moves SelectedComponent onto the proxy.
        // The proxies must stay alive — their anchor is the proxy's bound target.
        Entity boxProxy = default;
        foreach (var proxy in proxies.GetEntities())
            if (proxy.Get<GizmoProxyComponent>().Kind == ProxyBindingKind.BoxColliderBounds)
                boxProxy = proxy;
        owner.Remove<SelectedComponent>();
        boxProxy.Set(new SelectedComponent());

        sync.Update(Edit());
        Assert.Equal(2, proxies.Count);
        Assert.True(boxProxy.IsAlive);
    }

    // ---- Sync: moving the OWNING entity's transform moves the proxy (re-derived per frame) ----

    [Fact]
    public void ProxySyncTest_OwnerTransformMoveMovesTheProxy()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        using var sync = new ProxySyncSystem(world, camera);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();

        var owner = CreateOwnerWithBothColliders(world, new Vector2(100, 100));
        owner.Set(new SelectedComponent());
        sync.Update(Edit());

        // The box proxy sits at the collider's world centre: (100,100) + Bounds.Center (25,40).
        Entity boxProxy = default, convexProxy = default;
        foreach (var proxy in proxies.GetEntities())
        {
            if (proxy.Get<GizmoProxyComponent>().Kind == ProxyBindingKind.BoxColliderBounds) boxProxy = proxy;
            else convexProxy = proxy;
        }
        Assert.Equal(new Vector2(125, 140), boxProxy.Get<TransformComponent>().Position);

        // Move the owner; the proxies re-derive the same frame the sync runs.
        owner.Get<TransformComponent>().Position = new Vector2(160, 130);
        sync.Update(Edit());
        Assert.Equal(new Vector2(185, 170), boxProxy.Get<TransformComponent>().Position);

        // The sync also refreshes the convex collider's WorldVertices (physics is frozen in Edit,
        // so nothing else would), keeping the debug outline coherent with the moved owner.
        var convex = owner.Get<ConvexColliderComponent>();
        Assert.Equal(new Vector2(160, 130), convex.WorldVertices[0]);
        Assert.True(convexProxy.IsAlive);
    }

    // ---- Write-back: dragging a Box proxy by delta D shifts Bounds by D through ONE undo step ----

    [Fact]
    public void BoxProxyWriteBackTest_DragShiftsBounds_OneUndoStep_UndoRedo()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var sync = new ProxySyncSystem(world, camera);
        using var gizmo = new GizmoSystem(world, camera, history);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();

        var owner = world.CreateEntity();
        owner.Set(new TransformComponent(new Vector2(100, 100)));
        owner.Set(new BoxColliderComponent(new Rectangle(10, 20, 30, 40)));
        owner.Set(new SelectedComponent());
        sync.Update(Edit());
        Assert.Equal(1, proxies.Count);

        Entity boxProxy = default;
        foreach (var proxy in proxies.GetEntities()) boxProxy = proxy;

        // Select the proxy (what SelectionSystem does on a border click) and press on its pivot —
        // the collider's world centre (125, 140) — grabbing the move handle.
        owner.Remove<SelectedComponent>();
        boxProxy.Set(new SelectedComponent());
        sync.Update(Edit());
        var cursor = CreateCursor(world, new Vector2(125, 140), pressed: true);
        gizmo.Update(Edit());

        // Drag by D = (40, 10) across two frames; the edit applies live, but no history entry yet.
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(125 + 25, 140 + 5);
        gizmo.Update(Edit());
        input.WorldPosition = new Vector2(125 + 40, 140 + 10);
        gizmo.Update(Edit());

        Assert.Equal(new Rectangle(50, 30, 30, 40), owner.Get<BoxColliderComponent>().Bounds);
        Assert.Equal(0, history.Count); // still inside the coalescing transaction

        // The write-back goes into the COMPONENT, never the owner's transform.
        Assert.Equal(new Vector2(100, 100), owner.Get<TransformComponent>().Position);

        // The proxy tracks the written-back collider on the next sync.
        sync.Update(Edit());
        Assert.Equal(new Vector2(165, 150), boxProxy.Get<TransformComponent>().Position);

        // Release → exactly ONE undo step for the whole drag.
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        gizmo.Update(Edit());
        Assert.Equal(1, history.Count);

        // Undo restores the exact prior Bounds; redo re-applies.
        history.Undo();
        Assert.Equal(new Rectangle(10, 20, 30, 40), owner.Get<BoxColliderComponent>().Bounds);
        history.Redo();
        Assert.Equal(new Rectangle(50, 30, 30, 40), owner.Get<BoxColliderComponent>().Bounds);
    }

    // ---- Write-back: dragging a Convex proxy translates ALL ModelVertices by D, refreshes
    // WorldVertices + BroadPhaseAABB, one undo step ----

    [Fact]
    public void ConvexProxyWriteBackTest_DragTranslatesModelVertices_OneUndoStep_UndoRedo()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var sync = new ProxySyncSystem(world, camera);
        using var gizmo = new GizmoSystem(world, camera, history);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();

        var owner = world.CreateEntity();
        owner.Set(new TransformComponent(Vector2.Zero));
        owner.Set(new ConvexColliderComponent(new[]
        {
            new Vector2(0, 0), new Vector2(20, 0), new Vector2(10, 15),
        }));
        owner.Set(new SelectedComponent());
        sync.Update(Edit());
        Assert.Equal(1, proxies.Count);

        Entity convexProxy = default;
        foreach (var proxy in proxies.GetEntities()) convexProxy = proxy;

        // Select the proxy; press on its pivot — the polygon's world centroid (10, 5).
        owner.Remove<SelectedComponent>();
        convexProxy.Set(new SelectedComponent());
        sync.Update(Edit());
        var cursor = CreateCursor(world, new Vector2(10, 5), pressed: true);
        gizmo.Update(Edit());

        // Drag by D = (6, -4); release.
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(10 + 6, 5 - 4);
        gizmo.Update(Edit());
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        gizmo.Update(Edit());

        var convex = owner.Get<ConvexColliderComponent>();
        Assert.Equal(new Vector2(6, -4), convex.ModelVertices[0]);
        Assert.Equal(new Vector2(26, -4), convex.ModelVertices[1]);
        Assert.Equal(new Vector2(16, 11), convex.ModelVertices[2]);
        // The write-back refreshed the derived world data (owner transform is identity here), per
        // the collision premise "BroadPhaseAABB must be refreshed when vertices change".
        Assert.Equal(new Vector2(6, -4), convex.WorldVertices[0]);
        Assert.Equal(new Vector2(6, -4), convex.BroadPhaseAABB.Position);

        Assert.Equal(1, history.Count); // one drag = one undo step

        history.Undo();
        convex = owner.Get<ConvexColliderComponent>();
        Assert.Equal(new Vector2(0, 0), convex.ModelVertices[0]);
        Assert.Equal(new Vector2(20, 0), convex.ModelVertices[1]);
        Assert.Equal(new Vector2(10, 15), convex.ModelVertices[2]);
        Assert.Equal(new Vector2(0, 0), convex.WorldVertices[0]);

        history.Redo();
        Assert.Equal(new Vector2(6, -4), owner.Get<ConvexColliderComponent>().ModelVertices[0]);
    }

    // ---- Selection integration: a border click picks the proxy through the SAME pick path; a
    // click inside (away from the border) still picks the owner's sprite ----

    [Fact]
    public void ProxySelectionTest_BorderClickPicksProxy_InsideClickPicksOwner()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        using var sync = new ProxySyncSystem(world, camera);
        using var selection = new SelectionSystem(world, camera);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();

        // A sprite that fully covers its own box collider (the common Tile/Wall shape).
        var owner = world.CreateEntity();
        owner.Set(new TransformComponent(new Vector2(100, 100)));
        owner.Set(new SpriteInfoComponent
        {
            Source = new Rectangle(0, 0, 32, 32),
            Size = new Vector2(32, 32),
            Target = RenderTargetID.Main,
        });
        owner.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main, LayerDepth = 0.5f });
        owner.Set(new VisibleComponent());
        owner.Set(new BoxColliderComponent(new Rectangle(0, 0, 32, 32)));
        owner.Set(new SelectedComponent());
        sync.Update(Edit());
        Assert.Equal(1, proxies.Count);
        Entity boxProxy = default;
        foreach (var proxy in proxies.GetEntities()) boxProxy = proxy;

        // Click ON the collider border (left edge midpoint, world (100, 116)): the proxy wins the
        // pick even though the sprite is under the cursor too (the proxy draws on top).
        var cursor = CreateCursor(world, new Vector2(100, 116), pressed: true);
        selection.Update(Edit());
        Assert.True(boxProxy.Has<SelectedComponent>());
        Assert.False(owner.Has<SelectedComponent>());

        // The proxies stay alive (the anchor follows the selected proxy's target).
        sync.Update(Edit());
        Assert.True(boxProxy.IsAlive);

        // Click INSIDE, away from the border (the centre, world (116, 116)): the owner's sprite is
        // picked — the proxy only hit-tests its border, so it never shadows the entity it decorates.
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.WorldPosition = new Vector2(116, 116);
        input.LeftButtonPressed = true;
        selection.Update(Edit());
        Assert.True(owner.Has<SelectedComponent>());
        Assert.False(boxProxy.Has<SelectedComponent>());
    }

    // ---- Click-ownership (bugfix): pressing the selected proxy's move handle — the shape's world
    // centre, which is neither on the proxy's border nor (here) over any sprite — must not be
    // treated as click-empty by the same frame's selection pass (which would deselect, despawn the
    // family via ProxySyncSystem, and kill the drag). Frames run in the real pipeline order:
    // gizmo -> proxy sync (update pipeline), then selection (end of draw pipeline). ----

    [Fact]
    public void ProxyClickOwnershipTest_MoveHandlePressAtShapeCentre_KeepsProxySelectedAndDrags()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var sync = new ProxySyncSystem(world, camera);
        using var gizmo = new GizmoSystem(world, camera, history);
        using var selection = new SelectionSystem(world, camera);
        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();
        world.CreateEntity().Set(GizmoStateComponent.Default); // shared editor state (tool = Move)

        void Frame(GameState state, Entity cursorEntity)
        {
            gizmo.Update(state);
            sync.Update(state);
            selection.Update(state);
            // A real frame's press edge lasts one frame.
            ref var input = ref cursorEntity.Get<CursorInputComponent>();
            input.LeftButtonPressed = false;
            input.LeftButtonReleased = false;
        }

        // A sprite-less owner (a pure trigger volume): its box collider covers EMPTY space —
        // world rect (110,120)-(140,160) — the harshest variant of the reported bug.
        var owner = world.CreateEntity();
        owner.Set(new TransformComponent(new Vector2(100, 100)));
        owner.Set(new BoxColliderComponent(new Rectangle(10, 20, 30, 40)));
        owner.Set(new SelectedComponent());

        var cursor = CreateCursor(world, new Vector2(0, 0), pressed: false);
        var edit = Edit();
        Frame(edit, cursor);
        Assert.Equal(1, proxies.Count);
        Entity boxProxy = default;
        foreach (var proxy in proxies.GetEntities()) boxProxy = proxy;

        // Border press: selection moves onto the proxy (8b behavior preserved), family stays alive.
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.WorldPosition = new Vector2(110, 140); // left edge of the world rect
        input.LeftButton = true;
        input.LeftButtonPressed = true;
        Frame(edit, cursor);
        Assert.True(boxProxy.Has<SelectedComponent>());
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        Frame(edit, cursor);
        Assert.True(boxProxy.IsAlive);
        Assert.True(boxProxy.Has<SelectedComponent>());

        // Press the proxy's move handle at the shape's world centre (125, 140): 15/20 units away
        // from every border edge (outside the 8px border pick) and over no sprite. THE BUG: the
        // selection pass treated this press as click-empty, deselecting the proxy — the family
        // despawned and the drag died.
        input.WorldPosition = new Vector2(125, 140);
        input.LeftButton = true;
        input.LeftButtonPressed = true;
        Frame(edit, cursor);
        Assert.True(boxProxy.Has<SelectedComponent>());
        Assert.Equal(1, proxies.Count);

        // Drag by (40, 10) and release: the write-back lands in Bounds, one undo step.
        input.WorldPosition = new Vector2(165, 150);
        Frame(edit, cursor);
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        Frame(edit, cursor);

        Assert.Equal(new Rectangle(50, 30, 30, 40), owner.Get<BoxColliderComponent>().Bounds);
        Assert.Equal(1, history.Count);
        Assert.True(boxProxy.IsAlive);
        Assert.True(boxProxy.Has<SelectedComponent>());
    }

    // ---- Pure geometry: the world→model delta honors rotation/scale (and IgnoreTransformRotation) ----

    [Fact]
    public void WorldDeltaToModelDeltaTest_HonorsRotationScaleAndIgnoreFlag()
    {
        const float tol = 1e-4f;

        // Rotation π/2 + uniform scale 2: a +X world delta maps to a -Y model delta, halved.
        var transform = new TransformComponent(Vector2.Zero, MathHelper.PiOver2, new Vector2(2f, 2f));
        var model = ProxyGeometry.WorldDeltaToModelDelta(transform, ignoreRotation: false, new Vector2(10f, 0f));
        Assert.Equal(0f, model.X, tol);
        Assert.Equal(-5f, model.Y, tol);

        // IgnoreTransformRotation (Blender-imported colliders): rotation is treated as 0.
        var ignoring = ProxyGeometry.WorldDeltaToModelDelta(transform, ignoreRotation: true, new Vector2(10f, 0f));
        Assert.Equal(5f, ignoring.X, tol);
        Assert.Equal(0f, ignoring.Y, tol);
    }
}
