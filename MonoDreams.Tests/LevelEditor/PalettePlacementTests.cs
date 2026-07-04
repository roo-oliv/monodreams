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
        bool leftDown = false, bool rightPressed = false, bool outsideViewport = false)
    {
        ref var input = ref cursor.Get<CursorInputComponent>();
        input.WorldPosition = world;
        input.VirtualPosition = world;
        input.LeftButtonPressed = leftPressed;
        input.LeftButton = leftDown || leftPressed;
        input.LeftButtonReleased = false;
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

        // Follows the cursor's world position.
        SetCursor(cursor, new Vector2(50, 60));
        palette.Update(Edit());
        Assert.True(palette.HasGhost);
        var ghost = palette.Ghost;
        Assert.Equal(new Vector2(50, 60), ghost.Get<TransformComponent>().Position);

        // Editor infrastructure, never scene content, tinted, feet-origin on the Y-sorted band.
        Assert.True(ghost.Has<EditorInfrastructureComponent>());
        Assert.False(ghost.Has<SceneObjectComponent>());
        var ghostSprite = ghost.Get<SpriteInfoComponent>();
        Assert.Equal(PalettePlacementSystem.GhostColor, ghostSprite.Color);
        Assert.Equal(new Vector2(16f, 48f), ghostSprite.Origin); // bottom-center of the 32×48 region
        Assert.Null(ghostSprite.AssetKey); // key-less: not a save candidate even by accident

        // Snap quantizes the ghost (and thus the placement) position.
        ref var state = ref gizmoState.Get<GizmoStateComponent>();
        state.SnapEnabled = true;
        state.GridStep = 16f;
        SetCursor(cursor, new Vector2(100.4f, 77.7f));
        palette.Update(Edit());
        Assert.Equal(new Vector2(96f, 80f), ghost.Get<TransformComponent>().Position);

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
    public void PlacementTest_OneUndoStepAutoSelectAndRepeat()
    {
        using var world = new World();
        MakeGizmoState(world);
        var cursor = MakeCursor(world);
        var (serializer, history) = MakeInfra(world);
        using var palette = MakePalette(world, history, serializer);

        palette.ArmByIndex(0);
        palette.SelectBand("Ground");

        // First press places one prop = one undo step, tagged + auto-selected.
        SetCursor(cursor, new Vector2(120, 80), leftPressed: true);
        palette.Update(Edit());

        var placed = Assert.Single(PlacedProps(world));
        Assert.Equal(1, history.Count);
        Assert.True(placed.Has<SceneObjectComponent>());
        Assert.Equal(placed, Selected(world));
        Assert.Equal(new Vector2(120, 80), placed.Get<TransformComponent>().Position);
        var sprite = placed.Get<SpriteInfoComponent>();
        Assert.Equal("file:Island/props/tree01.png", sprite.AssetKey);
        Assert.Equal(0.9f, sprite.LayerDepth); // the Ground band's SOURCE depth

        // Holding the button is not a press edge: nothing extra is placed.
        SetCursor(cursor, new Vector2(140, 90), leftDown: true);
        palette.Update(Edit());
        Assert.Single(PlacedProps(world));

        // A second click keeps placing (still armed), auto-select moves to the newest.
        SetCursor(cursor, new Vector2(200, 40), leftPressed: true);
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
