using System;
using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
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
/// Protects the native-resolution editor overlays (the "sharp overlays" directive): the pure
/// world/virtual → screen mapping (<see cref="OverlayProjection"/> — camera view matrix +
/// aspect-fit destination, sizes scaled by the fit factor and NEVER the camera zoom), the
/// viewport clipping (<see cref="OverlayMeshClip"/>), and the emission contract of
/// <c>GizmoSystem.EmitOverlays</c> / <c>ProxySyncSystem.EmitOverlays</c>: screen-baked meshes on
/// <c>RenderTargetID.Editor</c>, constant on-screen handle/stroke size at any zoom, geometry
/// confined to the game viewport rectangle. Pure logic — no GraphicsDevice (the
/// <see cref="ViewportManager"/> never dereferences its <c>Game</c>).
/// </summary>
public class OverlayProjectionTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };

    private static ViewportManager Vm(int screenW, int screenH, int virtualW = 800, int virtualH = 600)
        => new(null, virtualW, virtualH) { ScreenWidth = screenW, ScreenHeight = screenH };

    // ---- OverlayProjection: world → virtual (camera) → screen (aspect-fit destination) ----

    [Fact]
    public void WorldProjection_MapsThroughCameraAndDestination()
    {
        var camera = new GameCamera(800, 600) { Zoom = 1f, Position = Vector2.Zero };
        var vm = Vm(1600, 1200); // same 4:3 aspect → dest = full window, fit factor 2

        var projection = OverlayProjection.For(RenderTargetID.Main, camera, vm);

        // World origin sits at the virtual centre (400,300) → screen (800,600).
        Assert.Equal(new Vector2(800, 600), projection.ToScreen(Vector2.Zero));
        // World (100,50) → virtual (500,350) → screen (1000,700).
        Assert.Equal(new Vector2(1000, 700), projection.ToScreen(new Vector2(100, 50)));
        Assert.Equal(new Rectangle(0, 0, 1600, 1200), projection.Viewport);
    }

    [Fact]
    public void WorldProjection_HonorsTheViewportInset()
    {
        var camera = new GameCamera(800, 600) { Zoom = 1f, Position = Vector2.Zero };
        var vm = Vm(1600, 1200);
        vm.SetViewportInset(0, 100, 200, 0); // available 1400×1100 at (0,100) → dest 1400×1050 at (0,125)

        var projection = OverlayProjection.For(RenderTargetID.Main, camera, vm);

        Assert.Equal(new Rectangle(0, 125, 1400, 1050), projection.Viewport);
        // Fit factor 1400/800 = 1.75: world origin → virtual (400,300) → (700, 525+125).
        Assert.Equal(new Vector2(700, 650), projection.ToScreen(Vector2.Zero));
    }

    [Fact]
    public void ZoomMovesProjectedPoints_ButNeverEmittedSizes()
    {
        var vm = Vm(1600, 1200);
        var zoom1 = OverlayProjection.For(RenderTargetID.Main,
            new GameCamera(800, 600) { Zoom = 1f, Position = Vector2.Zero }, vm);
        var zoom4 = OverlayProjection.For(RenderTargetID.Main,
            new GameCamera(800, 600) { Zoom = 4f, Position = Vector2.Zero }, vm);

        // Zoom transforms world GEOMETRY: an off-centre point lands further from the centre.
        Assert.NotEqual(zoom1.ToScreen(new Vector2(100, 50)), zoom4.ToScreen(new Vector2(100, 50)));
        // But an emitted size (line width, handle radius) is zoom-INDEPENDENT: only the
        // aspect-fit factor (1600/800 = 2) applies.
        Assert.Equal(4f, zoom1.ToScreenSize(2f));
        Assert.Equal(zoom1.ToScreenSize(2f), zoom4.ToScreenSize(2f));
    }

    [Fact]
    public void VirtualProjection_IgnoresTheCamera()
    {
        // A camera looking somewhere else entirely: a UI/HUD-space point must not care.
        var camera = new GameCamera(800, 600) { Zoom = 3f, Position = new Vector2(9000, 9000) };
        var vm = Vm(1600, 1200);

        var projection = OverlayProjection.For(RenderTargetID.HUD, camera, vm);

        Assert.Equal(new Vector2(200, 100), projection.ToScreen(new Vector2(100, 50)));
    }

    [Fact]
    public void NullViewportManager_DegradesToIdentityFit()
    {
        var camera = new GameCamera(800, 600) { Zoom = 1f, Position = Vector2.Zero };

        var projection = OverlayProjection.For(RenderTargetID.Main, camera, null);

        Assert.Equal(new Vector2(400, 300), projection.ToScreen(Vector2.Zero)); // screen == virtual
        Assert.Equal(2f, projection.ToScreenSize(2f));
        Assert.Equal(new Rectangle(0, 0, 800, 600), projection.Viewport);
    }

    // ---- OverlayMeshClip: meshes respect the game viewport rectangle ----

    private static MeshData Triangle(Vector2 a, Vector2 b, Vector2 c) => new(
        new[]
        {
            new VertexPositionColor(new Vector3(a, 0f), Color.Cyan),
            new VertexPositionColor(new Vector3(b, 0f), Color.Cyan),
            new VertexPositionColor(new Vector3(c, 0f), Color.Cyan),
        },
        new[] { 0, 1, 2 });

    [Fact]
    public void Clip_FullyInsideMesh_IsReturnedUnchanged()
    {
        var mesh = Triangle(new Vector2(10, 10), new Vector2(50, 10), new Vector2(30, 50));

        var clipped = OverlayMeshClip.ClipToRect(mesh, new Rectangle(0, 0, 100, 100));

        Assert.Same(mesh.Vertices, clipped.Vertices); // no re-allocation for the common case
        Assert.Same(mesh.Indices, clipped.Indices);
    }

    [Fact]
    public void Clip_FullyOutsideMesh_BecomesEmpty()
    {
        var mesh = Triangle(new Vector2(200, 200), new Vector2(250, 200), new Vector2(225, 250));

        var clipped = OverlayMeshClip.ClipToRect(mesh, new Rectangle(0, 0, 100, 100));

        Assert.False(clipped.IsValid); // filtered out by DrawComponent.HasValidMesh downstream
    }

    [Fact]
    public void Clip_StraddlingMesh_IsCutAtTheBounds()
    {
        var bounds = new Rectangle(0, 0, 100, 100);
        var mesh = Triangle(new Vector2(50, 50), new Vector2(150, 50), new Vector2(50, 150));

        var clipped = OverlayMeshClip.ClipToRect(mesh, bounds);

        Assert.True(clipped.IsValid);
        Assert.Equal(PrimitiveType.TriangleList, clipped.PrimitiveType);
        Assert.Equal(0, clipped.Indices.Length % 3);
        foreach (var v in clipped.Vertices)
        {
            Assert.InRange(v.Position.X, bounds.Left, bounds.Right);
            Assert.InRange(v.Position.Y, bounds.Top, bounds.Bottom);
        }
        // The inside corner survived the cut.
        Assert.Contains(clipped.Vertices, v => v.Position.X == 50f && v.Position.Y == 50f);
    }

    // ---- Emission: screen-baked, Editor-target, constant size at any zoom, clipped ----

    private static (Entity entity, GizmoSystem gizmo) SelectedSprite(World world, GameCamera camera, ViewportManager vm)
    {
        var entity = world.CreateEntity();
        entity.Set(new TransformComponent(new Vector2(64, 32)));
        entity.Set(new SpriteInfoComponent
        {
            Source = new Rectangle(0, 0, 32, 32),
            Size = new Vector2(32, 32),
            Target = RenderTargetID.Main,
        });
        entity.Set(new SelectedComponent());
        var gizmo = new GizmoSystem(world, camera, new EditorHistory(world), vm);
        return (entity, gizmo);
    }

    private static (Vector2 min, Vector2 max) MeshBounds(Entity overlay)
    {
        ref readonly var dc = ref overlay.Get<DrawComponent>();
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var v in dc.Vertices!)
        {
            min = Vector2.Min(min, new Vector2(v.Position.X, v.Position.Y));
            max = Vector2.Max(max, new Vector2(v.Position.X, v.Position.Y));
        }
        return (min, max);
    }

    private static Entity FindHandle(World world)
    {
        // The handle is the overlay whose mesh contains the projected pivot circle — but for the
        // size assertion any distinction works: take the overlay with the SMALLER bounding box
        // (the outline traces the whole sprite; the move handle is compact).
        Entity best = default;
        var bestArea = float.MaxValue;
        using var overlays = world.GetEntities().With<GizmoOverlayComponent>().AsSet();
        foreach (var overlay in overlays.GetEntities())
        {
            var (min, max) = MeshBounds(overlay);
            var area = (max.X - min.X) * (max.Y - min.Y);
            if (area < bestArea)
            {
                bestArea = area;
                best = overlay;
            }
        }
        return best;
    }

    [Fact]
    public void GizmoEmission_HandleSizeIsConstantAcrossZoom_AndLandsOnTheEditorTarget()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600) { Zoom = 1f, Position = new Vector2(64, 32) };
        var vm = Vm(1600, 1200);
        var (_, gizmo) = SelectedSprite(world, camera, vm);
        using var g = gizmo;

        gizmo.EmitOverlays(Edit());
        var handle = FindHandle(world);
        Assert.Equal(RenderTargetID.Editor, handle.Get<DrawComponent>().Target);
        Assert.False(handle.Has<VisibleComponent>());
        var (min1, max1) = MeshBounds(handle);

        camera.Zoom = 2.5f;
        gizmo.EmitOverlays(Edit());
        var (min2, max2) = MeshBounds(FindHandle(world));

        // Constant on-screen handle size: zoom moves geometry, never fattens/shrinks the handle.
        Assert.Equal(max1.X - min1.X, max2.X - min2.X, 2);
        Assert.Equal(max1.Y - min1.Y, max2.Y - min2.Y, 2);
    }

    [Fact]
    public void GizmoEmission_OutlineTracksZoomedGeometry_InScreenPixels()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600) { Zoom = 1f, Position = new Vector2(64, 32) };
        var vm = Vm(1600, 1200); // fit factor 2
        var (_, gizmo) = SelectedSprite(world, camera, vm);
        using var g = gizmo;

        gizmo.EmitOverlays(Edit());
        // Outline = the bigger overlay (traces the 32×32 sprite quad).
        Entity outline = default;
        var bestArea = float.MinValue;
        using (var overlays = world.GetEntities().With<GizmoOverlayComponent>().AsSet())
        {
            foreach (var overlay in overlays.GetEntities())
            {
                var (min, max) = MeshBounds(overlay);
                var area = (max.X - min.X) * (max.Y - min.Y);
                if (area > bestArea)
                {
                    bestArea = area;
                    outline = overlay;
                }
            }
        }

        // A 32-unit world quad at zoom 1 through fit 2 ≈ 64 screen px (+ stroke overhang).
        var (outMin, outMax) = MeshBounds(outline);
        Assert.InRange(outMax.X - outMin.X, 60f, 76f);
        Assert.InRange(outMax.Y - outMin.Y, 60f, 76f);
    }

    [Fact]
    public void GizmoEmission_ClipsOverlaysToTheGameViewport()
    {
        using var world = new World();
        // Camera far from the entity: the sprite sits near the right viewport edge.
        var camera = new GameCamera(800, 600) { Zoom = 1f, Position = new Vector2(-330, 0) };
        var vm = Vm(1600, 1200);
        vm.SetViewportInset(0, 100, 200, 0);
        var (entity, gizmo) = SelectedSprite(world, camera, vm);
        entity.Get<TransformComponent>().Position = new Vector2(64, 0);
        using var g = gizmo;

        gizmo.EmitOverlays(Edit());

        var viewport = OverlayProjection.For(RenderTargetID.Main, camera, vm).Viewport;
        using var overlays = world.GetEntities().With<GizmoOverlayComponent>().AsSet();
        foreach (var overlay in overlays.GetEntities())
        {
            ref readonly var dc = ref overlay.Get<DrawComponent>();
            if (dc.Vertices == null) continue;
            foreach (var v in dc.Vertices)
            {
                Assert.InRange(v.Position.X, viewport.Left, viewport.Right);
                Assert.InRange(v.Position.Y, viewport.Top, viewport.Bottom);
            }
        }
    }

    [Fact]
    public void ProxyEmission_BakesScreenSpaceHandles_OnTheEditorTarget()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600) { Zoom = 1f, Position = Vector2.Zero };
        var vm = Vm(1600, 1200); // fit factor 2, dest at origin

        // Colliders-as-entities: the surviving proxies are a convex collider ENTITY's vertex GRIPS
        // (the whole-shape box/convex proxies retired). A triangle at (35,60) with model vertices
        // placing vertex 0 at world (10,20).
        var collider = world.CreateEntity();
        collider.Set(new TransformComponent(new Vector2(35, 60)));
        collider.Set(new ConvexColliderComponent(new[]
        {
            new Vector2(-25, -40), new Vector2(25, -40), new Vector2(0, 40),
        }));
        collider.Set(new SelectedComponent());

        using var sync = new ProxySyncSystem(world, camera, vm);
        sync.Update(Edit());       // grips materialize on selecting the collider entity
        sync.EmitOverlays(Edit());

        using var proxies = world.GetEntities().With<GizmoProxyComponent>().AsSet();
        var seen = 0;
        Entity v0 = default;
        foreach (var proxy in proxies.GetEntities())
        {
            seen++;
            ref readonly var dc = ref proxy.Get<DrawComponent>();
            Assert.Equal(RenderTargetID.Editor, dc.Target);
            Assert.True(dc.HasValidMesh);
            if (proxy.Get<GizmoProxyComponent>().Index == 0) v0 = proxy;
        }
        Assert.Equal(3, seen); // one grip per vertex

        // Vertex 0's world position (10,20) → virtual (+400,+300) → screen ×2 = (820,640): the handle
        // square is baked centred there (a constant-on-screen square), proving the projection.
        var (min, max) = MeshBounds(v0);
        var center = (min + max) / 2f;
        Assert.InRange(center.X, 814f, 826f);
        Assert.InRange(center.Y, 634f, 646f);
    }
}
