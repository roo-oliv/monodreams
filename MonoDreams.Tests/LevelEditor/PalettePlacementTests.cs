#nullable enable
using System.Collections.Generic;
using System.IO;
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
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Platform;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoDreams.UI;
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
///   standard prop stack, parented to the ACTIVE scene layer (the layers wave: the LAYER is the
///   <c>SceneObjectComponent</c> save-root), auto-selected; undo removes it; repeated presses keep
///   placing.</item>
///   <item><b>Duplicate stamps:</b> a wobbling/back-tracking hold-drag stamps exactly one copy per
///   cell (the per-stroke visited set), and re-clicking a cell that already holds the identical prop
///   is a loud-logged no-op that costs no undo step (the cross-stroke guard).</item>
///   <item><b>Bottom-shelf tabs:</b> the active tab is part of the palette's layout cache key, so a
///   PROGRAMMATIC switch (the <c>panel:tab</c> op writing the shared shell state) re-lays the shelf —
///   the Assets chrome is never left painted over the Prefabs tab.</item>
///   <item><b>Headless channel:</b> a scripted <c>ToolbarAction</c> op's raw string (the
///   <c>palette:&lt;id&gt;</c> grammar) reaches the named dispatch.</item>
///   <item><b>Refresh re-skins (drop-a-PNG wave):</b> a refresh invalidates the loader AND walks the
///   already-placed <c>file:</c>-keyed sprites, re-resolving each one, so re-exporting art updates
///   the placed world in place.</item>
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
        SceneSerializer serializer, global::System.Func<GameState, bool>? cancel = null,
        EditorShellStateComponent? shellState = null) =>
        new(world, MakeCatalog(), Bands, MakeLoader(), serializer, history,
            viewportManager: null, font: null, cancelRequested: cancel, shellState: shellState);

    /// <summary>A scene LAYER entity (layers wave): an ordinary entity carrying a
    /// <c>SceneLayerComponent</c>, tagged <c>SceneObjectComponent</c> as its own save-root.</summary>
    private static Entity MakeLayer(World world, string name, int order, bool locked = false)
    {
        var layer = world.CreateEntity();
        layer.Set(new TransformComponent(Vector2.Zero));
        layer.Set(new EntityInfoComponent("Layer", name));
        layer.Set(new SceneObjectComponent());
        layer.Set(new MonoDreams.Component.Level.SceneLayerComponent { Order = order, Locked = locked });
        return layer;
    }

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

    /// <summary>The placed PROPS — sprite-prop entities, excluding the ghost (editor
    /// infrastructure) and the scene LAYER the stamps parent to (layers wave: the layer is the
    /// save-root, its member props are its <c>ChildOf</c> children, so the props themselves carry no
    /// <c>SceneObjectComponent</c> — the writer auto-closes the descendant set).</summary>
    private static List<Entity> PlacedProps(World world)
    {
        var list = new List<Entity>();
        using var set = world.GetEntities()
            .With<SpriteInfoComponent>().Without<EditorInfrastructureComponent>().AsSet();
        foreach (var e in set.GetEntities()) list.Add(e);
        return list;
    }

    /// <summary>The scene layer entities (layers wave) — placement bootstraps one when a scene has
    /// none, and every stamp parents to the ACTIVE layer.</summary>
    private static List<Entity> SceneLayers(World world)
    {
        var list = new List<Entity>();
        using var set = world.GetEntities().With<MonoDreams.Component.Level.SceneLayerComponent>().AsSet();
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

    // ---- Placement targeting: the ACTIVE scene layer (layers wave) ----

    [Fact]
    public void PlacementTargetsTheActiveLayer_ParentingTheStampToIt()
    {
        using var world = new World();
        MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);
        var back = MakeLayer(world, "Background", 0);
        MakeLayer(world, "Props", 1);
        var shell = new EditorShellStateComponent { ActiveLayer = back };
        using var palette = MakePalette(world, history, serializer, shellState: shell);

        palette.ArmByIndex(0);
        SetCursor(cursor, new Vector2(120, 80), leftPressed: true);
        palette.Update(Edit());
        SetCursor(cursor, new Vector2(120, 80), leftReleased: true);
        palette.Update(Edit());

        // The stamp is a ChildOf member of the ACTIVE layer (not the front-most one), so the layer
        // remap owns its final draw depth and the writer saves it inside the layer's descendant set.
        var placed = Assert.Single(PlacedProps(world));
        Assert.Equal(back, placed.Get<ChildOfComponent>().Parent);
        Assert.False(placed.Has<SceneObjectComponent>()); // the LAYER is the save-root, not the prop
        Assert.Equal(2, SceneLayers(world).Count);        // no extra layer was invented
    }

    [Fact]
    public void PlacementWithNoLayers_BootstrapsLayer1_AndActivatesIt()
    {
        using var world = new World();
        MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);
        var shell = new EditorShellStateComponent();
        using var palette = MakePalette(world, history, serializer, shellState: shell);
        Assert.Empty(SceneLayers(world));

        palette.ArmByIndex(0);
        SetCursor(cursor, new Vector2(64, 32), leftPressed: true);
        palette.Update(Edit());
        SetCursor(cursor, new Vector2(64, 32), leftReleased: true);
        palette.Update(Edit());

        // The first placement never dead-ends: it bootstraps a save-root "Layer 1" and activates it.
        var layer = Assert.Single(SceneLayers(world));
        Assert.Equal("Layer 1", layer.Get<EntityInfoComponent>().Name);
        Assert.True(layer.Has<SceneObjectComponent>());
        Assert.Equal(0, layer.Get<MonoDreams.Component.Level.SceneLayerComponent>().Order);
        Assert.Equal(layer, shell.ActiveLayer);
        Assert.Equal(layer, Assert.Single(PlacedProps(world)).Get<ChildOfComponent>().Parent);

        // A second placement reuses that layer rather than creating another.
        SetCursor(cursor, new Vector2(200, 32), leftPressed: true);
        palette.Update(Edit());
        SetCursor(cursor, new Vector2(200, 32), leftReleased: true);
        palette.Update(Edit());
        Assert.Single(SceneLayers(world));
        Assert.Equal(2, PlacedProps(world).Count);
    }

    [Fact]
    public void ABandlessPalette_IsLegal_AndPlacesOnTheSyntheticWithinLayerBand()
    {
        // Layers wave: screen-supplied bands became OPTIONAL legacy (the placement's DRAW layer is
        // the ACTIVE scene layer), so an empty band list must construct — it used to throw — and
        // resolve to the synthetic within-layer band (depth 0.5, never Y-sorted).
        using var world = new World();
        MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);
        var layer = MakeLayer(world, "Props", 0);
        using var palette = new PalettePlacementSystem(world, MakeCatalog(),
            global::System.Array.Empty<PaletteBand>(), MakeLoader(), serializer, history,
            viewportManager: null, font: null,
            shellState: new EditorShellStateComponent { ActiveLayer = layer });

        var band = palette.ResolveBand(MakeCatalog().Entries[0]);
        Assert.Equal(0.5f, band.LayerDepth);
        Assert.False(band.YSorted);
        Assert.Equal(band, palette.SelectedBand);

        palette.ArmByIndex(0);
        SetCursor(cursor, new Vector2(120, 80), leftPressed: true);
        palette.Update(Edit());
        SetCursor(cursor, new Vector2(120, 80), leftReleased: true);
        palette.Update(Edit());

        var placed = Assert.Single(PlacedProps(world));
        Assert.Equal(layer, placed.Get<ChildOfComponent>().Parent);
        // The SOURCE depth is the within-layer key; SceneLayerSystem slices it into the layer's band.
        Assert.Equal(0.5f, placed.Get<SpriteInfoComponent>().LayerDepth);
    }

    [Fact]
    public void PlacementIntoALockedActiveLayer_IsRefused_NothingIsCreated()
    {
        using var world = new World();
        MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);
        var locked = MakeLayer(world, "Terrain", 0, locked: true);
        var shell = new EditorShellStateComponent { ActiveLayer = locked };
        using var palette = MakePalette(world, history, serializer, shellState: shell);

        palette.ArmByIndex(0);
        SetCursor(cursor, new Vector2(120, 80), leftPressed: true);
        palette.Update(Edit());
        SetCursor(cursor, new Vector2(120, 80), leftReleased: true);
        palette.Update(Edit());

        // Refused: no prop, no bootstrap layer, and nothing selected. (The LOUD half — the Logger
        // warning that makes the no-op explainable — is asserted in
        // <see cref="PalettePlacementLockedLayerLogTests"/>.)
        Assert.Empty(PlacedProps(world));
        Assert.Equal(locked, Assert.Single(SceneLayers(world)));
        Assert.Null(Selected(world));

        // Unlocking the same layer makes the very next click land — the refusal is state, not a latch.
        locked.Get<MonoDreams.Component.Level.SceneLayerComponent>().Locked = false;
        SetCursor(cursor, new Vector2(120, 80), leftPressed: true);
        palette.Update(Edit());
        SetCursor(cursor, new Vector2(120, 80), leftReleased: true);
        palette.Update(Edit());
        Assert.Equal(locked, Assert.Single(PlacedProps(world)).Get<ChildOfComponent>().Parent);
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
        // Layers wave: the stamp is a CHILD of the ACTIVE layer (bootstrapped here), so the LAYER is
        // the save-root and the prop rides along in its ChildOf descendant set.
        var layer = Assert.Single(SceneLayers(world));
        Assert.True(layer.Has<SceneObjectComponent>());
        Assert.Equal(layer, placed.Get<ChildOfComponent>().Parent);
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
    public void MultiStampTest_WobblingDragStampsOneCopyPerCell_NeverDoublesARevisitedCell()
    {
        using var world = new World();
        var gizmoState = MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);
        using var palette = MakePalette(world, history, serializer);

        // Snap ON at the same quantum as the spacing, so every stamp of the drag lands exactly on a
        // grid cell — the tile-painting case where a wobbling hand re-enters a cell it already filled.
        {
            ref var state = ref gizmoState.Get<GizmoStateComponent>();
            state.SnapEnabled = true;
            state.GridStep = 32f;
            state.StampSpacing = 32f;
        }
        palette.ArmByIndex(0);
        palette.SelectBand("Ground"); // top-left origin: position = snap(cursor − centre(16,16))

        // Press in cell A (cursor (16,16) → position (0,0)).
        SetCursor(cursor, new Vector2(16, 16), leftPressed: true);
        palette.Update(Edit());
        Assert.Single(PlacedProps(world));

        // Drag one cell right into B (cursor (48,16) → position (32,0)).
        SetCursor(cursor, new Vector2(48, 16), leftDown: true);
        palette.Update(Edit());
        Assert.Equal(2, PlacedProps(world).Count);

        // Wobble BACK into A, then forward into B again — both cells were already filled by THIS
        // stroke, so the per-stroke visited set drops both stamps (the previous-position guard alone
        // catches neither: the last stamp was the OTHER cell each time).
        SetCursor(cursor, new Vector2(16, 16), leftDown: true);
        palette.Update(Edit());
        SetCursor(cursor, new Vector2(48, 16), leftDown: true);
        palette.Update(Edit());
        Assert.Equal(2, PlacedProps(world).Count);

        // Exactly one copy per cell, and the whole wobble is still ONE undo step.
        SetCursor(cursor, new Vector2(48, 16), leftReleased: true);
        palette.Update(Edit());
        var xs = new List<float>();
        foreach (var e in PlacedProps(world)) xs.Add(e.Get<TransformComponent>().Position.X);
        xs.Sort();
        Assert.Equal(new[] { 0f, 32f }, xs);
        Assert.Equal(1, history.Count);

        history.Undo();
        Assert.Empty(PlacedProps(world));
    }

    [Fact]
    public void PlacementTest_ReClickingAFilledCellIsALoudNoOp_AcrossStrokes()
    {
        using var world = new World();
        MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);
        using var palette = MakePalette(world, history, serializer);

        palette.ArmByIndex(0);
        palette.SelectBand("Ground");

        // Stroke 1: one click places the prop at (104,64) (cursor − centre(16,16)).
        SetCursor(cursor, new Vector2(120, 80), leftPressed: true);
        palette.Update(Edit());
        SetCursor(cursor, new Vector2(120, 80), leftReleased: true);
        palette.Update(Edit());
        var placed = Assert.Single(PlacedProps(world));
        Assert.Equal(new Vector2(104, 64), placed.Get<TransformComponent>().Position);
        Assert.Equal(1, history.Count);

        // Stroke 2 at EXACTLY the same spot with the same entry + band: the cross-stroke duplicate
        // guard skips it (identical props must never stack invisibly), and because nothing was pushed
        // the stroke's transaction commits no history entry either — the no-op costs no undo step.
        SetCursor(cursor, new Vector2(120, 80), leftPressed: true);
        palette.Update(Edit());
        SetCursor(cursor, new Vector2(120, 80), leftReleased: true);
        palette.Update(Edit());
        Assert.Equal(placed, Assert.Single(PlacedProps(world)));
        Assert.Equal(1, history.Count);

        // The guard is narrow — the same prop one cell over still places (it is the CELL that is
        // occupied, not the asset that is spent).
        SetCursor(cursor, new Vector2(152, 80), leftPressed: true);
        palette.Update(Edit());
        SetCursor(cursor, new Vector2(152, 80), leftReleased: true);
        palette.Update(Edit());
        Assert.Equal(2, PlacedProps(world).Count);
        Assert.Equal(2, history.Count);
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

    /// <summary>
    /// Refresh is not only a shelf rebuild: it re-resolves every ALREADY-PLACED <c>file:</c>-keyed
    /// sprite through the just-invalidated loader, so re-exporting a PNG updates the placed world in
    /// place (the Blender-material behaviour). Headless every texture stays null, so what this
    /// observes is the RE-RESOLUTION — the placed prop's path is opened again after the refresh — not
    /// the texture swap itself.
    /// </summary>
    [Fact]
    public void RefreshTest_ReSkinsEveryPlacedFileKeyedSprite()
    {
        var opened = new List<string>();
        using var world = new World();
        MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);
        var textures = new FileAssetTextureLoader(
            openStream: path => { opened.Add(path); return null; },
            decode: _ => null,
            createPlaceholder: () => null);
        // Chrome-built (the headless ViewportManager + a null font, as in MakeChromePalette) so the
        // refresh runs its full body — the toolbar's Refresh button drives exactly this.
        using var palette = new PalettePlacementSystem(world, MakeCatalog(), Bands, textures,
            serializer, history,
            viewportManager: new ViewportManager(null, 800, 600)
                { ScreenWidth = 1600, ScreenHeight = 900, DevicePixelRatio = 1f },
            font: null);
        palette.Update(Edit()); // builds + lays out the chrome (thumbnails load once, memoized)

        // Place one prop the ordinary way: its SpriteInfoComponent.AssetKey is a file: key.
        palette.ArmByIndex(0);
        palette.SelectBand("Ground");
        SetCursor(cursor, new Vector2(120, 80), leftPressed: true);
        palette.Update(Edit());
        SetCursor(cursor, new Vector2(120, 80), leftReleased: true);
        palette.Update(Edit());

        var placed = Assert.Single(PlacedProps(world));
        Assert.Equal("file:Island/props/tree01.png", placed.Get<SpriteInfoComponent>().AssetKey);

        // Everything the palette needed is memoized by now — nothing re-opens on its own.
        var beforeRefresh = opened.Count;
        palette.Refresh();

        // The refresh invalidated the loader AND walked the placed props, so the placed sprite's PNG
        // was opened again: a changed file on disk decodes fresh into the live world.
        var afterRefresh = opened.GetRange(beforeRefresh, opened.Count - beforeRefresh);
        Assert.Contains("Island/props/tree01.png", afterRefresh);
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

    // ---- Prefabs shelf (PF-D) ----

    [Fact]
    public void PrefabShelf_ArmPrefab_ThenViewportClick_PlacesTheArmedPrefab_AtCursor()
    {
        using var world = new World();
        MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);

        string? placedId = null;
        var placedAt = new Vector2(-1, -1);
        var palette = new PalettePlacementSystem(world, MakeCatalog(), Bands, MakeLoader(), serializer, history,
            viewportManager: null, font: null,
            prefabLister: () => new[] { "npc" },
            placePrefab: (id, pos) => { placedId = id; placedAt = pos; });

        // Arm a prefab (mutually exclusive with an asset/trigger) — Place mode.
        palette.ArmPrefab("npc");
        Assert.Equal("npc", palette.ArmedPrefab);
        Assert.False(palette.HasGhost);

        // A viewport left-click stamps the armed prefab at the cursor (through the injected placePrefab).
        SetCursor(cursor, new Vector2(37, 42), leftPressed: true);
        palette.Update(Edit());

        Assert.Equal("npc", placedId);
        Assert.Equal(new Vector2(37, 42), placedAt); // snap off by default → the raw cursor world position
        // PF-G: with no prefab resolver wired, the prefab has no resolvable sprite → the crosshair aim
        // indicator shows (not the sprite ghost).
        Assert.False(palette.HasGhost);
        Assert.True(palette.HasCrosshair);

        // Escape / disarm clears the armed prefab.
        palette.Disarm();
        Assert.Null(palette.ArmedPrefab);
        palette.Dispose();
    }

    // ---- Prefab placement ghost (PF-G item 4) ----

    // A house-like prefab: root (collider only) → child sprite at a local offset + sub-1 scale.
    private const string GhostHouseJson = """
    {
      "version": 1,
      "entities": [
        {
          "id": 0,
          "components": {
            "core.BoxCollider": { "bounds": [-15, 5, 27, 20], "activeLayers": [-1], "passive": true, "enabled": true },
            "core.Transform": { "position": [0, 0], "rotation": 0, "scale": [1, 1], "origin": [0, 0] }
          }
        },
        {
          "components": {
            "core.SpriteInfo": {
              "assetKey": "file:Island/House2.png", "source": [0, 0, 128, 192], "size": [128, 192],
              "color": "/////w==", "origin": [3, 5], "offset": [0, 0], "target": 0, "layerDepth": 0.1
            },
            "core.Transform": { "position": [-7, -40], "rotation": 0, "scale": [0.5, 0.5], "origin": [0, 0] }
          },
          "parent": 0
        }
      ]
    }
    """;

    private static PrefabData? ResolveGhostHouse(string id) =>
        id == "house" ? PrefabData.FromScene(id, CanonicalJson.Deserialize<SceneData>(GhostHouseJson)!) : null;

    [Fact]
    public void PrefabGhost_ArmedPrefabWithSprite_ShowsSpriteGhost_FollowingCursor_AtPlacementSpot()
    {
        using var world = new World();
        MakeGizmoState(world); // snap OFF by default → ghost position is exactly cursor + offset
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);

        string? placedId = null;
        var placedAt = new Vector2(-1, -1);
        var palette = new PalettePlacementSystem(world, MakeCatalog(), Bands, MakeLoader(), serializer, history,
            viewportManager: null, font: null,
            prefabResolver: ResolveGhostHouse,
            placePrefab: (id, pos) => { placedId = id; placedAt = pos; });

        palette.ArmPrefab("house");
        SetCursor(cursor, new Vector2(200, 120)); // hover, no click yet
        palette.Update(Edit());

        // The ghost is the prefab's dominant (child) sprite — resolved root-first — following the cursor.
        Assert.True(palette.HasGhost);
        Assert.False(palette.HasCrosshair);
        var ghost = palette.Ghost;
        ref readonly var sprite = ref ghost.Get<SpriteInfoComponent>();
        Assert.Equal(new Rectangle(0, 0, 128, 192), sprite.Source);
        Assert.Equal(new Vector2(3, 5), sprite.Origin);
        Assert.Equal(new Vector2(0.5f, 0.5f), ghost.Get<TransformComponent>().Scale);
        // Ghost sits where the placed instance's sprite will land: root at the (snapped) cursor + the
        // sprite's prefab-space offset (-7,-40).
        Assert.Equal(new Vector2(193, 80), ghost.Get<TransformComponent>().Position);

        // A left click places the instance ROOT at the snapped cursor — i.e. where the ghost was aimed.
        SetCursor(cursor, new Vector2(200, 120), leftPressed: true);
        palette.Update(Edit());
        Assert.Equal("house", placedId);
        Assert.Equal(new Vector2(200, 120), placedAt);
        // The placed root (200,120) + the sprite offset (-7,-40) == where the ghost showed the sprite.
        Assert.Equal(placedAt + new Vector2(-7, -40), ghost.Get<TransformComponent>().Position);

        palette.Dispose();
    }

    [Fact]
    public void PrefabGhost_OutsideViewport_ParksTheGhost()
    {
        using var world = new World();
        MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);
        var palette = new PalettePlacementSystem(world, MakeCatalog(), Bands, MakeLoader(), serializer, history,
            viewportManager: null, font: null, prefabResolver: ResolveGhostHouse);

        palette.ArmPrefab("house");
        SetCursor(cursor, new Vector2(500, 500), outsideViewport: true);
        palette.Update(Edit());

        Assert.True(palette.HasGhost); // exists but parked off-screen (culling drops its VisibleComponent)
        Assert.Equal(SystemsPanelLayout.ParkedPosition, palette.Ghost.Get<TransformComponent>().Position);
        palette.Dispose();
    }

    [Fact]
    public void PrefabGhost_SpritelessPrefab_ShowsCrosshair_NotSpriteGhost()
    {
        using var world = new World();
        MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);

        const string zoneJson = """
        {
          "version": 1,
          "entities": [
            {
              "id": 0,
              "components": {
                "core.BoxCollider": { "bounds": [0, 0, 32, 32], "activeLayers": [-1], "passive": true, "enabled": true },
                "core.Transform": { "position": [0, 0], "rotation": 0, "scale": [1, 1], "origin": [0, 0] }
              }
            }
          ]
        }
        """;
        var palette = new PalettePlacementSystem(world, MakeCatalog(), Bands, MakeLoader(), serializer, history,
            viewportManager: null, font: null,
            prefabResolver: id => id == "zone" ? PrefabData.FromScene(id, CanonicalJson.Deserialize<SceneData>(zoneJson)!) : null);

        palette.ArmPrefab("zone");
        SetCursor(cursor, new Vector2(64, 48));
        palette.Update(Edit());

        Assert.False(palette.HasGhost);    // no sprite to ghost
        Assert.True(palette.HasCrosshair); // the aim crosshair stands in

        // Disarm tears the crosshair down too.
        palette.Disarm();
        Assert.False(palette.HasCrosshair);
        palette.Dispose();
    }

    // ---- Bottom-shelf tab chrome (the active tab is part of the layout cache key) ----

    /// <summary>A palette WITH chrome (a headless <see cref="ViewportManager"/> — it never dereferences
    /// its Game — and a null font, so labels lay out but no text prep runs), sharing the shell state the
    /// <c>panel:tab</c> op writes.</summary>
    private static PalettePlacementSystem MakeChromePalette(World world, EditorHistory history,
        SceneSerializer serializer, EditorShellStateComponent shell) =>
        new(world, MakeCatalog(), Bands, MakeLoader(), serializer, history,
            viewportManager: new ViewportManager(null, 800, 600)
                { ScreenWidth = 1600, ScreenHeight = 900, DevicePixelRatio = 1f },
            font: null, shellState: shell);

    /// <summary>The palette's on-screen chrome widgets: every <see cref="SimpleButtonComponent"/> that is
    /// not parked off-screen (the chrome hides by parking, never by a flag).</summary>
    private static int OnScreenButtons(World world)
    {
        using var set = world.GetEntities().With<SimpleButtonComponent>().AsSet();
        var n = 0;
        foreach (var e in set.GetEntities())
            if (e.Get<TransformComponent>().Position != SystemsPanelLayout.ParkedPosition) n++;
        return n;
    }

    /// <summary>Whether the chrome label carrying exactly <paramref name="text"/> is on-screen.</summary>
    private static bool LabelOnScreen(World world, string text)
    {
        using var set = world.GetEntities().With<DynamicTextComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.Get<DynamicTextComponent>().TextContent == text)
                return e.Get<TransformComponent>().Position != SystemsPanelLayout.ParkedPosition;
        return false;
    }

    [Fact]
    public void BottomTab_ProgrammaticSwitch_RelaysTheShelfChrome_NotJustTheClickHandler()
    {
        using var world = new World();
        MakeGizmoState(world);
        MakeCursor(world);
        var (serializer, history) = MakeInfra(world);
        var shell = new EditorShellStateComponent();
        using var palette = MakeChromePalette(world, history, serializer, shell);

        // First frame builds + lays out the chrome for the default Assets tab.
        palette.Update(Edit());
        Assert.Equal(EditorBottomTab.Assets, shell.ActiveBottomTab);
        var assetsButtons = OnScreenButtons(world);
        Assert.True(assetsButtons > 2, "the Assets tab shows its band row + cards beside the tab strip");
        Assert.True(LabelOnScreen(world, "Ground"));      // the Assets-tab band selector
        Assert.False(LabelOnScreen(world, PrefabEmptyHint)); // the Prefabs body is parked

        // The programmatic switch the `panel:tab prefabs` op performs: it writes the SHARED shell state
        // and nothing else — no resize, no scroll change, no click through the palette's hit-test. The
        // active tab is part of the layout cache key, so the next frame re-lays the shelf.
        shell.ActiveBottomTab = EditorBottomTab.Prefabs;
        palette.Update(Edit());

        // Every Assets widget is parked and the Prefabs body is laid out — no stale Assets chrome
        // painted over the Prefabs tab.
        Assert.Equal(2, OnScreenButtons(world)); // only the Assets | Prefabs tab strip itself
        Assert.False(LabelOnScreen(world, "Ground"));
        Assert.True(LabelOnScreen(world, PrefabEmptyHint));

        // And back: the switch re-lays in both directions.
        shell.ActiveBottomTab = EditorBottomTab.Assets;
        palette.Update(Edit());
        Assert.Equal(assetsButtons, OnScreenButtons(world));
        Assert.True(LabelOnScreen(world, "Ground"));
        Assert.False(LabelOnScreen(world, PrefabEmptyHint));
    }

    /// <summary>The Prefabs tab's empty-shelf message (the observable that its body was laid out).</summary>
    private const string PrefabEmptyHint =
        "No prefabs - Create Empty Prefab (right-click) or from a selection";
}

