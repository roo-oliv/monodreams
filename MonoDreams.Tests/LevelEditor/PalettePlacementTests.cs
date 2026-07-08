#nullable enable
using System.Collections.Generic;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Assets;
using MonoDreams.LevelEditor.Channel;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;
using Xunit;
using GameCamera = MonoDreams.Component.Camera;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects island-authoring Slice 1's tool modality + ghost + placement over the REAL systems,
/// in-process (the <c>SelectionTests</c>/<c>GizmoTests</c> style — no GraphicsDevice, no chrome:
/// the palette runs in its headless form, viewportManager/font null):
///
/// <list type="bullet">
///   <item><b>Modality (§S1):</b> in <see cref="EditorToolMode.Place"/> a viewport press neither
///   selects nor starts a gizmo drag; Escape / right-click restore
///   <see cref="EditorToolMode.SelectTransform"/>.</item>
///   <item><b>Ghost lifecycle:</b> follows the cursor's world position, snap-quantized via the
///   shared snap settings, parked while <c>OutsideViewport</c>, despawned on disarm; editor
///   infrastructure, never a scene object.</item>
///   <item><b>Placement:</b> one press = one <c>CreateEntityCommand</c> undo step building the
///   standard prop stack (tagged <c>SceneObjectComponent</c>, auto-selected); undo removes it;
///   repeated presses keep placing.</item>
///   <item><b>Headless channel:</b> a scripted <c>ToolbarAction</c> op's raw string (the
///   <c>palette:&lt;id&gt;</c> grammar) reaches the named dispatch.</item>
/// </list>
/// </summary>
public class PalettePlacementTests
{
    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    private static Entity MakeGizmoState(World world)
    {
        var e = world.CreateEntity();
        e.Set(new EditorInfrastructureComponent());
        e.Set(GizmoStateComponent.Default);
        return e;
    }

