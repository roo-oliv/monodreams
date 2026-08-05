#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Level;
using MonoDreams.State;

namespace MonoDreams.System.Level;

/// <summary>
/// The scene-layer draw remap (see <see cref="SceneLayerComponent"/>): woven into the DRAW
/// pipeline between <c>SpritePrepSystem</c> and <c>YSortSystem</c> (the documented layer-depth
/// ownership chain gains one stage), it rewrites each layer member's just-prepped
/// <c>DrawComponent.LayerDepth</c> from <c>(layer slice, within-layer key)</c>:
/// <c>final = sliceMin + clamp01(source LayerDepth) * sliceWidth</c>. Layers slice the
/// <see cref="BandMin"/>..<see cref="BandMax"/> range evenly by <see cref="SceneLayerComponent.Order"/>.
/// A HIDDEN layer's members draw fully transparent (color zeroed after prep — no render-path or
/// culling query changes). Membership is the <c>ChildOf</c> ancestor chain (a prefab instance's
/// sprites remap through their instance root's layer). Entities on no layer pass through
/// untouched. Runs in BOTH the editor and the game (a hidden layer ships hidden).
/// </summary>
public sealed class SceneLayerSystem : ISystem<GameState>
{
    /// <summary>The depth range dynamic layers slice (HUD text and code-built overlays keep the
    /// range above <see cref="BandMax"/>; legacy non-layered content keeps whatever it authored).</summary>
    public const float BandMin = 0.05f;
    public const float BandMax = 0.9f;

    /// <summary>Ancestor-walk guard against a malformed ChildOf cycle.</summary>
    private const int MaxParentWalk = 16;

    private readonly EntitySet _layers;
    private readonly EntitySet _members;
    private readonly List<Entity> _orderedBuffer = new();
    private readonly Dictionary<Entity, (float Min, float Width, bool Visible)> _slices = new();

    public bool IsEnabled { get; set; } = true;

    public SceneLayerSystem(World world)
    {
        _layers = world.GetEntities().With<SceneLayerComponent>().AsSet();
        _members = world.GetEntities()
            .With<ChildOfComponent>()
            .With<SpriteInfoComponent>()
            .With<DrawComponent>()
            .AsSet();
    }

    /// <summary>The world's layers ordered back-to-front — shared with a future editor panel and
    /// placement so the list, the paint routing, and the render all agree. An ON-DEMAND helper: it
    /// builds and disposes a one-shot set, so never call it per frame (the instance keeps a cached
    /// set for that).</summary>
    public static List<Entity> OrderedLayers(World world)
    {
        var list = new List<Entity>();
        using var layers = world.GetEntities().With<SceneLayerComponent>().AsSet();
        foreach (var layer in layers.GetEntities()) list.Add(layer);
        list.Sort(CompareLayers);
        return list;
    }

    /// <summary>Deterministic layer order: <see cref="SceneLayerComponent.Order"/>, then name.</summary>
    public static int CompareLayers(Entity a, Entity b)
    {
        var byOrder = a.Get<SceneLayerComponent>().Order.CompareTo(b.Get<SceneLayerComponent>().Order);
        if (byOrder != 0) return byOrder;
        return string.CompareOrdinal(LayerName(a), LayerName(b));
    }

    /// <summary>The layer's designer-facing name (its EntityInfo name, else a stable fallback).</summary>
    public static string LayerName(Entity layer) =>
        layer.Has<EntityInfoComponent>() ? layer.Get<EntityInfoComponent>().Name ?? "Layer" : "Layer";

    /// <summary>The draw-depth slice of the layer at <paramref name="index"/> of
    /// <paramref name="count"/> back-to-front layers.</summary>
    public static (float Min, float Width) Slice(int index, int count)
    {
        var width = (BandMax - BandMin) / Math.Max(1, count);
        return (BandMin + width * index, width);
    }

    /// <summary>The layer entity owning <paramref name="entity"/> (the nearest
    /// <see cref="SceneLayerComponent"/> ancestor), or a dead Entity when it is on no layer.</summary>
    public static Entity OwningLayer(Entity entity)
    {
        var current = entity;
        for (var i = 0; i < MaxParentWalk && current.IsAlive; i++)
        {
            if (!current.Has<ChildOfComponent>()) return default;
            var parent = current.Get<ChildOfComponent>().Parent;
            if (!parent.IsAlive) return default;
            if (parent.Has<SceneLayerComponent>()) return parent;
            current = parent;
        }
        return default;
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        // Order the layers and slice the band once per frame (the layer count is tiny).
        // SCREEN-SPACE layers (HUD grouping) are organizational, not draw bands — excluded from the
        // slicing so the world layers' slices stay stable and HUD members keep their authored depths.
        _orderedBuffer.Clear();
        foreach (var layer in _layers.GetEntities())
            if (!layer.Get<SceneLayerComponent>().ScreenSpace)
                _orderedBuffer.Add(layer);
        if (_orderedBuffer.Count == 0) return;
        _orderedBuffer.Sort(CompareLayers);

        _slices.Clear();
        for (var i = 0; i < _orderedBuffer.Count; i++)
        {
            var layer = _orderedBuffer[i];
            var (min, width) = Slice(i, _orderedBuffer.Count);
            _slices[layer] = (min, width, layer.Get<SceneLayerComponent>().Visible);
        }

        foreach (var member in _members.GetEntities())
        {
            var layer = OwningLayer(member);
            if (!layer.IsAlive || !_slices.TryGetValue(layer, out var slice)) continue;

            ref readonly var sprite = ref member.Get<SpriteInfoComponent>();
            var draw = member.Get<DrawComponent>();
            var key = MathHelper.Clamp(sprite.LayerDepth, 0f, 1f);
            draw.LayerDepth = slice.Min + key * slice.Width;
            if (!slice.Visible) draw.Color = Color.Transparent; // prep re-tints next frame when shown
        }
    }

    public void Dispose()
    {
        _layers.Dispose();
        _members.Dispose();
    }
}
