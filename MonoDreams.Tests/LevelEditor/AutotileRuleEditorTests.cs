#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using DefaultEcs;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Level;
using MonoDreams.LevelEditor.Assets;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Tile;
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.Renderer;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the <b>Autotile Rules workspace</b> (WS, issue #47) — the full-view rule-set editor whose
/// edits are LIVE undoable commands rather than a Save/Cancel modal:
///
/// <list type="bullet">
///   <item><b>A rule edit re-bakes visibly and undoes.</b> A <see cref="PaintValueEditCommand"/>
///   pushed through the ONE shared <see cref="EditorHistory"/> rewrites the value's
///   <c>AutotileRules</c> and publishes <c>NotifyChanged&lt;TileGridComponent&gt;</c>, so the debounced
///   <see cref="TileGridBakeSystem"/> re-derives the painted cells' SOURCE RECTS — the baked tile's
///   art actually changes — and one Undo walks the whole thing back.</item>
///   <item><b>Rule-set CRUD round-trips serialization.</b> A rule set born through
///   <see cref="AddPaintValueCommand"/> and edited through <see cref="PaintValueEditCommand"/>
///   survives <c>save → load</c> byte-stably (id / name / tilesetKey / tileSize / autotileRules), and
///   undoing its creation round-trips too (the value leaves the file, redo brings it back).</item>
///   <item><b>The workspace's own verbs</b> — <c>SelectValue</c> / <c>SelectValueByName</c> /
///   <c>SelectCase</c> / <c>ToggleTile</c> / <c>ApplyTilesetPick</c> — are each ONE history entry, and
///   the binding self-heals when undo removes the bound rule set out from under the view.</item>
/// </list>
///
/// Pure logic — an in-memory world, no <c>GraphicsDevice</c> and no font (the rules system takes a
/// nullable <c>BitmapFont</c>; the bake's texture seam is fed the ctor-less
/// <see cref="Texture2D"/> stand-in the rendering tests already use).
///
/// Names the level-editor premises "The Autotile Rules WORKSPACE edits rule sets LIVE through the one
/// shared history" (all of the above), "Bounded undo with drag-coalescing" (the history the edits ride)
/// and "The Paint tab arms a tile-grid brush …" (the rule sets ARE that tab's paintable indices); plus
/// the level-loading premise "The paint grid is authored cells + values; everything visible/collidable
/// is a bake product" (what a rules edit re-derives).
/// </summary>
public class AutotileRuleEditorTests
{
    private const float CellSize = 32f;
    private const int TileSize = 32;
    private const string Sheet = "file:Terrain/tiles.png";

    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };

    private static ViewportManager Vm() =>
        new(null!, 800, 600) { ScreenWidth = 1600, ScreenHeight = 900, DevicePixelRatio = 1f };

    /// <summary>
    /// A stand-in <see cref="Texture2D"/>: the bake's sprite path only ever COPIES the reference onto
    /// the baked tile's <c>SpriteInfoComponent.SpriteSheet</c> (the source RECT comes from the rules
    /// table, not the texture), and a real texture needs a <c>GraphicsDevice</c> no unit test has. Same
    /// ctor-less trick as <c>SpriteFlipTests.StubTexture</c>, with the finalizer suppressed (it would
    /// dereference the null graphics device).
    /// </summary>
    private static Texture2D StubTexture()
    {
        var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
        GC.SuppressFinalize(texture);
        return texture;
    }

    // ---- rig -----------------------------------------------------------------------------------

    /// <summary>A Paint layer anchored at the origin holding ONE rule set ("Rock", id 1) bound to a
    /// sheet, plus a solid <paramref name="span"/>×<paramref name="span"/> block of painted cells — so
    /// the block's interior cell carries neighbour mask 15 and its top-left corner mask 6
    /// (right + down), the two cases the rule edits below target.</summary>
    private static (Entity Grid, TileGridComponent Data, TilePaintValue Value) MakePaintLayer(
        World world, string? rules = "15:0,0", int span = 3)
    {
        var value = new TilePaintValue
        {
            Id = 1,
            Name = "Rock",
            Color = Color.SaddleBrown,
            TilesetKey = Sheet,
            TileSize = TileSize,
            AutotileRules = rules,
        };
        var data = new TileGridComponent { CellSize = CellSize };
        data.Values.Add(value);
        for (var y = 0; y < span; y++)
        for (var x = 0; x < span; x++)
            data.Cells[TileGridComponent.Pack(x, y)] = value.Id;

        var grid = world.CreateEntity();
        grid.Set(new TransformComponent(Vector2.Zero));
        grid.Set(new EntityInfoComponent("Layer", "Terrain"));
        grid.Set(new SceneLayerComponent { Order = 0 });
        grid.Set(data);
        return (grid, data, value);
    }

    private static TileGridBakeSystem NewBake(World world) =>
        new(world, resolveTexture: _ => StubTexture());

    /// <summary>The baked tile SPRITE covering cell (<paramref name="x"/>, <paramref name="y"/>) — the
    /// bake places tiles UNPARENTED at the grid anchor + the cell's top-left, and re-creates them on
    /// every bake, so this is re-queried after each re-bake rather than cached.</summary>
    private static SpriteInfoComponent BakedTileAt(World world, int x, int y)
    {
        var position = new Vector2(x * CellSize, y * CellSize);
        using var set = world.GetEntities().With<BakedProductComponent>().With<SpriteInfoComponent>().AsSet();
        foreach (var e in set.GetEntities())
            if (e.Get<TransformComponent>().Position == position)
                return e.Get<SpriteInfoComponent>();
        throw new InvalidOperationException($"no baked tile at cell ({x},{y})");
    }

    /// <summary>Runs the bake past its change-debounce window (a rules edit publishes
    /// <c>NotifyChanged</c>, which the bake coalesces over <see cref="TileGridBakeSystem.QuietFrames"/>
    /// frames of silence exactly like a paint stroke).</summary>
    private static void SettleBake(TileGridBakeSystem bake)
    {
        for (var frame = 0; frame < TileGridBakeSystem.QuietFrames; frame++) bake.Update(Edit());
    }

    private static Rectangle Cell(int col, int row) => new(col * TileSize, row * TileSize, TileSize, TileSize);

    private static AutotileRuleEditorSystem NewRulesEditor(World world, EditorShellStateComponent shell,
        EditorHistory history, Action<string, EditorNotifySeverity>? notify = null)
    {
        // The texture seam: every key opens (a dummy stream) and decodes to the ctor-less stand-in, so
        // the workspace's sheet lookups resolve without a GraphicsDevice.
        var textures = new FileAssetTextureLoader(
            _ => new MemoryStream(new byte[] { 0 }),
            _ => StubTexture(),
            () => null);
        return new AutotileRuleEditorSystem(world, Vm(), textures, font: null, shell, history, notify);
    }

    // ═══ 1. A rule edit re-bakes VISIBLY, and one Undo restores the previous skin ═══════════════════

    [Fact]
    public void RulesEdit_ReBakesTheTileSourceRects_PerNeighborMask_AndUndoRestoresThem()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        // The bake subscribes to the grid component being ADDED, so it must exist first (the component
        // lifecycle IS the bake trigger — the same order the scene reader produces).
        using var bake = NewBake(world);
        var (grid, _, value) = MakePaintLayer(world);

        bake.Update(Edit());
        Assert.Equal(1, bake.BakeCount);
        // Only "15" is mapped, and 15 is the fallback for every other mask → the whole block is cell 0,0.
        Assert.Equal(Cell(0, 0), BakedTileAt(world, 1, 1).Source); // interior, mask 15
        Assert.Equal(Cell(0, 0), BakedTileAt(world, 0, 0).Source); // top-left corner, mask 6 (R|D)
        Assert.Equal(Sheet, BakedTileAt(world, 1, 1).AssetKey);

        // The workspace's edit: assign sheet cell (5,5) to the mask-6 case only. LIVE — no Save step.
        history.Push(PaintValueEditCommand.Rules(grid, value, "6:5,5 15:0,0"));

        Assert.Equal("6:5,5 15:0,0", value.AutotileRules);
        Assert.Equal(1, history.Count); // exactly one undo step per rules edit
        SettleBake(bake);

        Assert.Equal(2, bake.BakeCount); // the command's NotifyChanged reached the bake
        Assert.Equal(Cell(5, 5), BakedTileAt(world, 0, 0).Source); // the corner RE-SKINNED
        Assert.Equal(Cell(0, 0), BakedTileAt(world, 1, 1).Source); // the interior did not (mask-targeted)

        history.Undo();

        Assert.Equal("15:0,0", value.AutotileRules);
        SettleBake(bake);
        Assert.Equal(3, bake.BakeCount);
        Assert.Equal(Cell(0, 0), BakedTileAt(world, 0, 0).Source); // back to the previous art
        Assert.True(history.CanRedo);

        history.Redo();
        SettleBake(bake);
        Assert.Equal(Cell(5, 5), BakedTileAt(world, 0, 0).Source);
    }

    [Fact]
    public void ToggleTile_ThroughTheWorkspace_IsOneUndoableStep_ThatReSkinsTheSelectedCase()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var shell = new EditorShellStateComponent();
        using var bake = NewBake(world);
        var (grid, _, value) = MakePaintLayer(world);
        using var rules = NewRulesEditor(world, shell, history);

        bake.Update(Edit());
        Assert.Equal(Cell(0, 0), BakedTileAt(world, 1, 1).Source);

        rules.SelectValue(grid, value.Id);
        rules.SelectCase(15);
        rules.ToggleTile(4, 1); // ADD an alternate to the interior case

        Assert.Equal(1, history.Count);
        var table = TileGridBaking.ParseRules(value.AutotileRules);
        Assert.Equal(new[] { new Point(0, 0), new Point(4, 1) }, table[15]);

        rules.ToggleTile(0, 0); // toggling an assigned cell REMOVES it — the case keeps only (4,1)

        Assert.Equal(2, history.Count);
        Assert.Equal(new Point(4, 1), Assert.Single(TileGridBaking.ParseRules(value.AutotileRules)[15]));
        SettleBake(bake);
        Assert.Equal(Cell(4, 1), BakedTileAt(world, 1, 1).Source); // the interior re-skinned

        history.Undo();
        history.Undo();

        Assert.Equal(new Point(0, 0), Assert.Single(TileGridBaking.ParseRules(value.AutotileRules)[15]));
        SettleBake(bake);
        Assert.Equal(Cell(0, 0), BakedTileAt(world, 1, 1).Source);
    }

    [Fact]
    public void ToggleTile_NeverEmptiesACase_TheLastRemovalFallsBackToSheetCellZero()
    {
        // A case with no tile at all has no meaning to the bake (PickTile would index an empty
        // alternates array), so removing the last assignment restores the 0,0 default instead.
        using var world = new World();
        var history = new EditorHistory(world);
        var (grid, _, value) = MakePaintLayer(world, rules: "15:7,3");
        using var rules = NewRulesEditor(world, new EditorShellStateComponent(), history);

        rules.SelectValue(grid, value.Id);
        rules.SelectCase(15);
        rules.ToggleTile(7, 3);

        Assert.Equal(new Point(0, 0), Assert.Single(TileGridBaking.ParseRules(value.AutotileRules)[15]));
        Assert.Equal(1, history.Count);
    }

    [Fact]
    public void SerializeRules_IsTheCanonicalDsl_ParseRulesReadsItBackUnchanged()
    {
        // The workspace writes the rule string the bake's own parser reads — the DSL toggle shows the
        // same text, and the Inspector's hand-edited field is the identical grammar.
        var table = TileGridBaking.ParseRules("0:1,2 6:3,4|5,6 15:0,0");

        var dsl = AutotileRuleEditorSystem.SerializeRules(table);
        var reparsed = TileGridBaking.ParseRules(dsl);

        Assert.StartsWith("0:1,2 1:", dsl);          // all 16 cases, space-separated, mask-ordered
        Assert.Contains("6:3,4|5,6", dsl);           // alternates joined by '|'
        for (var mask = 0; mask < 16; mask++)
            Assert.Equal(table[mask], reparsed[mask]);
    }

    [Fact]
    public void ApplyTilesetPick_IsOneUndoableCommand_ThatKeepsTheRules()
    {
        // Masks are sheet-agnostic, so re-binding a sheet must not clear the case→cell mapping the
        // designer already authored (the cell indices are theirs to fix up next).
        using var world = new World();
        var history = new EditorHistory(world);
        var notifications = new List<string>();
        var (grid, _, value) = MakePaintLayer(world, rules: "15:2,2");
        using var rules = NewRulesEditor(world, new EditorShellStateComponent(), history,
            notify: (message, _) => notifications.Add(message));

        rules.ApplyTilesetPick(grid, value.Id, "file:Terrain/other.png", 16);

        Assert.Equal("file:Terrain/other.png", value.TilesetKey);
        Assert.Equal(16, value.TileSize);
        Assert.Equal("15:2,2", value.AutotileRules); // untouched
        Assert.Equal(1, history.Count);
        Assert.NotEmpty(notifications); // a silent re-bind is indistinguishable from a dead button

        history.Undo();

        Assert.Equal(Sheet, value.TilesetKey);
        Assert.Equal(TileSize, value.TileSize);
        Assert.Equal("15:2,2", value.AutotileRules);
    }

    [Fact]
    public void SelectValueByName_BindsAcrossEveryGrid_AndIsCaseInsensitive()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        MakePaintLayer(world);                       // "Rock" on grid A
        var (second, data, _) = MakePaintLayer(world); // a second Indexed layer
        data.Values.Add(new TilePaintValue { Id = 2, Name = "Grass", TilesetKey = Sheet, TileSize = 8 });
        using var rules = NewRulesEditor(world, new EditorShellStateComponent(), history);

        Assert.True(rules.SelectValueByName("grass"));
        Assert.Equal(second, rules.CurrentLayer);
        Assert.Equal((byte)2, rules.CurrentValueId);

        Assert.False(rules.SelectValueByName("nope")); // an unknown name leaves the binding alone
        Assert.Equal((byte)2, rules.CurrentValueId);
    }

    [Fact]
    public void UndoingARuleSetsCreation_ReBindsTheViewToASurvivingRuleSet()
    {
        // Undo/redo and deletes can invalidate the bound rule set under an open workspace; the view
        // heals on its next frame instead of editing a value that no longer exists.
        using var world = new World();
        var history = new EditorHistory(world);
        var shell = new EditorShellStateComponent();
        var (grid, _, _) = MakePaintLayer(world);
        using var rules = NewRulesEditor(world, shell, history);

        var added = new TilePaintValue { Id = 2, Name = "Sand", TilesetKey = Sheet, TileSize = TileSize };
        history.Push(new AddPaintValueCommand(grid, added));
        rules.SelectValue(grid, added.Id);
        Assert.Equal((byte)2, rules.CurrentValueId);

        history.Undo(); // the bound rule set is gone
        rules.OpenWorkspace();
        rules.Update(Edit());

        Assert.Equal(grid, rules.CurrentLayer);
        Assert.Equal((byte)1, rules.CurrentValueId); // re-bound to the surviving "Rock"
    }

    // ═══ 2. Rule-set CRUD round-trips the canonical serializer ══════════════════════════════════════

    private static (ComponentSerializerRegistry Registry, SceneSerializer Serializer) NewSerializer()
    {
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        return (registry, new SceneSerializer(registry));
    }

    private static List<TilePaintValue> ReloadedValues(SceneSerializer serializer, Entity grid)
    {
        var scene = serializer.Serialize(new List<Entity> { grid });
        using var fresh = new World();
        var loaded = serializer.Deserialize(fresh, scene);
        return Assert.Single(loaded).Get<TileGridComponent>().Values;
    }

    [Fact]
    public void CreatedAndEditedRuleSet_RoundTripsThroughTheCanonicalSerializer()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var shell = new EditorShellStateComponent();
        var (_, serializer) = NewSerializer();

        // An Indexed layer with no rule sets at all — the "+ New Rule Set…" starting point.
        var data = new TileGridComponent { CellSize = CellSize };
        var grid = world.CreateEntity();
        grid.Set(new TransformComponent(Vector2.Zero));
        grid.Set(new EntityInfoComponent("Layer", "Terrain"));
        grid.Set(new SceneLayerComponent { Order = 0 });
        grid.Set(data);
        using var rules = NewRulesEditor(world, shell, history);

        // Create → bind a sheet → author the case mapping, each its own undoable command.
        var created = new TilePaintValue { Id = 3, Name = "Cliff", Color = new Color(90, 150, 210, 255) };
        history.Push(new AddPaintValueCommand(grid, created));
        rules.SelectValue(grid, created.Id);
        rules.ApplyTilesetPick(grid, created.Id, Sheet, 16);
        rules.SelectCase(6);
        rules.ToggleTile(2, 1);
        Assert.Equal(3, history.Count);

        var authored = data.FindValue(3)!;
        Assert.Equal(Sheet, authored.TilesetKey);
        Assert.Equal(16, authored.TileSize);

        // save → load: every authored field of the rule set survives.
        var reloaded = Assert.Single(ReloadedValues(serializer, grid));
        Assert.Equal((byte)3, reloaded.Id);
        Assert.Equal("Cliff", reloaded.Name);
        Assert.Equal(new Color(90, 150, 210, 255), reloaded.Color);
        Assert.Equal(Sheet, reloaded.TilesetKey);
        Assert.Equal(16, reloaded.TileSize);
        Assert.Equal(authored.AutotileRules, reloaded.AutotileRules);
        Assert.Equal(new[] { new Point(0, 0), new Point(2, 1) },
            TileGridBaking.ParseRules(reloaded.AutotileRules)[6]);

        // The rules edit undoes on its own — the sheet binding and the value stay.
        history.Undo();
        var afterRulesUndo = Assert.Single(ReloadedValues(serializer, grid));
        Assert.Equal(Sheet, afterRulesUndo.TilesetKey);
        Assert.Equal(new Point(0, 0), Assert.Single(TileGridBaking.ParseRules(afterRulesUndo.AutotileRules)[6]));

        // …then the tileset binding…
        history.Undo();
        var afterTilesetUndo = Assert.Single(ReloadedValues(serializer, grid));
        Assert.Null(afterTilesetUndo.TilesetKey);
        Assert.Equal(32, afterTilesetUndo.TileSize); // the TilePaintValue default

        // …then the creation itself: the rule set leaves the FILE, and redo brings it all back.
        history.Undo();
        Assert.Empty(ReloadedValues(serializer, grid));

        history.Redo();
        history.Redo();
        history.Redo();
        var restored = Assert.Single(ReloadedValues(serializer, grid));
        Assert.Equal("Cliff", restored.Name);
        Assert.Equal(Sheet, restored.TilesetKey);
        Assert.Equal(16, restored.TileSize);
        Assert.Equal(new[] { new Point(0, 0), new Point(2, 1) },
            TileGridBaking.ParseRules(restored.AutotileRules)[6]);
    }

    [Fact]
    public void RuleSetEdits_AreByteStable_LoadSaveIsAFixedPoint()
    {
        using var world = new World();
        var history = new EditorHistory(world);
        var (registry, serializer) = NewSerializer();
        var (grid, _, value) = MakePaintLayer(world);
        using var rules = NewRulesEditor(world, new EditorShellStateComponent(), history);

        rules.SelectValue(grid, value.Id);
        rules.SelectCase(9);
        rules.ToggleTile(1, 4);
        history.Push(PaintValueEditCommand.Tileset(grid, value, "file:Terrain/cliffs.png", 24));

        var written = registry.SerializeEntity(grid).Components[EngineComponentSerializers.TileGridKey];
        var scene = serializer.Serialize(new List<Entity> { grid });
        using var fresh = new World();
        var loaded = Assert.Single(serializer.Deserialize(fresh, scene));

        var rewritten = registry.SerializeEntity(loaded).Components[EngineComponentSerializers.TileGridKey];
        Assert.Equal(written.GetRawText(), rewritten.GetRawText());
    }
}