/// <summary>
/// The LOUD half of the locked-layer placement refusal (layers wave): refusing must say so through
/// <see cref="Logger"/>, because a silent no-op reads as "the editor is broken" — the designer has no
/// way to tell a locked layer from a dead click. <see cref="Logger"/> and
/// <see cref="PlatformServices.Current"/> are process-global, so this lives in the existing
/// non-parallel collection and observes the sinks through a fake platform rather than the disk.
/// </summary>
[Collection("PlatformServices (non-parallel: mutates static state)")]
public class PalettePlacementLockedLayerLogTests
{
    /// <summary>Minimal in-memory <see cref="IPlatformServices"/> — enough to capture the two Logger
    /// sinks. Mirrors the fakes in <c>TexturedMeshTests</c> / <c>LoggerInterpolationTests</c>.</summary>
    private sealed class FakePlatformServices : IPlatformServices
    {
        public string BaseDirectory => "/fake/base/";
        public List<string> ConsoleLines { get; } = new();
        public StringWriter LogWriter { get; } = new();

        public string GetEnvironmentVariable(string name) => null!;
        public string CombinePath(params string[] paths) => string.Join("/", paths);
        public bool FileExists(string path) => false;
        public string ReadAllText(string path) => throw new FileNotFoundException(path);
        public void WriteAllText(string path, string contents) { }
        public void WriteAllBytes(string path, byte[] bytes) { }
        public string ExportScene(string suggestedFileName, string contents) => suggestedFileName;
        public void CreateDirectory(string path) { }
        public TextWriter OpenLogWriter(string directory, string fileName) => LogWriter;
        public void WriteLineToConsole(string line) => ConsoleLines.Add(line);
        public void RunBackground(global::System.Action work) => work();
    }

