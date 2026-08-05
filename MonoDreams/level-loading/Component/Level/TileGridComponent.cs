#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoDreams.Component.Level;

/// <summary>
/// One paintable VALUE of a <see cref="TileGridComponent"/> — the LDtk-IntGrid-style bundle the
/// designer paints with: a name + overlay color, the collision the bake derives (layers/passive/
/// identity), and the visuals the bake derives (a tileset + autotile rules). Pure data; the bake
/// (<c>TileGridBakeSystem</c>) turns painted cells into derived child entities.
/// </summary>
public sealed class TilePaintValue
{
    /// <summary>The cell value painted into <see cref="TileGridComponent.Cells"/> (1..255; 0 = empty).</summary>
    public byte Id;

    /// <summary>Designer-facing name ("Wall", "Spikes") — also the paint card label.</summary>
    public string Name = string.Empty;

    /// <summary>The paint-view overlay / card swatch color (straight alpha; overlays premultiply).</summary>
    public Color Color = Color.White;

    /// <summary>Collision layers of the baked (greedy-merged) collider rectangles. EMPTY = this
    /// value bakes no colliders (a pure-visual paint).</summary>
    public int[] ActiveLayers = Array.Empty<int>();

    /// <summary>Whether baked colliders are passive (static world geometry — the default).</summary>
    public bool Passive = true;

    /// <summary>The <c>EntityInfoComponent.Type</c> stamped on baked collider entities (defaults to
    /// <see cref="Name"/>) — what game systems pattern-match; also the key the game's bake-configure
    /// hook dispatches on (attaching e.g. a hazard component).</summary>
    public string? EntityType;

    /// <summary>The tile sheet's asset key (<c>file:</c> or content). Null = no baked visuals
    /// (an invisible collision-only paint).</summary>
    public string? TilesetKey;

    /// <summary>The sheet's source cell size in pixels (the sheet is a uniform grid of these).</summary>
    public int TileSize = 32;

    /// <summary>
    /// The autotile mapping DSL: space-separated <c>mask:col,row</c> entries, where <c>mask</c> is
    /// the 4-bit SAME-NEIGHBOR mask (U=1, R=2, D=4, L=8 — bit SET means the neighbor holds the same
    /// value) and <c>col,row</c> a cell of the tileset; alternates split by <c>|</c>
    /// (<c>"15:1,1|6,0|7,0"</c>) are picked by a deterministic per-cell hash. A missing mask falls
    /// back to the <c>15</c> entry, then to cell 0,0. Null/empty = every cell renders sheet cell 0,0.
    /// </summary>
    public string? AutotileRules;

    /// <summary>SOURCE layer depth of the baked tile sprites (which draw band the paint renders in).</summary>
    public float LayerDepth = 0.25f;
}

/// <summary>
/// A paintable logical tile grid — the scene's "IntGrid": a sparse map of cells to
/// <see cref="TilePaintValue"/> ids, authored by the editor's paint tool and serialized as ordinary
/// component data (<c>core.TileGrid</c> — the one-data-model tenet; no special file block). What
/// the player SEES and COLLIDES with is derived: <c>TileGridBakeSystem</c> re-bakes tile sprites
/// (autotile-picked) and greedy-merged collider rectangles as <c>BakedProductComponent</c> children
/// on every load/change — so replacing a tileset PNG or editing a rule re-skins the whole terrain,
/// and the scene file stays small (values + cells, never the derived entities).
/// </summary>
public sealed class TileGridComponent
{
    /// <summary>World size of one cell (the paint grid quantum; usually the editor snap step).</summary>
    public float CellSize = 32f;

    /// <summary>The paintable values (authored; Inspector-editable per scene).</summary>
    public List<TilePaintValue> Values = new();

    /// <summary>The painted cells: packed (x,y) → value id. Sparse — unpainted cells are absent.
    /// Cell (0,0)'s top-left sits at the grid ENTITY's transform position (the one anchor — moving
    /// the grid entity slides the whole painted terrain, baked children ride the parent matrix).</summary>
    public Dictionary<long, byte> Cells = new();

    /// <summary>Packs a signed cell coordinate into the dictionary key.</summary>
    public static long Pack(int x, int y) => ((long)y << 32) | (uint)x;

    /// <summary>Unpacks a dictionary key into the signed cell coordinate.</summary>
    public static (int X, int Y) Unpack(long key) => ((int)(uint)(key & 0xFFFFFFFF), (int)(key >> 32));

    /// <summary>The value definition for <paramref name="id"/>, or null.</summary>
    public TilePaintValue? FindValue(byte id)
    {
        foreach (var value in Values)
            if (value.Id == id) return value;
        return null;
    }

    /// <summary>The cell under <paramref name="worldPosition"/>, given the grid entity's world
    /// position <paramref name="gridWorldPosition"/> (the anchor).</summary>
    public (int X, int Y) CellAt(Vector2 worldPosition, Vector2 gridWorldPosition) => (
        (int)MathF.Floor((worldPosition.X - gridWorldPosition.X) / CellSize),
        (int)MathF.Floor((worldPosition.Y - gridWorldPosition.Y) / CellSize));

    /// <summary>Cell (<paramref name="x"/>, <paramref name="y"/>)'s top-left corner in the grid
    /// entity's LOCAL space (baked children parent to the grid, so local IS the layout).</summary>
    public Vector2 CellTopLeft(int x, int y) => new(x * CellSize, y * CellSize);
}
