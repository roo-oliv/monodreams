#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// The dominant sprite of a prefab, resolved for the Prefabs shelf card <b>thumbnail</b> (PF-G item 3)
/// and the placement <b>ghost</b> (PF-G item 4). A pure, allocation-light <b>root-first walk</b> of a
/// <see cref="PrefabData"/>'s entities (the root, then its <c>ChildOf</c> descendants breadth-first, in
/// document order) for the first entity carrying a <c>core.SpriteInfo</c> with a usable asset key — the
/// user's <c>house</c> prefab (root has only a collider, the child <c>House2</c> owns the sprite) is why
/// the walk must descend, not just read the root. The resolved <see cref="AssetKey"/> feeds the palette's
/// existing <c>file:</c>/content texture loader — the SAME path the asset cards use.
///
/// <para>The sprite's <see cref="Offset"/>/<see cref="Scale"/>/<see cref="Rotation"/> are its WORLD
/// transform WITHIN the prefab (composed from the sprite entity up the parent chain to the origin-normalized
/// root), so the ghost lands exactly where the placed instance's sprite will: the instance root goes at the
/// (snapped) cursor and the sprite sits at <c>cursor + Offset</c>, scaled/rotated by <see cref="Scale"/>/
/// <see cref="Rotation"/>. A prefab with no sprite resolves to none (the card falls back to its glyph, the
/// ghost to a crosshair).</para>
/// </summary>
public readonly struct PrefabSpritePreview
{
    /// <summary>The sprite's <c>core.SpriteInfo.assetKey</c> (a <c>file:</c> or content key) — fed to the
    /// palette's texture loader exactly as an asset card's key is.</summary>
    public string AssetKey { get; }

    /// <summary>The source sub-rectangle of the sheet the sprite draws.</summary>
    public Rectangle Source { get; }

    /// <summary>The sprite's draw origin (<c>core.SpriteInfo.origin</c>) — the ghost renders with it so it
    /// pivots as the placed sprite will.</summary>
    public Vector2 SpriteOrigin { get; }

    /// <summary>The sprite's source layer depth — the ghost's draw depth.</summary>
    public float LayerDepth { get; }

    /// <summary>The sprite entity's WORLD position within the prefab (root normalized to origin) — the
    /// offset from the placed instance root to where the sprite lands.</summary>
    public Vector2 Offset { get; }

    /// <summary>The sprite entity's WORLD scale within the prefab.</summary>
    public Vector2 Scale { get; }

    /// <summary>The sprite entity's WORLD rotation (radians) within the prefab.</summary>
    public float Rotation { get; }

    /// <summary>The dominant sprite's animation frame ASSET KEYS (from a <c>core.SpriteAnimation</c>
    /// on the same entity), in play order — null for a static sprite. The shelf card and the
    /// placement ghost cycle these (pixel-art wave: previews are animated).</summary>
    public IReadOnlyList<string>? SequenceFrames { get; }

    private PrefabSpritePreview(string assetKey, Rectangle source, Vector2 spriteOrigin, float layerDepth,
        Vector2 offset, Vector2 scale, float rotation, IReadOnlyList<string>? sequenceFrames)
    {
        AssetKey = assetKey;
        Source = source;
        SpriteOrigin = spriteOrigin;
        LayerDepth = layerDepth;
        Offset = offset;
        Scale = scale;
        Rotation = rotation;
        SequenceFrames = sequenceFrames;
    }

    /// <summary>
    /// Resolves <paramref name="prefab"/>'s dominant sprite via the root-first walk. Returns false (and
    /// <c>default</c>) when the prefab is null or no entity carries a <c>core.SpriteInfo</c> with a
    /// non-empty asset key.
    /// </summary>
    public static bool TryResolve(PrefabData? prefab, out PrefabSpritePreview preview)
    {
        preview = default;
        if (prefab == null) return false;
        var entities = prefab.Scene.Entities;
        if (entities == null || entities.Count == 0) return false;

        foreach (var i in RootFirstOrder(entities, prefab.RootIndex))
        {
            if (!entities[i].Components.TryGetValue(EngineComponentSerializers.SpriteInfoKey, out var spriteEl))
                continue;
            var assetKey = ReadString(spriteEl, "assetKey");
            if (string.IsNullOrEmpty(assetKey)) continue; // a keyless sprite can't be thumbnailed/ghosted

            ComposeWorld(entities, i, out var offset, out var scale, out var rotation);
            preview = new PrefabSpritePreview(
                assetKey!,
                ReadRect(spriteEl, "source"),
                ReadVec(spriteEl, "origin"),
                ReadFloat(spriteEl, "layerDepth"),
                offset, scale, rotation,
                ReadSequenceFrames(entities[i]));
            return true;
        }
        return false;
    }

    /// <summary>Entity indices in root-first breadth-first order: the root, then its direct children (in
    /// document order), then grandchildren, and so on. An entity unreachable from the root is skipped (a
    /// well-formed prefab has none — <see cref="PrefabData"/> validates the single connected tree).</summary>
    private static IEnumerable<int> RootFirstOrder(List<SceneEntityData> entities, int rootIndex)
    {
        var childrenOf = new Dictionary<int, List<int>>();
        for (var i = 0; i < entities.Count; i++)
        {
            var parent = entities[i].Parent;
            if (parent is { } p && p >= 0 && p < entities.Count && p != i)
                (childrenOf.TryGetValue(p, out var list) ? list : childrenOf[p] = new List<int>()).Add(i);
        }

        var queue = new Queue<int>();
        queue.Enqueue(rootIndex);
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            yield return i;
            if (childrenOf.TryGetValue(i, out var kids))
                foreach (var k in kids) queue.Enqueue(k);
        }
    }

    /// <summary>Composes the sprite entity's WORLD transform within the prefab: walks from
    /// <paramref name="index"/> up the parent chain, multiplying each level's local matrix
    /// (<c>T(-origin)·S·R·T(pos)</c> — the same construction as <c>TransformComponent.WorldMatrix</c>).
    /// The position is the matrix translation; scale is the product and rotation the sum up the chain
    /// (matching <c>TransformComponent.WorldScale</c>/<c>WorldRotation</c>). Bounded against a cycle.</summary>
    private static void ComposeWorld(List<SceneEntityData> entities, int index,
        out Vector2 offset, out Vector2 scale, out float rotation)
    {
        var matrix = Matrix.Identity;
        scale = Vector2.One;
        rotation = 0f;

        var cur = index;
        for (var guard = 0; guard <= entities.Count; guard++)
        {
            ReadTransform(entities[cur], out var pos, out var rot, out var scl, out var org);
            var local =
                Matrix.CreateTranslation(-org.X, -org.Y, 0f) *
                Matrix.CreateScale(scl.X, scl.Y, 1f) *
                Matrix.CreateRotationZ(rot) *
                Matrix.CreateTranslation(pos.X, pos.Y, 0f);
            matrix *= local;
            scale *= scl;
            rotation += rot;

            var parent = entities[cur].Parent;
            if (parent is not { } p || p < 0 || p >= entities.Count || p == cur) break;
            cur = p;
        }

        offset = new Vector2(matrix.M41, matrix.M42);
    }

    // ---- Defensive JsonElement readers (a malformed field degrades to a sensible default, never throws) ----

    private static void ReadTransform(SceneEntityData entity, out Vector2 pos, out float rot, out Vector2 scale, out Vector2 origin)
    {
        pos = Vector2.Zero;
        rot = 0f;
        scale = Vector2.One;
        origin = Vector2.Zero;
        if (!entity.Components.TryGetValue(EngineComponentSerializers.TransformKey, out var el)) return;
        pos = ReadVec(el, "position");
        rot = ReadFloat(el, "rotation");
        scale = ReadVec(el, "scale", fallback: Vector2.One);
        origin = ReadVec(el, "origin");
    }

    private static string? ReadString(JsonElement obj, string prop) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>The frame asset keys of a <c>core.SpriteAnimation</c> on <paramref name="entity"/>
    /// (in play order, key-less frames skipped), or null when the entity has none / no keyed frames.</summary>
    private static IReadOnlyList<string>? ReadSequenceFrames(SceneEntityData entity)
    {
        if (!entity.Components.TryGetValue(EngineComponentSerializers.SpriteAnimationKey, out var animEl) ||
            animEl.ValueKind != JsonValueKind.Object ||
            !animEl.TryGetProperty("frames", out var framesEl) ||
            framesEl.ValueKind != JsonValueKind.Array)
            return null;

        var keys = new List<string>();
        foreach (var frame in framesEl.EnumerateArray())
        {
            var key = ReadString(frame, "assetKey");
            if (!string.IsNullOrEmpty(key)) keys.Add(key!);
        }
        return keys.Count > 1 ? keys : null;
    }

    private static float ReadFloat(JsonElement obj, string prop) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetSingle()
            : 0f;

    private static Vector2 ReadVec(JsonElement obj, string prop, Vector2 fallback = default)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(prop, out var v) ||
            v.ValueKind != JsonValueKind.Array || v.GetArrayLength() < 2)
            return fallback;
        return new Vector2(v[0].GetSingle(), v[1].GetSingle());
    }

    private static Rectangle ReadRect(JsonElement obj, string prop)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(prop, out var v) ||
            v.ValueKind != JsonValueKind.Array || v.GetArrayLength() < 4)
            return Rectangle.Empty;
        return new Rectangle(v[0].GetInt32(), v[1].GetInt32(), v[2].GetInt32(), v[3].GetInt32());
    }
}
