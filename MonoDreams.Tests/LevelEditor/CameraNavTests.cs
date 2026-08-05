using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Navigation;
using MonoDreams.LevelEditor.System;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the level-editor premise "Editor camera navigation pans, zooms, and frames the scene,
/// Edit-guarded". Pure logic for the math (<see cref="CameraNav"/>: pan sign + zoom clamp + AABB
/// centre/fit) and a hand-driven <see cref="CameraNavSystem.Update"/> frame for the Edit-guard +
/// frame-scene-no-content invariants. No GraphicsDevice — a real <see cref="Camera"/> is constructed
/// (it is pure CPU math) and a hand-built cursor entity supplies the input.
/// </summary>
public class CameraNavTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    private static Entity MakeCursor(World world)
    {
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent());
        return cursor;
    }

    private static Entity MakeSprite(World world, Vector2 position, int size = 10)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(position));
        e.Set(new SpriteInfoComponent
        {
            Source = new Rectangle(0, 0, size, size),
            Size = new Vector2(size, size),
            Origin = Vector2.Zero,
            Target = RenderTargetID.Main,
        });
        return e;
    }

    // ---- Pan math: a drag of delta D at zoom Z moves Position by -D/Z (content follows the cursor) ----

    [Fact]
    public void Pan_AtZoomOne_MovesCameraOppositeTheDrag()
    {
        // Drag the cursor +50 in X (drag right). To keep the grabbed world point under the cursor the
        // camera must move LEFT (so the world content scrolls right with the cursor) → Position.X -= 50.
        var result = CameraNav.Pan(new Vector2(100, 100), new Vector2(50, 0), zoom: 1f);
        Assert.Equal(new Vector2(50, 100), result);
    }

    [Fact]
    public void Pan_AccountsForZoom()
    {
        // At zoom 2, one virtual pixel is half a world unit, so a 50px drag is only 25 world units.
        var result = CameraNav.Pan(new Vector2(100, 100), new Vector2(50, 0), zoom: 2f);
        Assert.Equal(new Vector2(75, 100), result);

        // At zoom 0.5, a 50px drag is 100 world units.
        var zoomedOut = CameraNav.Pan(new Vector2(100, 100), new Vector2(0, 50), zoom: 0.5f);
        Assert.Equal(new Vector2(100, 0), zoomedOut);
    }

    [Fact]
    public void Pan_ViaSystem_MiddleDrag_KeepsWorldPointUnderCursor()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600);
        camera.Position = new Vector2(100, 100);
        var cursor = MakeCursor(world);
        using var nav = new CameraNavSystem(world, camera);

        // Frame 1: press middle, anchor (no movement on the first frame of the drag).
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.MiddleButton = true;
        input.VirtualPosition = new Vector2(400, 300);
        nav.Update(Edit());
        Assert.Equal(new Vector2(100, 100), camera.Position); // anchored, not moved yet

        // Frame 2: still held, cursor moved +40 in X → camera pans -40 in X.
        input.VirtualPosition = new Vector2(440, 300);
        nav.Update(Edit());
        Assert.Equal(new Vector2(60, 100), camera.Position);
    }

    // ---- Zoom math: scroll multiplies + clamps ----

    [Fact]
    public void Zoom_ScrollIn_MultipliesUp_ScrollOut_MultipliesDown()
    {
        Assert.Equal(1.1f, CameraNav.Zoom(1f, scrollNotches: 1, stepFactor: 1.1f, min: 0.25f, max: 4f), 5);
        // One in + one out returns to the start (geometric, symmetric).
        var inThenOut = CameraNav.Zoom(CameraNav.Zoom(1f, 1, 1.1f, 0.25f, 4f), -1, 1.1f, 0.25f, 4f);
        Assert.Equal(1f, inThenOut, 5);
    }

    [Fact]
    public void Zoom_ClampsAtBounds()
    {
        // Many notches in clamps at max.
        Assert.Equal(4f, CameraNav.Zoom(1f, scrollNotches: 100, stepFactor: 1.1f, min: 0.25f, max: 4f), 5);
        // Many notches out clamps at min.
        Assert.Equal(0.25f, CameraNav.Zoom(1f, scrollNotches: -100, stepFactor: 1.1f, min: 0.25f, max: 4f), 5);
    }

    [Fact]
    public void Zoom_ViaSystem_ScrollStepsAndClamps()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600); // zoom 1
        var cursor = MakeCursor(world);
        using var nav = new CameraNavSystem(world, camera,
            zoomStep: 2f, minZoom: 0.25f, maxZoom: 4f);

        ref var input = ref cursor.Get<CursorInputComponent>();
        input.ScrollWheelDelta = 120; // one notch in
        nav.Update(Edit());
        Assert.Equal(2f, camera.Zoom, 5);

        input.ScrollWheelDelta = 120 * 5; // five notches in → clamps at max 4
        nav.Update(Edit());
        Assert.Equal(4f, camera.Zoom, 5);
    }

    // ---- Frame-scene: centre on the content AABB; no content = no-op ----

    [Fact]
    public void FrameScene_CentersOnContentAabb()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600);
        camera.Position = Vector2.Zero;

        // Two 10×10 sprites: one at (1000,-600), one at (1100,-500). AABB = (1000,-600)..(1110,-490);
        // centre = ((1000+1110)/2, (-600+-490)/2) = (1055, -545).
        MakeSprite(world, new Vector2(1000, -600));
        MakeSprite(world, new Vector2(1100, -500));
        MakeCursor(world);

        // Frame-scene is now the public FrameScene() the shortcut table (Home) + the view:frame op call.
        using var nav = new CameraNavSystem(world, camera);

        nav.FrameScene();

        Assert.Equal(1055f, camera.Position.X, 0);
        Assert.Equal(-545f, camera.Position.Y, 0);
        // A fit-zoom was applied (content fits the viewport), clamped to the sane range.
        Assert.InRange(camera.Zoom, 0.25f, 4f);
    }

    [Fact]
    public void FrameScene_NoContent_IsNoOp()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600);
        camera.Position = new Vector2(42, 7);
        var startZoom = camera.Zoom;
        MakeCursor(world);

        using var nav = new CameraNavSystem(world, camera);
        nav.FrameScene(); // no sprites → camera untouched

        Assert.Equal(new Vector2(42, 7), camera.Position);
        Assert.Equal(startZoom, camera.Zoom);
    }

    [Fact]
    public void ContentBounds_NoQuads_ReturnsNull()
    {
        Assert.Null(CameraNav.ContentBounds(Array.Empty<Vector2[]>()));
    }

    // ---- Frame-selected (HP): centre on the SELECTION, zoom kept; no selection = no-op ----

    [Fact]
    public void FrameSelected_CentersOnTheSelectedSpritesQuadCentre_AndKeepsZoom()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600);
        camera.Position = Vector2.Zero;
        camera.Zoom = 2.5f; // a deliberate zoom the focus must NOT change (a focus, not a fit)

        // A 10×10 top-left-origin sprite at (1000,-600) → world quad centre = (1005,-595).
        var sprite = MakeSprite(world, new Vector2(1000, -600));
        MakeSprite(world, new Vector2(-400, 400)); // unselected content must not pull the view
        sprite.Set(new MonoDreams.LevelEditor.Component.SelectedComponent());
        MakeCursor(world);

        using var nav = new CameraNavSystem(world, camera);

        Assert.True(nav.FrameSelected());
        Assert.Equal(1005f, camera.Position.X, 3);
        Assert.Equal(-595f, camera.Position.Y, 3);
        Assert.Equal(2.5f, camera.Zoom, 5);
    }

    [Fact]
    public void FrameSelected_SpritelessSelection_CentersOnItsWorldPosition()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600);
        camera.Position = Vector2.Zero;

        // A spriteless entity (a layer, a marker) focuses on its transform WORLD position — here a
        // child, so the parent offset must be included.
        var parent = world.CreateEntity();
        parent.Set(new TransformComponent(new Vector2(200, 50)));
        var child = world.CreateEntity();
        var childTransform = new TransformComponent(new Vector2(30, -10)) { Parent = parent.Get<TransformComponent>() };
        child.Set(childTransform);
        child.Set(new MonoDreams.LevelEditor.Component.SelectedComponent());
        MakeCursor(world);

        using var nav = new CameraNavSystem(world, camera);

        Assert.True(nav.FrameSelected());
        Assert.Equal(new Vector2(230, 40), camera.Position);
    }

    [Fact]
    public void FrameSelected_NoSelection_IsNoOpAndReportsFalse()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600);
        camera.Position = new Vector2(42, 7);
        var startZoom = camera.Zoom;
        MakeSprite(world, new Vector2(1000, 1000)); // content exists, but nothing is selected
        MakeCursor(world);

        using var nav = new CameraNavSystem(world, camera);

        Assert.False(nav.FrameSelected());
        Assert.Equal(new Vector2(42, 7), camera.Position);
        Assert.Equal(startZoom, camera.Zoom);
    }

    // ---- Edit-guarded: inert in Play (no camera mutation for pan/zoom/frame) ----

    [Fact]
    public void CameraNav_InPlayMode_IsInert()
    {
        using var world = new World();
        var camera = new MonoDreams.Component.Camera(800, 600);
        camera.Position = new Vector2(100, 100);
        var startZoom = camera.Zoom;
        MakeSprite(world, new Vector2(1000, 1000)); // content exists
        var cursor = MakeCursor(world);

        using var nav = new CameraNavSystem(world, camera);

        // Drive a middle-drag + scroll, all in Play — pan/zoom are Update-driven and Edit-guarded.
        // (Frame-scene is the public FrameScene() now; its Play-inertness is the shortcut context
        // gate, tested in EditorShortcutTests.)
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.MiddleButton = true;
        input.VirtualPosition = new Vector2(400, 300);
        input.ScrollWheelDelta = 120;
        nav.Update(Play());
        input.VirtualPosition = new Vector2(500, 300); // would pan -100 if active
        nav.Update(Play());

        // Play-guarded: nothing moved.
        Assert.Equal(new Vector2(100, 100), camera.Position);
        Assert.Equal(startZoom, camera.Zoom);
    }
}
