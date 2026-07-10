using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.System;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the island-authoring Slice 2 within-band ordering (plan §4.2): Bring forward /
/// Send back nudge the selection's SOURCE sort fields by <see cref="EditorCommandSystem.OrderStep"/>,
/// clamped inside the band the screen's <see cref="DrawLayerMap"/> resolves — a plain band
/// nudges <c>SpriteInfo.LayerDepth</c>, a Y-sorted band nudges <c>YSortDepthBias</c> and NEVER
/// <c>LayerDepth</c> (Y-sort participation is an exact-match band lookup) — one click = one undo
/// step. Names the live premise "Within-band ordering nudges SOURCE sort fields and never breaks
/// the band" in MonoDreams/level-editor/docs/premises.md.
/// </summary>
public class OrderingTests
{
    private enum TestLayer
    {
        Foreground,
        Characters,
        Background,
    }

    private static GameState Edit() => new(new GameTime()) { RunMode = RunMode.Edit };
    private static GameState Play() => new(new GameTime()) { RunMode = RunMode.Play };

    private static DrawLayerMap Layers() =>
        DrawLayerMap.FromEnum<TestLayer>().WithYSort(TestLayer.Characters);

    private static (EditorCommandSystem commands, EditorHistory history) NewCommands(
        World world, DrawLayerMap layers)
    {
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        var history = new EditorHistory(world);
        var commands = new EditorCommandSystem(
            world, history, new SceneSerializer(registry),
            layers: layers);
        return (commands, history);
    }

