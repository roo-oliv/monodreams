using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Extension;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Proxy;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the colliders-as-entities editor model (CE-C): a collider is its OWN entity — a shape
/// component + its <c>TransformComponent</c> — so it is <b>border-picked on its world shape</b> (the
/// camera-rig precedent for a spriteless first-class entity) and moved / scaled by the ORDINARY gizmo
/// (a <see cref="TransformEditCommand"/> on its own transform), like any entity. A box refuses Rotate
/// (axis-aligned); a convex rotates. A boundary's baked segment is pickable but movement-refused (it
/// regenerates). The whole-shape box/convex PROXIES are retired — only the sub-element vertex/thickness
/// handles remain proxies (see <see cref="ProxyVertexTests"/>).
///
/// Names the live premise "A collider is a first-class editor entity: selected on its world shape,
/// moved/scaled by the ordinary gizmo…" in MonoDreams/level-editor/docs/premises.md.
/// </summary>
public class ProxyTests
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

    // ---- The collider world shape derivation reads the WORLD transform, so a collider on a CHILD
    // (a prefab instance's child collider) sits where the collider does (PF-G item 1). ----

    [Fact]
    public void ConvexWorldVertices_ChildEntity_FoldInParentWorldPosition()
    {
        using var world = new World();
        var model = new[] { new Vector2(0, 0), new Vector2(20, 0), new Vector2(10, 15) };

        var root = world.CreateEntity();
        root.Set(new TransformComponent(new Vector2(300, 200)));
        var child = world.CreateEntity();
        child.Set(new TransformComponent(new Vector2(-10, 5)));
        child.Set(new ConvexColliderComponent((Vector2[])model.Clone()));
        child.SetParent(root); // matrix link → child.WorldPosition = (290, 205)

        var childTransform = child.Get<TransformComponent>();
        var collider = child.Get<ConvexColliderComponent>();

        var outline = ProxyGeometry.ConvexWorldVertices(childTransform, collider);
        Assert.Equal(new Vector2(290, 205), outline[0]);   // model[0] + child.WorldPosition
        Assert.Equal(new Vector2(310, 205), outline[1]);
        Assert.Equal(new Vector2(300, 220), outline[2]);

        // TryGetColliderWorldShape (the shape the pick + selection outline use) agrees.
        Assert.True(ProxyGeometry.TryGetColliderWorldShape(child, out var viaShape));
        Assert.Equal(outline, viaShape);
    }

    [Fact]
    public void BoxWorldRect_ComposesTransformScale()
    {
        using var world = new World();
        var box = new BoxColliderComponent(new Vector2(30, 40));
        var e = world.CreateEntity();
        e.Set(new TransformComponent(new Vector2(100, 100)));
        e.Set(box);

        // Scale on a box scales its Transform.Scale — the world derivation composes it (no
        // special-case resize command). Scale (2,1) → the world rect is 60×40, still centered.
        e.Get<TransformComponent>().Scale = new Vector2(2f, 1f);
        var rect = MonoDreams.Extensions.Monogame.SATCollision.BoxWorldRect(box, e.Get<TransformComponent>());
        Assert.Equal(60f, rect.Size.X);
        Assert.Equal(40f, rect.Size.Y);
        Assert.Equal(new Vector2(100, 100), rect.Position + rect.Size / 2f);
    }

    // ---- Selection: a collider ENTITY is border-picked on its world shape; a sprite it overlaps
    // is still picked on a click inside (the collider hit-tests only its border) ----

    [Fact]
    public void ColliderEntity_BorderPickSelectsIt_InsideClickPicksSprite()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        using var selection = new SelectionSystem(world, camera);

        // A sprite covering (84,84)-(116,116) (origin-centred on (100,100)).
        var sprite = world.CreateEntity();
        sprite.Set(new TransformComponent(new Vector2(100, 100)));
        sprite.Set(new SpriteInfoComponent
        {
            Source = new Rectangle(0, 0, 32, 32), Size = new Vector2(32, 32),
            Origin = new Vector2(16, 16), Target = RenderTargetID.Main,
        });
        sprite.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main, LayerDepth = 0.5f });
        sprite.Set(new VisibleComponent());

        // A standalone box collider ENTITY over the same world rect (centered on (100,100), 32×32).
        var collider = world.CreateEntity();
        collider.Set(new EntityInfoComponent("BoxCollider"));
        collider.Set(new TransformComponent(new Vector2(100, 100)));
        collider.Set(new BoxColliderComponent(new Vector2(32, 32)));

        // Click ON the collider border (left edge midpoint, world (84,100)): the collider entity wins
        // (it draws on top). It is selected directly (a spriteless first-class entity).
        CreateCursor(world, new Vector2(84, 100), pressed: true);
        selection.Update(Edit());
        Assert.True(collider.Has<SelectedComponent>());
        Assert.False(sprite.Has<SelectedComponent>());

        // Click INSIDE, away from the border (the centre (100,100)): the sprite is picked — the
        // collider only hit-tests its border, so it never shadows a sprite under it.
        var cursor = world.GetEntities().With<CursorInputComponent>().AsSet();
        Entity cursorEntity = default;
        foreach (var c in cursor.GetEntities()) cursorEntity = c;
        ref var input = ref cursorEntity.Get<CursorInputComponent>();
        input.WorldPosition = new Vector2(100, 100);
        input.LeftButtonPressed = true;
        selection.Update(Edit());
        Assert.True(sprite.Has<SelectedComponent>());
        Assert.False(collider.Has<SelectedComponent>());
    }

    // ---- Pre-mortem #5: a collider on a moved + scaled PARENT is still picked on its WORLD shape,
    // at a non-unit zoom (unselectable colliders would be orphans) ----

    [Fact]
    public void ColliderEntity_BorderPick_UnderMovedScaledParent_AtZoom()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600) { Zoom = 3f };
        using var selection = new SelectionSystem(world, camera);

        var parent = world.CreateEntity();
        parent.Set(new TransformComponent(new Vector2(300, 200), 0f, new Vector2(2f, 2f)));
        var collider = world.CreateEntity();
        collider.Set(new TransformComponent(Vector2.Zero)); // local origin → world (300,200)
        collider.Set(new BoxColliderComponent(new Vector2(10, 10)));
        collider.SetParent(parent); // world box: centre (300,200), extent 10*2=20 → (290,190)-(310,210)

        // Click exactly on the left edge (290,200): the world-shape border-pick composes the parent's
        // move + scale, so the collider is selectable (tolerance 8/zoom = ~2.67, distance 0).
        CreateCursor(world, new Vector2(290, 200), pressed: true);
        selection.Update(Edit());
        Assert.True(collider.Has<SelectedComponent>());
    }

    // ---- Transform: a collider ENTITY moves via the ordinary gizmo — one undo step ----

    [Fact]
    public void ColliderEntity_MoveViaGizmo_OneUndoStep_UndoRedo()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, camera, history);

        var collider = world.CreateEntity();
        collider.Set(new TransformComponent(new Vector2(100, 100)));
        collider.Set(new BoxColliderComponent(new Vector2(30, 40)));
        collider.Set(new SelectedComponent());

        // Press the move handle at the collider's world position (its pivot = the box centre).
        var cursor = CreateCursor(world, new Vector2(100, 100), pressed: true);
        gizmo.Update(Edit());

        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(140, 110);
        gizmo.Update(Edit());
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        gizmo.Update(Edit());

        // The ordinary transform edit moved the collider ENTITY (not any component field), one step.
        Assert.Equal(new Vector2(140, 110), collider.Get<TransformComponent>().Position);
        Assert.Equal(new Vector2(30, 40), collider.Get<BoxColliderComponent>().Size); // shape unchanged
        Assert.Equal(1, history.Count);

        history.Undo();
        Assert.Equal(new Vector2(100, 100), collider.Get<TransformComponent>().Position);
        history.Redo();
        Assert.Equal(new Vector2(140, 110), collider.Get<TransformComponent>().Position);
    }

    // ---- Transform: a box collider entity's Scale drag grows its world rect (no resize command) ----

    [Fact]
    public void BoxColliderEntity_ScaleViaGizmo_GrowsWorldRect()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, camera, history);
        var gizmoState = world.CreateEntity();
        var scaleState = GizmoStateComponent.Default;
        scaleState.Tool = GizmoTool.Scale;
        gizmoState.Set(scaleState);

        var collider = world.CreateEntity();
        collider.Set(new TransformComponent(new Vector2(100, 100)));
        var box = new BoxColliderComponent(new Vector2(30, 40));
        collider.Set(box);
        collider.Set(new SelectedComponent());

        // The scale handle sits at pivot + (48,-48) (invZoom 1) = (148,52). Press + drag +X to grow.
        var cursor = CreateCursor(world, new Vector2(148, 52), pressed: true);
        gizmo.Update(Edit());
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(148 + 120, 52); // +X drag → factor > 1
        gizmo.Update(Edit());
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        gizmo.Update(Edit());

        // Scale grew via Transform.Scale (the box Size is unchanged); the world rect composes it.
        Assert.True(collider.Get<TransformComponent>().Scale.X > 1f);
        Assert.Equal(new Vector2(30, 40), box.Size);
        var rect = MonoDreams.Extensions.Monogame.SATCollision.BoxWorldRect(box, collider.Get<TransformComponent>());
        Assert.True(rect.Size.X > 30f);
        Assert.Equal(1, history.Count);
    }

    // ---- Box rotate is refused (axis-aligned); a convex collider rotates ----

    [Fact]
    public void BoxColliderEntity_RotateRefused_FallsBackToMove()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, camera, history);
        var gizmoState = world.CreateEntity();
        var rotateState = GizmoStateComponent.Default;
        rotateState.Tool = GizmoTool.Rotate;
        gizmoState.Set(rotateState);

        var box = world.CreateEntity();
        box.Set(new TransformComponent(new Vector2(100, 100)));
        box.Set(new BoxColliderComponent(new Vector2(30, 40)));
        box.Set(new SelectedComponent());

        // The Rotate tool is resolved to Move for a box collider: the rotate RING (at radius ~40) is
        // NOT hit-tested; pressing the MOVE handle at the pivot starts a move drag, not a rotate.
        var cursor = CreateCursor(world, new Vector2(100, 100), pressed: true);
        gizmo.Update(Edit());
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(110, 100);
        gizmo.Update(Edit());
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        gizmo.Update(Edit());

        // The box moved (Rotate → Move fallback) and never rotated.
        Assert.Equal(0f, box.Get<TransformComponent>().Rotation);
        Assert.Equal(new Vector2(110, 100), box.Get<TransformComponent>().Position);
    }

    [Fact]
    public void ConvexColliderEntity_RotatesNormally()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, camera, history);
        var gizmoState = world.CreateEntity();
        var rotateState = GizmoStateComponent.Default;
        rotateState.Tool = GizmoTool.Rotate;
        gizmoState.Set(rotateState);

        var convex = world.CreateEntity();
        convex.Set(new TransformComponent(new Vector2(100, 100)));
        convex.Set(new ConvexColliderComponent(new[]
        {
            new Vector2(-10, -10), new Vector2(10, -10), new Vector2(0, 12),
        }));
        convex.Set(new SelectedComponent());

        // The rotate RING is at pivot + radius 40; press on it (140,100) and drag around the pivot.
        var cursor = CreateCursor(world, new Vector2(140, 100), pressed: true);
        gizmo.Update(Edit());
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(100, 140); // a quarter turn around the pivot
        gizmo.Update(Edit());
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        gizmo.Update(Edit());

        // A convex collider CAN rotate (the Rotate tool is not refused): its rotation changed.
        Assert.NotEqual(0f, convex.Get<TransformComponent>().Rotation);
        Assert.Equal(1, history.Count);
    }

    // ---- Click-ownership: pressing a spriteless collider's move handle (over empty space, not on
    // its border) must not be treated as click-empty by the same frame's selection pass ----

    [Fact]
    public void ColliderEntity_MoveHandlePress_KeepsSelection_AndDrags()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, camera, history);
        using var selection = new SelectionSystem(world, camera);
        world.CreateEntity().Set(GizmoStateComponent.Default); // shared claim carrier (tool = Move)

        void Frame(GameState state, Entity cursorEntity)
        {
            gizmo.Update(state);
            selection.Update(state);
            ref var i = ref cursorEntity.Get<CursorInputComponent>();
            i.LeftButtonPressed = false;
            i.LeftButtonReleased = false;
        }

        // A box collider covering (110,120)-(140,160): its move handle is the world centre (125,140),
        // which is neither on the border nor over any sprite — the harshest click-ownership variant.
        var collider = world.CreateEntity();
        collider.Set(new TransformComponent(new Vector2(125, 140)));
        collider.Set(new BoxColliderComponent(new Vector2(30, 40)));
        collider.Set(new SelectedComponent());

        var cursor = CreateCursor(world, new Vector2(125, 140), pressed: false);
        var edit = Edit();

        // Press the move handle at the shape centre: the gizmo claims it, so the selection pass does
        // NOT clear the selection (the reported click-empty bug), and the drag begins.
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.LeftButton = true;
        input.LeftButtonPressed = true;
        Frame(edit, cursor);
        Assert.True(collider.Has<SelectedComponent>());

        // Drag and release: the collider moved, one undo step, still selected.
        input.WorldPosition = new Vector2(165, 150);
        Frame(edit, cursor);
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        Frame(edit, cursor);

        Assert.Equal(new Vector2(165, 150), collider.Get<TransformComponent>().Position);
        Assert.Equal(1, history.Count);
        Assert.True(collider.Has<SelectedComponent>());
    }

    // ---- Bake products are pickable but movement-refused (they regenerate from their source) ----

    [Fact]
    public void BakedProduct_IsPickable_ButMoveRefused()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600);
        var history = new EditorHistory(world);
        using var gizmo = new GizmoSystem(world, camera, history);
        using var selection = new SelectionSystem(world, camera);
        world.CreateEntity().Set(GizmoStateComponent.Default);

        var segment = world.CreateEntity();
        segment.Set(new TransformComponent(new Vector2(200, 200)));
        segment.Set(new ConvexColliderComponent(new[]
        {
            new Vector2(-20, -4), new Vector2(20, -4), new Vector2(20, 4), new Vector2(-20, 4),
        }, passive: true));
        segment.Set(new BakedProductComponent());

        // Border-pick the segment (top edge, world (200,196)): it IS selectable (inspectable).
        var cursor = CreateCursor(world, new Vector2(200, 196), pressed: true);
        selection.Update(Edit());
        Assert.True(segment.Has<SelectedComponent>());

        // Try to move it: the press on the move handle (its world centre (200,200)) is claimed but
        // refused — a baked product regenerates from its source, so it never moves.
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.WorldPosition = new Vector2(200, 200);
        input.LeftButton = true;
        input.LeftButtonPressed = true;
        gizmo.Update(Edit());
        input.LeftButtonPressed = false;
        input.WorldPosition = new Vector2(230, 200);
        gizmo.Update(Edit());
        input.LeftButton = false;
        input.LeftButtonReleased = true;
        gizmo.Update(Edit());

        Assert.Equal(new Vector2(200, 200), segment.Get<TransformComponent>().Position); // never moved
        Assert.Equal(0, history.Count);
    }
}
