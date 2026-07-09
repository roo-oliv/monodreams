using System;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Transform;
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Renderer;
using MonoDreams.State;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the viewport Overlays wave (UX3-D §3/§6/§7): the settings-component defaults, the
/// <see cref="ViewportOverlayOps"/> op/menu channels (with the ONE spacing value = the gizmo snap
/// step), the <see cref="EditorGrid"/> emission (lines at the shared spacing, viewport-clipped,
/// Editor target, no VisibleComponent, bounded), and the three gates: the selection-outline
/// suppression, the camera-glyph suppression, and the Game-mode hide. Pure/logic — no GraphicsDevice
/// (the <see cref="ViewportManager"/> never dereferences its Game).
/// </summary>
public class ViewportOverlayTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    private static ViewportManager Vm(int screenW, int screenH, int virtualW = 800, int virtualH = 600)
        => new(null, virtualW, virtualH) { ScreenWidth = screenW, ScreenHeight = screenH, DevicePixelRatio = 1f };

    // ═══ Settings component defaults ═════════════════════════════════════════════════════════════════

    [Fact]
    public void Settings_Defaults_GridOff_OutlineOn_CameraOn()
    {
        var s = ViewportOverlaySettingsComponent.Default;
        Assert.False(s.ShowGrid);        // preserves the current look
        Assert.True(s.OutlineSelected);
        Assert.True(s.ShowCameraGlyph);
    }

    // ═══ Ops: overlay:* + the ONE spacing value (= the gizmo snap step) ══════════════════════════════

    [Fact]
    public void Op_TogglesTheThreeFlags_On_And_Off()
    {
        var s = ViewportOverlaySettingsComponent.Default;
        var g = GizmoStateComponent.Default;

        Assert.True(ViewportOverlayOps.TryApplyOp("overlay:grid on", ref s, ref g));
        Assert.True(s.ShowGrid);
        Assert.True(ViewportOverlayOps.TryApplyOp("overlay:grid off", ref s, ref g));
        Assert.False(s.ShowGrid);

        Assert.True(ViewportOverlayOps.TryApplyOp("overlay:outline off", ref s, ref g));
        Assert.False(s.OutlineSelected);
        Assert.True(ViewportOverlayOps.TryApplyOp("overlay:camera off", ref s, ref g));
        Assert.False(s.ShowCameraGlyph);
    }

    [Fact]
    public void Op_Spacing_WritesTheSharedGridStep_NoSecondCopy()
    {
        var s = ViewportOverlaySettingsComponent.Default;
        var g = GizmoStateComponent.Default; // GridStep 16 by default

        Assert.True(ViewportOverlayOps.TryApplyOp("overlay:spacing 32", ref s, ref g));
        Assert.Equal(32f, g.GridStep); // the op edits the single authoritative snap step
    }

    [Fact]
    public void Op_Unrecognized_ReturnsFalse_ForTheCallerToLog()
    {
        var s = ViewportOverlaySettingsComponent.Default;
        var g = GizmoStateComponent.Default;
        Assert.False(ViewportOverlayOps.TryApplyOp("overlay:bogus 1", ref s, ref g));
        Assert.False(ViewportOverlayOps.TryApplyOp("overlay:grid maybe", ref s, ref g));
        Assert.False(ViewportOverlayOps.TryApplyOp("overlay:spacing -4", ref s, ref g));
        Assert.False(ViewportOverlayOps.TryApplyOp("notoverlay:x", ref s, ref g));
    }

    [Fact]
    public void MenuPath_Toggles_Flip_And_SpacingPreset_Sets()
    {
        var s = ViewportOverlaySettingsComponent.Default;
        var g = GizmoStateComponent.Default;

        Assert.True(ViewportOverlayOps.TryApplyMenuPath(ViewportOverlayOps.GridTogglePath, ref s, ref g));
        Assert.True(s.ShowGrid);
        Assert.True(ViewportOverlayOps.TryApplyMenuPath(ViewportOverlayOps.GridTogglePath, ref s, ref g));
        Assert.False(s.ShowGrid); // a toggle flips back

        Assert.True(ViewportOverlayOps.TryApplyMenuPath(ViewportOverlayOps.SpacingPath(64f), ref s, ref g));
        Assert.Equal(64f, g.GridStep); // the same shared field the op writes
        Assert.False(ViewportOverlayOps.TryApplyMenuPath("order/forward", ref s, ref g)); // not an overlay path
    }

    [Fact]
    public void Spacing_IsTheSnapStep_OneValue_BothDirections()
    {
        var s = ViewportOverlaySettingsComponent.Default;
        var g = GizmoStateComponent.Default;
        g.SnapEnabled = true;

        // Change spacing via the menu/op → the gizmo now snaps at the new step (SnapStep reads GridStep).
        ViewportOverlayOps.TryApplyOp("overlay:spacing 32", ref s, ref g);
        Assert.Equal(32f, g.GridStep);
        Assert.Equal(new Vector2(64f, 0f), GizmoTransform.Snap(new Vector2(50f, 0f), g.GridStep));

        // Change the snap step elsewhere → the SAME field the grid reads for its spacing.
        g.GridStep = 8f;
        Assert.Equal(new Vector2(48f, 0f), GizmoTransform.Snap(new Vector2(50f, 0f), g.GridStep));
    }

    // ═══ Grid emission ═══════════════════════════════════════════════════════════════════════════════

    private static (EditorGrid grid, Entity gizmo) MakeGrid(World world, GameCamera camera,
        ViewportManager? vm, float spacing = 16f, Func<bool>? visible = null)
    {
        var gizmo = world.CreateEntity();
        var gs = GizmoStateComponent.Default;
        gs.GridStep = spacing;
        gizmo.Set(gs);
        var grid = new EditorGrid(world, camera, vm,
            () => gizmo.Get<GizmoStateComponent>().GridStep,
            visible ?? (() => true));
        return (grid, gizmo);
    }

    private static int VertexCount(EditorGrid grid) =>
        grid.Entity.Get<DrawComponent>().Vertices?.Length ?? 0;

    [Fact]
    public void Grid_BakesAnEditorTargetMesh_NoVisibleComponent_WhenOnAndInEdit()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600) { Zoom = 1f, Position = Vector2.Zero };
        var (grid, _) = MakeGrid(world, camera, Vm(1600, 1200), spacing: 16f);

        grid.EmitGrid(Edit());

        ref readonly var dc = ref grid.Entity.Get<DrawComponent>();
        Assert.Equal(RenderTargetID.Editor, dc.Target);
        Assert.Equal(DrawElementType.Mesh, dc.Type);
        Assert.Equal(EditorTheme.Depths.Grid, dc.LayerDepth);
        Assert.False(grid.Entity.Has<VisibleComponent>()); // chrome rule
        Assert.True(dc.Vertices!.Length > 0);
        // The grid's depth is beneath every other overlay (proxy 0.02 < gizmo 0.04, glyph 0.03).
        Assert.True(EditorTheme.Depths.Grid < EditorTheme.Depths.ProxyOverlay);
    }

    [Fact]
    public void Grid_FollowsTheSharedGridStep_ChangingItViaTheOpReSpacesTheGrid()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600) { Zoom = 1f, Position = Vector2.Zero };
        var vm = Vm(1600, 1200);
        var (grid, gizmo) = MakeGrid(world, camera, vm, spacing: 16f);

        grid.EmitGrid(Edit());
        var dense = VertexCount(grid);
        Assert.True(dense > 0);

        // Change the ONE shared step through the op channel; the grid reads the SAME field.
        var s = ViewportOverlaySettingsComponent.Default;
        ref var gs = ref gizmo.Get<GizmoStateComponent>();
        Assert.True(ViewportOverlayOps.TryApplyOp("overlay:spacing 64", ref s, ref gs));

        grid.EmitGrid(Edit());
        var sparse = VertexCount(grid);
        Assert.True(sparse > 0);
        Assert.True(sparse < dense, $"64-unit spacing ({sparse}) should be sparser than 16-unit ({dense})");
    }

    [Fact]
    public void Grid_ClipsToTheGameViewport_UnderAnInset_DevicePixelDestination()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600) { Zoom = 1f, Position = Vector2.Zero };
        var vm = Vm(1600, 1200);
        vm.SetViewportInset(0, 100, 200, 0); // available 1400×1100 → aspect-fit destination inside it
        var (grid, _) = MakeGrid(world, camera, vm, spacing: 16f);

        grid.EmitGrid(Edit());

        var viewport = OverlayProjection.For(RenderTargetID.Main, camera, vm).Viewport;
        ref readonly var dc = ref grid.Entity.Get<DrawComponent>();
        Assert.True(dc.Vertices!.Length > 0);
        foreach (var v in dc.Vertices)
        {
            Assert.InRange(v.Position.X, viewport.Left, viewport.Right);
            Assert.InRange(v.Position.Y, viewport.Top, viewport.Bottom);
        }
    }

    [Fact]
    public void Grid_HiddenInPlay_AndWhenTheGateIsFalse()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600) { Zoom = 1f, Position = Vector2.Zero };
        var vm = Vm(1600, 1200);

        var (grid, _) = MakeGrid(world, camera, vm, spacing: 16f, visible: () => true);
        grid.EmitGrid(Play()); // Edit-only, like the other overlays
        Assert.Equal(0, VertexCount(grid));
        grid.EmitGrid(Edit());
        Assert.True(VertexCount(grid) > 0);

        // The gate captures ShowGrid && ViewMode != Game — a false gate (grid off, or the sandbox) hides it.
        var (offGrid, _) = MakeGrid(world, camera, vm, spacing: 16f, visible: () => false);
        offGrid.EmitGrid(Edit());
        Assert.Equal(0, VertexCount(offGrid));
    }

    [Fact]
    public void Grid_PathologicalZoomOut_DegradesToNothing_NoUnboundedMesh()
    {
        using var world = new World();
        // Max zoom-out (0.1) over a tiny spacing → GridGeometry.None → the grid is meaningless here.
        var camera = new GameCamera(800, 600) { Zoom = 0.1f, Position = Vector2.Zero };
        var (grid, _) = MakeGrid(world, camera, Vm(1600, 1200), spacing: 1f);

        grid.EmitGrid(Edit());
        Assert.Equal(0, VertexCount(grid));
    }

    [Fact]
    public void Grid_ModerateZoomOut_StaysBounded_MajorOnly()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600) { Zoom = 0.1f, Position = Vector2.Zero };
        var (grid, _) = MakeGrid(world, camera, Vm(1600, 1200), spacing: 16f);

        grid.EmitGrid(Edit());
        var n = VertexCount(grid);
        Assert.True(n > 0);
        // O(cap), never O(1/zoom): far under the pre-mortem #5 bound (2·cap lines, ≤ a handful of
        // vertices each after clipping).
        Assert.True(n <= 2 * GridGeometry.MinorLineCapPerAxis * 16,
            $"grid vertex count {n} is not bounded by the cap");
    }

    // ═══ Gate: selection outline (OutlineSelected) — suppresses only the outline ═════════════════════

    private static (Entity entity, GizmoSystem gizmo) SelectedSpriteGated(
        World world, GameCamera camera, ViewportManager vm,
        Func<bool> overlaysVisible, Func<bool> outlineVisible)
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
        var gizmo = new GizmoSystem(world, camera, new EditorHistory(world), vm,
            overlaysVisible, outlineVisible);
        return (entity, gizmo);
    }

    private static int OverlayEntityCount(World world)
    {
        using var set = world.GetEntities().With<GizmoOverlayComponent>().AsSet();
        var n = 0;
        foreach (var _ in set.GetEntities()) n++;
        return n;
    }

    private static int NonEmptyOverlays(World world)
    {
        using var set = world.GetEntities().With<GizmoOverlayComponent>().AsSet();
        var n = 0;
        foreach (var e in set.GetEntities())
            if ((e.Get<DrawComponent>().Vertices?.Length ?? 0) > 0) n++;
        return n;
    }

    [Fact]
    public void OutlineSelectedOff_SuppressesOnlyTheOutline_HandleStays_SelectionUnaffected()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600) { Zoom = 1f, Position = new Vector2(64, 32) };
        var vm = Vm(1600, 1200);
        var outline = true;
        var (entity, gizmo) = SelectedSpriteGated(world, camera, vm,
            overlaysVisible: () => true, outlineVisible: () => outline);
        using var g = gizmo;

        gizmo.EmitOverlays(Edit());
        Assert.Equal(2, OverlayEntityCount(world)); // outline + handle
        Assert.Equal(2, NonEmptyOverlays(world));   // both drawn

        outline = false;
        gizmo.EmitOverlays(Edit());
        Assert.Equal(2, OverlayEntityCount(world));  // both entities still exist
        Assert.Equal(1, NonEmptyOverlays(world));    // only the handle draws (outline parked)
        Assert.True(entity.Has<SelectedComponent>()); // selection itself is unaffected
    }

    [Fact]
    public void GameMode_HidesAllGizmoOverlays_TheSandboxLooksLikeTheGame()
    {
        using var world = new World();
        var camera = new GameCamera(800, 600) { Zoom = 1f, Position = new Vector2(64, 32) };
        var vm = Vm(1600, 1200);
        var overlaysVisible = true;
        var (_, gizmo) = SelectedSpriteGated(world, camera, vm,
            overlaysVisible: () => overlaysVisible, outlineVisible: () => true);
        using var g = gizmo;

        gizmo.EmitOverlays(Edit());
        Assert.Equal(2, NonEmptyOverlays(world));

        overlaysVisible = false; // the Game-mode sandbox
        gizmo.EmitOverlays(Edit());
        Assert.Equal(0, OverlayEntityCount(world)); // handle AND outline gone
    }

    // ═══ Gate: camera-rig glyph (ShowCameraGlyph / Game mode) ════════════════════════════════════════

    [Fact]
    public void CameraGlyphGate_Off_HidesTheFrustum_EvenWhenTheViewDiffersFromTheRig()
    {
        using var world = new World();
        var view = new GameCamera(800, 600);
        var visible = true;
        var rig = new EditorCameraRig(world, view, viewportManager: null, glyphVisible: () => visible);

        // View navigated off the rig → the glyph normally shows in Edit.
        view.Position = new Vector2(50f, 0f);
        rig.EmitGlyph(Edit());
        Assert.NotEmpty(rig.Entity.Get<DrawComponent>().Vertices);

        // "Camera" overlay off (or the Game-mode sandbox — the same gate) → hidden entirely.
        visible = false;
        rig.EmitGlyph(Edit());
        Assert.Empty(rig.Entity.Get<DrawComponent>().Vertices);
    }
}