    private static Entity CreateSprite(World world, float layerDepth, bool selected = true)
    {
        var entity = world.CreateEntity();
        entity.Set(new TransformComponent(new Vector2(10, 10)));
        entity.Set(new SpriteInfoComponent
        {
            Size = new Vector2(32, 32),
            Target = RenderTargetID.Main,
            LayerDepth = layerDepth,
        });
        entity.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main });
        if (selected) entity.Set(new SelectedComponent());
        return entity;
    }

    // ---- Plain (non-Y-sorted) band: LayerDepth nudges, one undo step per click ----

    [Fact]
    public void BringForward_NudgesSourceLayerDepth_OneUndoStepPerClick()
    {
        using var world = new World();
        var layers = Layers();
        var bandDepth = layers.GetDepth(TestLayer.Background);
        var (commands, history) = NewCommands(world, layers);
        var sprite = CreateSprite(world, bandDepth);

        commands.BringForward(Edit());
        Assert.Equal(bandDepth + EditorCommandSystem.OrderStep,
            sprite.Get<SpriteInfoComponent>().LayerDepth, 6);
        Assert.Equal(1, history.Count);

        commands.SendBack(Edit());
        commands.SendBack(Edit());
        Assert.Equal(bandDepth - EditorCommandSystem.OrderStep,
            sprite.Get<SpriteInfoComponent>().LayerDepth, 6);
        Assert.Equal(3, history.Count);

        // Undo walks back exactly; the source field round-trips bit-for-bit.
        history.Undo();
        history.Undo();
        history.Undo();
        Assert.Equal(bandDepth, sprite.Get<SpriteInfoComponent>().LayerDepth);

        history.Redo();
        Assert.Equal(bandDepth + EditorCommandSystem.OrderStep,
            sprite.Get<SpriteInfoComponent>().LayerDepth, 6);
    }

    [Fact]
    public void Ordering_ClampsAtBandEdges()
    {
        using var world = new World();
        var layers = Layers();
        var bandDepth = layers.GetDepth(TestLayer.Background);
        Assert.True(layers.TryGetBandRange(bandDepth, out _, out var min, out var max, out var ySorted));
        Assert.False(ySorted);

        var (commands, history) = NewCommands(world, layers);
        var sprite = CreateSprite(world, bandDepth);

        // Walk to the front edge: enough clicks to cross the half-band, then assert the clamp.
        var clicks = (int)((max - bandDepth) / EditorCommandSystem.OrderStep) + 5;
        for (var i = 0; i < clicks; i++) commands.BringForward(Edit());
        Assert.Equal(max, sprite.Get<SpriteInfoComponent>().LayerDepth, 6);

        // At the edge, another click pushes NOTHING (no empty undo entries).
        var entries = history.Count;
        commands.BringForward(Edit());
        Assert.Equal(entries, history.Count);
        Assert.Equal(max, sprite.Get<SpriteInfoComponent>().LayerDepth, 6);

        // The nudged depth still resolves to the SAME band (the clamp keeps it inside).
        Assert.True(layers.TryGetBandRange(
            sprite.Get<SpriteInfoComponent>().LayerDepth, out var resolved, out _, out _, out _));
        Assert.Equal(bandDepth, resolved);
    }

    // ---- Y-sorted band: the bias nudges, LayerDepth NEVER moves (exact-match band lookup) ----

    [Fact]
    public void Ordering_OnYSortedBand_AdjustsBiasNeverLayerDepth()
    {
        using var world = new World();
        var layers = Layers();
        var bandDepth = layers.GetDepth(TestLayer.Characters);
        var (commands, history) = NewCommands(world, layers);
        var sprite = CreateSprite(world, bandDepth);

        commands.BringForward(Edit());
        ref readonly var info = ref sprite.Get<SpriteInfoComponent>();
        // LayerDepth untouched: nudging it would break the exact-match TryGetYSortRange lookup
        // and silently drop the sprite out of Y-sorting.
        Assert.Equal(bandDepth, info.LayerDepth);
        Assert.Equal(EditorCommandSystem.OrderStep, info.YSortDepthBias, 6);
        Assert.True(layers.TryGetYSortRange(info.LayerDepth, out _, out _));
        Assert.Equal(1, history.Count);

        commands.SendBack(Edit());
        commands.SendBack(Edit());
        Assert.Equal(-EditorCommandSystem.OrderStep, sprite.Get<SpriteInfoComponent>().YSortDepthBias, 6);
        Assert.Equal(bandDepth, sprite.Get<SpriteInfoComponent>().LayerDepth);

        history.Undo();
        history.Undo();
        history.Undo();
        Assert.Equal(0f, sprite.Get<SpriteInfoComponent>().YSortDepthBias);
    }

    // ---- The action targets the OWNER when a collider proxy is selected ----

    [Fact]
    public void Ordering_TargetsTheOwner_WhenAProxyIsSelected()
    {
        using var world = new World();
        var layers = Layers();
        var bandDepth = layers.GetDepth(TestLayer.Background);
        var (commands, _) = NewCommands(world, layers);

        var owner = CreateSprite(world, bandDepth, selected: false);
        owner.Set(new BoxColliderComponent(new Vector2(32, 32)));

        var proxy = world.CreateEntity();
        proxy.Set(new GizmoProxyComponent(owner, ProxyBindingKind.BoxColliderBounds));
        proxy.Set(new SelectedComponent());

        commands.BringForward(Edit());
        Assert.Equal(bandDepth + EditorCommandSystem.OrderStep,
            owner.Get<SpriteInfoComponent>().LayerDepth, 6);
    }

    // ---- Keyboard nudge rides the same path through Update ----

    [Fact]
    public void Ordering_KeyboardNudge_ThroughUpdate()
    {
        using var world = new World();
        var layers = Layers();
        var bandDepth = layers.GetDepth(TestLayer.Background);
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        var history = new EditorHistory(world);
        var fireForward = false;
        using var commands = new EditorCommandSystem(
            world, history, new SceneSerializer(registry),
            layers: layers,
            orderForwardRequested: _ => fireForward,
            orderBackRequested: _ => false);
        var sprite = CreateSprite(world, bandDepth);

        commands.Update(Edit());
        Assert.Equal(bandDepth, sprite.Get<SpriteInfoComponent>().LayerDepth);

        fireForward = true;
        commands.Update(Edit());
        Assert.Equal(bandDepth + EditorCommandSystem.OrderStep,
            sprite.Get<SpriteInfoComponent>().LayerDepth, 6);
        Assert.Equal(1, history.Count);
    }

    // ---- Guards: Play mode, no selection, no sprite, no layer map — loud no-ops ----

    [Fact]
    public void Ordering_Guards_AreNoOps()
    {
        using var world = new World();
        var layers = Layers();
        var bandDepth = layers.GetDepth(TestLayer.Background);
        var (commands, history) = NewCommands(world, layers);

        // No selection.
        commands.BringForward(Edit());
        Assert.Equal(0, history.Count);

        var sprite = CreateSprite(world, bandDepth);

        // Playing: ordering is an editing action.
        commands.BringForward(Play());
        Assert.Equal(0, history.Count);
        Assert.Equal(bandDepth, sprite.Get<SpriteInfoComponent>().LayerDepth);

        // A selection without a sprite has nothing to order.
        sprite.Remove<SelectedComponent>();
        var spriteless = world.CreateEntity();
        spriteless.Set(new TransformComponent(Vector2.Zero));
        spriteless.Set(new SelectedComponent());
        commands.BringForward(Edit());
        Assert.Equal(0, history.Count);
    }

    // ---- The DrawLayerMap band-containment seam the ordering clamps against ----

    [Fact]
    public void TryGetBandRange_ResolvesNudgedDepths_AndRejectsOutOfBand()
    {
        var layers = Layers(); // 3 layers → step 0.5, half-step 0.25
        var background = layers.GetDepth(TestLayer.Background); // 0.0
        var characters = layers.GetDepth(TestLayer.Characters); // 0.5

        // The exact band value and a nudged value both resolve to the band.
        Assert.True(layers.TryGetBandRange(background, out var band, out var min, out var max, out var ySorted));
        Assert.Equal(background, band);
        Assert.False(ySorted);
        Assert.Equal(background - 0.25f + 0.001f, min, 6);
        Assert.Equal(background + 0.25f - 0.001f, max, 6);

        Assert.True(layers.TryGetBandRange(background + 0.01f, out band, out _, out _, out _));
        Assert.Equal(background, band);

        // A Y-sorted band reports so, and its range matches TryGetYSortRange's.
        Assert.True(layers.TryGetBandRange(characters + 0.02f, out band, out min, out max, out ySorted));
        Assert.Equal(characters, band);
        Assert.True(ySorted);
        Assert.True(layers.TryGetYSortRange(characters, out var ysMin, out var ysMax));
        Assert.Equal(ysMin, min, 6);
        Assert.Equal(ysMax, max, 6);

        // Way outside every band (beyond the last layer's half-step): no band.
        Assert.False(layers.TryGetBandRange(1.9f, out _, out _, out _, out _));
    }
}