    private static Entity MakeCursor(World world)
    {
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent());
        return cursor;
    }

    private static void SetCursor(Entity cursor, Vector2 world, bool leftPressed = false,
        bool leftDown = false, bool leftReleased = false, bool rightPressed = false,
        bool outsideViewport = false)
    {
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.WorldPosition = world;
        input.VirtualPosition = world;
        input.LeftButtonPressed = leftPressed;
        input.LeftButton = (leftDown || leftPressed) && !leftReleased;
        input.LeftButtonReleased = leftReleased;
        input.RightButtonPressed = rightPressed;
        input.OutsideViewport = outsideViewport;
    }

    private static Entity MakeSprite(World world, Vector2 position, float finalDepth = 0.5f)
    {
        var e = world.CreateEntity();
        e.Set(new TransformComponent(position));
        e.Set(new SpriteInfoComponent
        {
            Source = new Rectangle(0, 0, 10, 10),
            Size = new Vector2(10, 10),
            Target = RenderTargetID.Main,
        });
        e.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main, LayerDepth = finalDepth });
        e.Set(new VisibleComponent());
        return e;
    }

    private static AssetCatalog MakeCatalog() => new(new[]
    {
        new AssetCatalogEntry("Island/props/tree01.png", null, null, "tree01", "props"),
        new AssetCatalogEntry("Island/props/sheet.png", "trunk", new Rectangle(0, 0, 32, 48), "sheet#trunk", "props"),
    });

    private static readonly PaletteBand[] Bands =
    {
        new("Ground", 0.9f, YSorted: false),
        new("Props", 0.45f, YSorted: true),
    };

    private static FileAssetTextureLoader MakeLoader() => new(
        openStream: _ => null, decode: _ => null, createPlaceholder: () => null);

    private static PalettePlacementSystem MakePalette(World world, EditorHistory history,
        SceneSerializer serializer, global::System.Func<GameState, bool>? cancel = null) =>
        new(world, MakeCatalog(), Bands, MakeLoader(), serializer, history,
            viewportManager: null, font: null, cancelRequested: cancel);

    private static (SceneSerializer Serializer, EditorHistory History) MakeInfra(World world)
    {
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        return (new SceneSerializer(registry), new EditorHistory(world));
    }

    private static Entity? Selected(World world)
    {
        using var set = world.GetEntities().With<SelectedComponent>().AsSet();
        foreach (var e in set.GetEntities()) return e;
        return null;
    }

    private static List<Entity> PlacedProps(World world)
    {
        var list = new List<Entity>();
        using var set = world.GetEntities().With<SceneObjectComponent>().AsSet();
        foreach (var e in set.GetEntities()) list.Add(e);
        return list;
    }

    // ---- Tool modality (§S1) ----

    [Fact]
    public void ToolModalityTest_PlaceModePressNeitherSelectsNorDrags()
    {
        using var world = new World();
        var gizmoState = MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var sprite = MakeSprite(world, new Vector2(0, 0));
        var camera = new GameCamera(800, 600) { Zoom = 1f };
        var (_, history) = MakeInfra(world);

        using var selection = new SelectionSystem(world, camera);
        using var gizmo = new GizmoSystem(world, camera, history);

        // First: select the sprite in SelectTransform (baseline sanity).
        SetCursor(cursor, new Vector2(5, 5), leftPressed: true);
        gizmo.Update(Edit());
        selection.Update(Edit());
        Assert.Equal(sprite, Selected(world));

        // Enter Place mode (what arming the palette does on the shared state entity).
        {
            ref var state = ref gizmoState.Get<GizmoStateComponent>();
            state.Mode = EditorToolMode.Place;
        }

        // A press over EMPTY space — which in SelectTransform would clear the selection — is not
        // a selection click in Place mode.
        SetCursor(cursor, new Vector2(200, 200), leftPressed: true);
        gizmo.Update(Edit());
        selection.Update(Edit());
        Assert.Equal(sprite, Selected(world)); // no re-pick, no click-empty clear

        // A press on the selected pivot — the move handle's exact spot — starts no drag, and the
        // gizmo claims nothing (the mode, not the claim, mutes selection).
        var before = sprite.Get<TransformComponent>().Position;
        SetCursor(cursor, new Vector2(0, 0), leftPressed: true);
        gizmo.Update(Edit());
        selection.Update(Edit());
        Assert.False(gizmoState.Get<GizmoStateComponent>().PressClaimed);

        SetCursor(cursor, new Vector2(40, 24), leftDown: true); // would drag in SelectTransform
        gizmo.Update(Edit());
        SetCursor(cursor, new Vector2(40, 24)); // release
        gizmo.Update(Edit());
        Assert.Equal(before, sprite.Get<TransformComponent>().Position);
        Assert.Equal(0, history.Count); // no transform edit was recorded

        // Back in SelectTransform the same empty-space press DOES clear (the control case).
        {
            ref var state = ref gizmoState.Get<GizmoStateComponent>();
            state.Mode = EditorToolMode.SelectTransform;
        }
        SetCursor(cursor, new Vector2(200, 200), leftPressed: true);
        gizmo.Update(Edit());
        selection.Update(Edit());
        Assert.Null(Selected(world));
    }

    [Fact]
    public void ToolModalityTest_EscapeRestoresSelectTransform()
    {
        using var world = new World();
        var gizmoState = MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);

        var cancel = false;
        using var palette = MakePalette(world, history, serializer, _ => cancel);

        palette.ArmByIndex(0);
        SetCursor(cursor, new Vector2(10, 10));
        palette.Update(Edit());
        Assert.Equal(EditorToolMode.Place, gizmoState.Get<GizmoStateComponent>().Mode);
        Assert.True(palette.HasGhost);

        cancel = true; // Escape
        palette.Update(Edit());

        Assert.Equal(EditorToolMode.SelectTransform, gizmoState.Get<GizmoStateComponent>().Mode);
        Assert.Null(palette.ArmedEntry);
        Assert.False(palette.HasGhost);
    }

    [Fact]
    public void ToolModalityTest_RightClickDisarms()
    {
        using var world = new World();
        var gizmoState = MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);
        using var palette = MakePalette(world, history, serializer);

        palette.ArmByIndex(0);
        SetCursor(cursor, new Vector2(10, 10));
        palette.Update(Edit());
        Assert.True(palette.HasGhost);

        SetCursor(cursor, new Vector2(10, 10), rightPressed: true);
        palette.Update(Edit());

        Assert.Equal(EditorToolMode.SelectTransform, gizmoState.Get<GizmoStateComponent>().Mode);
        Assert.False(palette.HasGhost);
    }

    // ---- Ghost lifecycle ----

    [Fact]
    public void GhostLifecycleTest_FollowsCursorSnapsParksAndDespawns()
    {
        using var world = new World();
        var gizmoState = MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);
        using var palette = MakePalette(world, history, serializer);

        palette.ArmByIndex(1); // the sliced Props entry (Y-sorted band selected below)
        palette.SelectBand("Props");

        // Follows the cursor's world position — CENTRED: the transform position offsets by the
        // source-space centre↔origin delta so the sprite's VISUAL centre (not its feet) sits under the
        // cursor. For the 32×48 Y-sorted region: centre (16,24) − feet origin (16,48) = (0,−24), so
        // the feet land 24 below the cursor → position = (50,60) − (0,−24) = (50,84).
        SetCursor(cursor, new Vector2(50, 60));
        palette.Update(Edit());
        Assert.True(palette.HasGhost);
        var ghost = palette.Ghost;
        Assert.Equal(new Vector2(50, 84), ghost.Get<TransformComponent>().Position);

        // Editor infrastructure, never scene content, tinted, feet-origin on the Y-sorted band.
        Assert.True(ghost.Has<EditorInfrastructureComponent>());
        Assert.False(ghost.Has<SceneObjectComponent>());
        var ghostSprite = ghost.Get<SpriteInfoComponent>();
        Assert.Equal(PalettePlacementSystem.GhostColor, ghostSprite.Color);
        Assert.Equal(new Vector2(16f, 48f), ghostSprite.Origin); // bottom-center of the 32×48 region
        Assert.Null(ghostSprite.AssetKey); // key-less: not a save candidate even by accident

        // Snap quantizes the ghost (and thus the placement) position — the transform/feet position,
        // the SAME field snap quantized before centring: raw = (100.4,77.7) − (0,−24) = (100.4,101.7),
        // snapped to the 16 grid = (96,96).
        ref var state = ref gizmoState.Get<GizmoStateComponent>();
        state.SnapEnabled = true;
        state.GridStep = 16f;
        SetCursor(cursor, new Vector2(100.4f, 77.7f));
        palette.Update(Edit());
        Assert.Equal(new Vector2(96f, 96f), ghost.Get<TransformComponent>().Position);

        // Outside the game viewport (over chrome/margins) the ghost hides off-screen.
        SetCursor(cursor, new Vector2(100.4f, 77.7f), outsideViewport: true);
        palette.Update(Edit());
        Assert.Equal(MonoDreams.LevelEditor.UI.SystemsPanelLayout.ParkedPosition,
            ghost.Get<TransformComponent>().Position);

        // While the transport is Playing the palette is inert and the ghost despawns...
        palette.Update(Play());
        Assert.False(palette.HasGhost);
        // ...but the armed selection is kept: pausing resumes placement where the designer was.
        Assert.NotNull(palette.ArmedEntry);
        SetCursor(cursor, new Vector2(10, 10));
        palette.Update(Edit());
        Assert.True(palette.HasGhost);

        // Disarm despawns for good.
        palette.Disarm();
        Assert.False(palette.HasGhost);
    }

    // ---- Placement ----

    [Fact]
    public void PlacementTest_SingleClickIsOneUndoStepAutoSelectAndRepeat()
    {
        using var world = new World();
        MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);
        using var palette = MakePalette(world, history, serializer);

        palette.ArmByIndex(0);
        palette.SelectBand("Ground");

        // A single click = press then release. The prop is created live on the press (the stroke's
        // open transaction applies immediately)...
        SetCursor(cursor, new Vector2(120, 80), leftPressed: true);
        palette.Update(Edit());
        var placed = Assert.Single(PlacedProps(world));
        Assert.True(placed.Has<SceneObjectComponent>());
        // Centred: Ground is top-left origin (0,0) on the 32×32 fallback, so position = cursor −
        // centre(16,16) = (120,80) − (16,16) = (104,64) (visual centre lands at the cursor).
        Assert.Equal(new Vector2(104, 64), placed.Get<TransformComponent>().Position);
        var sprite = placed.Get<SpriteInfoComponent>();
        Assert.Equal("file:Island/props/tree01.png", sprite.AssetKey);
        Assert.Equal(0.9f, sprite.LayerDepth); // the Ground band's SOURCE depth

        // ...and the release commits it as exactly one undo step + auto-selects it.
        SetCursor(cursor, new Vector2(120, 80), leftReleased: true);
        palette.Update(Edit());
        Assert.Equal(1, history.Count);
        Assert.Equal(placed, Selected(world));

        // A second click keeps placing (still armed), auto-select moves to the newest.
        SetCursor(cursor, new Vector2(200, 40), leftPressed: true);
        palette.Update(Edit());
        SetCursor(cursor, new Vector2(200, 40), leftReleased: true);
        palette.Update(Edit());
        Assert.Equal(2, PlacedProps(world).Count);
        Assert.Equal(2, history.Count);
        Assert.NotEqual(placed, Selected(world));

        // Undo removes exactly the newest placement (its whole one-step command).
        history.Undo();
        var remaining = Assert.Single(PlacedProps(world));
        Assert.Equal(placed, remaining);
        Assert.Equal(1, history.Count);

        history.Undo();
        Assert.Empty(PlacedProps(world));
        Assert.False(placed.IsAlive);
    }

    [Fact]
    public void MultiStampTest_HoldDragStampsAtSpacingAndCoalescesToOneUndoStep()
    {
        using var world = new World();
        var gizmoState = MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);
        using var palette = MakePalette(world, history, serializer);

        // Deterministic spacing, snap off, a non-Y-sorted band. Arc-length spacing is measured on the
        // CURSOR path (unaffected by centring); each stamp's stored position is the CENTRED position
        // (cursor − centre(16,16) for the 32×32 tile), so the stored x's are the cursor x's shifted −16.
        {
            ref var state = ref gizmoState.Get<GizmoStateComponent>();
            state.StampSpacing = 10f;
            state.SnapEnabled = false;
        }
        palette.ArmByIndex(0);
        palette.SelectBand("Ground");

        // Press stamps the first prop and opens the coalescing stroke.
        SetCursor(cursor, new Vector2(0, 0), leftPressed: true);
        palette.Update(Edit());
        Assert.Single(PlacedProps(world));

        // Hold-drag to x=35: arc-length spacing 10 earns stamps at x=10,20,30 (4 total).
        SetCursor(cursor, new Vector2(35, 0), leftDown: true);
        palette.Update(Edit());
        Assert.Equal(4, PlacedProps(world).Count);

        // Continue to x=52: the leftover 5 units carry over, so stamps land at x=40,50 (6 total).
        SetCursor(cursor, new Vector2(52, 0), leftDown: true);
        palette.Update(Edit());
        Assert.Equal(6, PlacedProps(world).Count);

        // Mid-drag NOTHING is committed to the history yet (the transaction is still open).
        Assert.Equal(0, history.Count);

        // Release commits the WHOLE stroke as exactly one undo step, last stamp auto-selected.
        SetCursor(cursor, new Vector2(52, 0), leftReleased: true);
        palette.Update(Edit());
        Assert.Equal(1, history.Count);

        var xs = new List<float>();
        foreach (var e in PlacedProps(world)) xs.Add(e.Get<TransformComponent>().Position.X);
        xs.Sort();
        // Stamps earned at cursor x=0,10,20,30,40,50 (spacing 10 preserved); stored centred → −16 each.
        Assert.Equal(new[] { -16f, -6f, 4f, 14f, 24f, 34f }, xs);
        Assert.Equal(34f, Selected(world)!.Value.Get<TransformComponent>().Position.X);

        // One undo removes the entire coalesced stroke.
        history.Undo();
        Assert.Empty(PlacedProps(world));
        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void PlacementTest_NoPlacementOutsideViewportOrDisarmed()
    {
        using var world = new World();
        MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);
        using var palette = MakePalette(world, history, serializer);

        // Disarmed: a press places nothing.
        SetCursor(cursor, new Vector2(10, 10), leftPressed: true);
        palette.Update(Edit());
        Assert.Empty(PlacedProps(world));

        // Armed but the press lands over the chrome margins: no placement.
        palette.ArmByIndex(0);
        SetCursor(cursor, new Vector2(10, 10), leftPressed: true, outsideViewport: true);
        palette.Update(Edit());
        Assert.Empty(PlacedProps(world));
        Assert.Equal(0, history.Count);
    }

    // ---- Headless channel routing ----

    [Fact]
    public void HeadlessPaletteOpTest_ToolbarActionStringReachesNamedDispatch()
    {
        using var world = new World();
        MakeCursor(world);

        var received = new List<string>();
        var plan = new EditorOpPlan
        {
            Ops = new List<EditorOp>
            {
                new() { Frame = 0, Kind = EditorOpKind.ToolbarAction, Action = "palette:Island/props/tree01.png" },
                new() { Frame = 1, Kind = EditorOpKind.ToolbarAction, Action = "band:Props" },
                new() { Frame = 2, Kind = EditorOpKind.ToolbarAction, Action = "Undo" },
            },
        };
        using var driver = new EditorOpReplaySystem(world, plan,
            dispatch: null, requestExit: null, transport: null,
            dispatchNamed: (name, _) => received.Add(name));

        var state = Edit();
        for (var i = 0; i < 4; i++) driver.Update(state);

        // The raw strings (the palette grammar included) reach the named dispatch — the overlay's
        // DispatchNamedAction then arms/disarms/band-selects or falls back to the enum actions.
        Assert.Equal(new[] { "palette:Island/props/tree01.png", "band:Props", "Undo" }, received);
    }

    // ---- Ghost rotate (Slice 4) ----

    [Fact]
    public void GhostRotateTest_RotatesTheGhostAndBakesItIntoThePlacedEntity()
    {
        using var world = new World();
        MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);
        var cw = false;
        var ccw = false;
        using var palette = new PalettePlacementSystem(world, MakeCatalog(), Bands, MakeLoader(),
            serializer, history, viewportManager: null, font: null, cancelRequested: null,
            triggerTypes: null, rotateCwRequested: _ => cw, rotateCcwRequested: _ => ccw);

        palette.ArmByIndex(0);
        palette.SelectBand("Ground");
        SetCursor(cursor, new Vector2(50, 50));
        palette.Update(Edit());
        Assert.Equal(0f, palette.ArmedRotation);
        Assert.Equal(0f, palette.Ghost.Get<TransformComponent>().Rotation);

        // A CW press rotates by one step; the ghost follows this frame.
        cw = true;
        palette.Update(Edit());
        cw = false;
        Assert.Equal(PalettePlacementSystem.GhostRotationStep, palette.ArmedRotation, 3);
        Assert.Equal(PalettePlacementSystem.GhostRotationStep,
            palette.Ghost.Get<TransformComponent>().Rotation, 3);

        // Placing bakes the armed rotation into the created entity.
        SetCursor(cursor, new Vector2(120, 80), leftPressed: true);
        palette.Update(Edit());
        SetCursor(cursor, new Vector2(120, 80), leftReleased: true);
        palette.Update(Edit());
        var placed = Assert.Single(PlacedProps(world));
        Assert.Equal(PalettePlacementSystem.GhostRotationStep,
            placed.Get<TransformComponent>().Rotation, 3);

        // CCW rotates back to 0; disarm resets the orientation for the next arm.
        ccw = true;
        palette.Update(Edit());
        ccw = false;
        Assert.Equal(0f, palette.ArmedRotation, 3);
        palette.Disarm();
        Assert.Equal(0f, palette.ArmedRotation);
    }

    // ---- Refresh-catalog (Slice 4) ----

    [Fact]
    public void RefreshTest_RebuildsPaletteToIncludeANewlyDroppedAsset()
    {
        var root = global::System.IO.Path.Combine(
            global::System.IO.Path.GetTempPath(),
            "monodreams-palette-refresh-" + global::System.Guid.NewGuid().ToString("N"));
        global::System.IO.Directory.CreateDirectory(global::System.IO.Path.Combine(root, "props"));
        global::System.IO.File.WriteAllText(
            global::System.IO.Path.Combine(root, "props", "tree01.png"), "png");
        try
        {
            using var world = new World();
            MakeGizmoState(world);
            var (serializer, history) = MakeInfra(world);
            var catalog = AssetCatalog.Scan(root, "Island");
            using var palette = new PalettePlacementSystem(world, catalog, Bands, MakeLoader(),
                serializer, history, viewportManager: null, font: null);

            // The new asset is not in the palette yet (arming it fails).
            Assert.False(palette.Arm("Island/props/stone.png"));

            // Drop a new PNG and refresh: the palette rescans + rebuilds, so it is now armable.
            global::System.IO.File.WriteAllText(
                global::System.IO.Path.Combine(root, "props", "stone.png"), "png");
            palette.Refresh();

            Assert.True(palette.Arm("Island/props/stone.png"));
        }
        finally
        {
            try { global::System.IO.Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    // ---- Per-asset band marks (FW3): resolution rule + set + cycle + persistence ----

    [Fact]
    public void BandResolution_MarkedAssetUsesItsBand_UnmarkedUsesGlobalSelector()
    {
        using var world = new World();
        MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);
        using var palette = MakePalette(world, history, serializer);

        // Global selector is Ground; permanently mark tree01 (index 0) as Props.
        Assert.True(palette.SelectBand("Ground"));
        Assert.True(palette.SetAssetBand("Island/props/tree01.png", "Props"));
        Assert.Equal("Props", palette.MarkedBandName(new AssetCatalogEntry(
            "Island/props/tree01.png", null, null, "tree01", "props")));

        // Arming/placing the MARKED asset lands on its band (Props: depth 0.45, feet-origin),
        // NOT the global Ground selector.
        palette.ArmByIndex(0);
        SetCursor(cursor, new Vector2(10, 20), leftPressed: true);
        palette.Update(Edit());
        SetCursor(cursor, new Vector2(10, 20), leftReleased: true);
        palette.Update(Edit());
        var marked = Assert.Single(PlacedProps(world));
        var markedSprite = marked.Get<SpriteInfoComponent>();
        Assert.Equal(0.45f, markedSprite.LayerDepth);                 // Props band's SOURCE depth
        Assert.Equal(new Vector2(16f, 32f), markedSprite.Origin);     // feet-origin (Y-sorted Props)

        // Arming/placing an UNMARKED asset (sheet#trunk, index 1) uses the global Ground selector.
        palette.ArmByIndex(1);
        SetCursor(cursor, new Vector2(30, 40), leftPressed: true);
        palette.Update(Edit());
        SetCursor(cursor, new Vector2(30, 40), leftReleased: true);
        palette.Update(Edit());
        var unmarked = PlacedProps(world).Find(e => e != marked);
        var unmarkedSprite = unmarked.Get<SpriteInfoComponent>();
        Assert.Equal(0.9f, unmarkedSprite.LayerDepth);                // Ground band's SOURCE depth
        Assert.Equal(Vector2.Zero, unmarkedSprite.Origin);            // top-left (Ground not Y-sorted)
    }

    [Fact]
    public void SetAssetBand_SetsClearsAndIsLoudOnUnknown()
    {
        using var world = new World();
        MakeGizmoState(world);
        var (serializer, history) = MakeInfra(world);
        using var palette = MakePalette(world, history, serializer);
        var tree = new AssetCatalogEntry("Island/props/tree01.png", null, null, "tree01", "props");

        Assert.True(palette.SetAssetBand("Island/props/tree01.png", "Ground"));
        Assert.Equal("Ground", palette.MarkedBandName(tree));

        // "auto" clears the mark back to the global selector.
        Assert.True(palette.SetAssetBand("Island/props/tree01.png", "auto"));
        Assert.Null(palette.MarkedBandName(tree));

        // Loud false on an unknown band or an unknown entry (no mark applied).
        Assert.False(palette.SetAssetBand("Island/props/tree01.png", "Nope"));
        Assert.False(palette.SetAssetBand("Island/props/ghost.png", "Ground"));
        Assert.Null(palette.MarkedBandName(tree));
    }

    [Fact]
    public void CycleAssetBand_WalksUnmarkedThroughEveryBandBackToUnmarked()
    {
        using var world = new World();
        MakeGizmoState(world);
        var (serializer, history) = MakeInfra(world);
        using var palette = MakePalette(world, history, serializer);
        var tree = new AssetCatalogEntry("Island/props/tree01.png", null, null, "tree01", "props");

        Assert.Null(palette.MarkedBandName(tree));  // starts unmarked (global selector)
        palette.CycleAssetBand(0);
        Assert.Equal("Ground", palette.MarkedBandName(tree)); // → band 0
        palette.CycleAssetBand(0);
        Assert.Equal("Props", palette.MarkedBandName(tree));  // → band 1
        palette.CycleAssetBand(0);
        Assert.Null(palette.MarkedBandName(tree));             // → back to unmarked
    }

    [Fact]
    public void MarkedBand_SurvivesCatalogRescanAndEditorRestart()
    {
        var root = global::System.IO.Path.Combine(
            global::System.IO.Path.GetTempPath(),
            "monodreams-palette-bandmark-" + global::System.Guid.NewGuid().ToString("N"));
        global::System.IO.Directory.CreateDirectory(global::System.IO.Path.Combine(root, "props"));
        global::System.IO.File.WriteAllText(
            global::System.IO.Path.Combine(root, "props", "tree01.png"), "png");
        try
        {
            using var world = new World();
            MakeGizmoState(world);
            var (serializer, history) = MakeInfra(world);

            // First session: mark tree01 as Props (persists to asset-bands.json).
            var catalog1 = AssetCatalog.Scan(root, "Island");
            var config1 = AssetBandConfig.Load(catalog1.RootAbsolutePath);
            using (var palette1 = new PalettePlacementSystem(world, catalog1, Bands, MakeLoader(),
                       serializer, history, viewportManager: null, font: null, cancelRequested: null,
                       triggerTypes: null, rotateCwRequested: null, rotateCcwRequested: null,
                       bandConfig: config1))
            {
                Assert.True(palette1.SetAssetBand("Island/props/tree01.png", "Props"));
            }

            // Restart: a FRESH scan + a FRESH config load off the same folder. The mark resolves.
            var catalog2 = AssetCatalog.Scan(root, "Island");
            var config2 = AssetBandConfig.Load(catalog2.RootAbsolutePath);
            using var palette2 = new PalettePlacementSystem(world, catalog2, Bands, MakeLoader(),
                serializer, history, viewportManager: null, font: null, cancelRequested: null,
                triggerTypes: null, rotateCwRequested: null, rotateCcwRequested: null,
                bandConfig: config2);

            var entry = catalog2.Entries[0];
            Assert.Equal("tree01", entry.Label);
            Assert.Equal("Props", palette2.MarkedBandName(entry));
            Assert.Equal("Props", palette2.ResolveBand(entry).Name); // regardless of the default global band
        }
        finally
        {
            try { global::System.IO.Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void HeadlessPaletteOpTest_AssetBandStringReachesNamedDispatch()
    {
        using var world = new World();
        MakeCursor(world);

        var received = new global::System.Collections.Generic.List<string>();
        var plan = new EditorOpPlan
        {
            Ops = new global::System.Collections.Generic.List<EditorOp>
            {
                new() { Frame = 0, Kind = EditorOpKind.ToolbarAction, Action = "asset-band:Island/props/tree01.png:Ground" },
            },
        };
        using var driver = new EditorOpReplaySystem(world, plan,
            dispatch: null, requestExit: null, transport: null,
            dispatchNamed: (name, _) => received.Add(name));

        var state = Edit();
        for (var i = 0; i < 2; i++) driver.Update(state);

        // The asset-band op's raw string reaches the named dispatch (the overlay then parses
        // <entryId>:<band> and calls Palette.SetAssetBand).
        Assert.Equal(new[] { "asset-band:Island/props/tree01.png:Ground" }, received);
    }

    [Fact]
    public void HeadlessPaletteOpTest_ArmByIdThenClickPlaces()
    {
        using var world = new World();
        MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);
        using var palette = MakePalette(world, history, serializer);

        // The overlay's named-dispatch grammar, driven directly (the overlay itself needs a
        // GraphicsDevice; Arm/SelectBand are its exact call targets).
        Assert.True(palette.Arm("Island/props/sheet.png#trunk")); // by bare id
        Assert.True(palette.SelectBand("props")); // case-insensitive
        Assert.False(palette.Arm("Island/props/nope.png")); // unknown id = loud false

        SetCursor(cursor, new Vector2(64, 32), leftPressed: true);
        palette.Update(Edit());

        var placed = Assert.Single(PlacedProps(world));
        Assert.Equal("file:Island/props/sheet.png#trunk", placed.Get<SpriteInfoComponent>().AssetKey);
        Assert.Equal(new Rectangle(0, 0, 32, 48), placed.Get<SpriteInfoComponent>().Source);
    }
}