    private static List<string> RunCapturingLog(global::System.Action body)
    {
        var fake = new FakePlatformServices();
        var previous = PlatformServices.Current;
        try
        {
            PlatformServices.Current = fake;
            Logger.Shutdown();           // close whatever an earlier test left open
            Logger.Initialize("logdir");  // ...and reopen on the fake sink at Debug
            body();
            Logger.Shutdown();           // flush
        }
        finally
        {
            Logger.Shutdown();
            PlatformServices.Current = previous;
        }
        return fake.ConsoleLines;
    }

    [Fact]
    public void LockedActiveLayer_RefusesThePlacement_Loudly()
    {
        var lines = RunCapturingLog(() =>
        {
            using var world = new World();
            var gizmo = world.CreateEntity();
            gizmo.Set(new EditorInfrastructureComponent());
            gizmo.Set(GizmoStateComponent.Default);
            var cursor = world.CreateEntity();
            cursor.Set(new CursorControllerComponent(CursorType.Default));
            cursor.Set(new CursorInputComponent());

            var registry = new ComponentSerializerRegistry();
            registry.RegisterEngineComponents();
            var serializer = new SceneSerializer(registry);
            var history = new EditorHistory(world);

            var locked = world.CreateEntity();
            locked.Set(new TransformComponent(Vector2.Zero));
            locked.Set(new EntityInfoComponent("Layer", "Terrain"));
            locked.Set(new SceneObjectComponent());
            locked.Set(new MonoDreams.Component.Level.SceneLayerComponent { Order = 0, Locked = true });

            var catalog = new AssetCatalog(new[]
            {
                new AssetCatalogEntry("Island/props/tree01.png", null, null, "tree01", "props"),
            });
            var textures = new FileAssetTextureLoader(
                openStream: _ => null, decode: _ => null, createPlaceholder: () => null);
            using var palette = new PalettePlacementSystem(world, catalog,
                new[] { new PaletteBand("Ground", 0.9f, YSorted: false) },
                textures, serializer, history, viewportManager: null, font: null,
                shellState: new EditorShellStateComponent { ActiveLayer = locked });

            palette.ArmByIndex(0);
            ref var input = ref cursor.Get<CursorInputComponent>();
            input.WorldPosition = input.VirtualPosition = new Vector2(120, 80);
            input.LeftButtonPressed = input.LeftButton = true;
            palette.Update(new GameState(new GameTime()) { RunMode = RunMode.Edit });
        });

        Assert.Contains(lines, l => l.Contains("WARN") && l.Contains("Placement refused")
                                                       && l.Contains("LOCKED"));
    }
}
