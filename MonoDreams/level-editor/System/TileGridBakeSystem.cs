#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Level;
using MonoDreams.Extension;
using MonoDreams.LevelEditor.Tile;
using MonoDreams.LevelEditor.Component;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// Bakes every <see cref="TileGridComponent"/> into its DERIVED entities — the boundary-bake
/// pattern, applied to the paint grid: tile SPRITES (one per painted cell with visuals, source
/// rect picked by the value's autotile rules) and COLLIDERS (greedy-merged rectangles per value —
/// never per-cell: flush-adjacent colliders seam-catch swept AABBs, and merged rects are what the
/// reference levels hand-author). Products are <c>SetParent</c>-ed children tagged
/// <see cref="BakedProductComponent"/>: <b>never serialized</b> (the scene persists only the grid
/// component), disposed + re-created on every bake — so replacing a tileset PNG, editing a rule,
/// or repainting cells re-derives the world. Runs in BOTH the editor and the game (the scene
/// reader adding the component IS the bake trigger, the component-lifecycle convention).
///
/// <para><b>Sprites STREAM in chunks; colliders bake whole.</b> One entity per painted cell is what
/// makes a big world unaffordable (a 6400x2400px world is ~80k cells — 80k transforms walked by
/// culling every frame). So tile sprites bake per <see cref="ChunkCells"/>-square CHUNK, and only
/// for chunks overlapping <see cref="FocusBounds"/> (the camera's world view) plus a one-chunk
/// margin: live sprite entities become proportional to the VIEW, not to the world. Chunks that
/// leave the margin are disposed; missing ones bake at <see cref="ChunksPerFrame"/> per frame so a
/// fast camera never hitches (the first fill after a load is bigger — see
/// <see cref="FirstFillChunks"/> — so a level looks right on arrival, not three frames later).
/// Autotile neighbour masks read the WHOLE cell map, so a chunk border is seamless.
/// Colliders deliberately do NOT stream: they are already few (merged rects), and streaming them
/// would cut merged runs at chunk borders — reintroducing exactly the flush-adjacent seams the
/// merge exists to avoid. <see cref="FocusBounds"/> left null bakes everything at once (tests,
/// headless tools, small scenes) — the pre-streaming behaviour, byte for byte.</para>
///
/// <para><b>Tile sprites are UNPARENTED and CULLING-EXEMPT</b>, unlike the colliders (which stay
/// parented to the grid). Both are pure cost avoidance at scale, and both are safe because the
/// streamer already owns tile lifetime and visibility: a parented tile would join
/// <c>HierarchySystem</c>'s five per-frame passes over every ChildOf entity (~4k dictionary inserts
/// a frame for a view's worth of terrain), and a culled tile would be bounds-tested every frame
/// against a view it is inside by construction. The cost of that: a streamed grid's tiles do not
/// ride the grid entity's transform (moving the grid needs a re-bake to move its tiles) and do not
/// participate in <c>SceneLayerSystem</c>'s layer-order depth remapping — they draw at their paint
/// value's own <c>LayerDepth</c>.</para>
///
/// <para><b>Debounce.</b> A component-ADDED bakes immediately (a loaded scene must collide on
/// frame one). A CHANGED (paint stroke edits publish <c>NotifyChanged</c>) waits
/// <see cref="QuietFrames"/> frames of silence — mid-stroke repaints don't thrash thousands of
/// entities per frame; the paint overlay gives the live feedback instead.</para>
///
/// <para><b>Game seam.</b> <paramref name="configureCollider"/> lets the game attach its own
/// components to a baked collider (a hazard marker on "Thorn" rects) keyed by the paint value —
/// the module never references a game component. <paramref name="resolveTexture"/> is the screen's
/// content-or-<c>file:</c> resolver (the same one the scene reader and the animation system use).</para>
/// </summary>
public sealed class TileGridBakeSystem : ISystem<GameState>
{
    /// <summary>Frames of change-silence before a changed grid re-bakes (~0.13s at 60fps).</summary>
    public const int QuietFrames = 8;

    /// <summary>Cells per side of one streamed sprite chunk. Small enough that baking one is a
    /// trivial slice of a frame (a full 16x16 chunk is 256 sprites), big enough that the resident
    /// set around a view stays a few dozen chunks.</summary>
    public const int ChunkCells = 16;

    /// <summary>Chunks baked per frame while streaming (the steady-state budget).</summary>
    public int ChunksPerFrame { get; set; } = 3;

    /// <summary>Chunks baked in the frame a grid first fills (a load / re-bake): the view's worth of
    /// terrain lands at once instead of streaming in visibly.</summary>
    public int FirstFillChunks { get; set; } = 128;

    /// <summary>Hard ceiling on resident chunks per grid, nearest-to-view-centre first. A zoomed-out
    /// editor view over a big world would otherwise ask for the whole world at once; the cap trades
    /// far-away terrain visuals (the paint overlay still shows the cells) for a live frame rate.</summary>
    public int MaxResidentChunks { get; set; } = 192;

    /// <summary>
    /// Bake each chunk as ONE TEXTURED MESH instead of one sprite entity per cell. The cells drawn
    /// are identical; what changes is the cost of drawing them — a 16x16 chunk becomes 1 entity and
    /// 1 draw call rather than up to 256 of each, which takes per-frame culling, sprite prep, depth
    /// sorting, SpriteBatch quad building and entity churn from "proportional to visible cells" to
    /// "proportional to visible chunks" (~100x fewer).
    /// <para><b>What it costs:</b> a batched chunk has no <c>SpriteInfoComponent</c>, so it does not
    /// participate in <c>YSortSystem</c> or <c>SceneLayerSystem</c>'s layer-order depth remapping —
    /// it draws at its paint value's own <c>LayerDepth</c> (cells are grouped into one mesh per
    /// distinct depth, so lava over rock still layers correctly). Terrain is a flat backdrop for
    /// Y-sorting purposes, so that is a fair trade for a big world; leave it OFF (the default) for a
    /// scene whose painted layers are reordered in the editor.</para>
    /// </summary>
    public bool BatchChunks { get; set; }

    /// <summary>
    /// The world-space rect whose terrain must be baked — the camera's visible bounds
    /// (<c>Camera.VirtualScreenBounds</c>), wired by the screen. Null (the default) disables
    /// streaming entirely: every chunk bakes on the spot, which is what tests, headless tools and
    /// small scenes want.
    /// </summary>
    public Func<Rectangle>? FocusBounds { get; set; }


    private readonly World _world;
    private readonly Func<string, Texture2D?>? _resolveTexture;
    private readonly Action<Entity, TilePaintValue>? _configureCollider;
    private readonly EntitySet _bakedSet;
    private readonly IDisposable _addedSubscription;
    private readonly IDisposable _changedSubscription;
    private readonly HashSet<Entity> _bakeNow = new();
    private readonly Dictionary<Entity, int> _quiet = new();
    private readonly List<Entity> _disposeBuffer = new();
    private readonly List<Entity> _readyBuffer = new();
    private readonly Dictionary<string, Texture2D?> _textureBySheet = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _warnedSheets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-grid streaming state (live chunks + the value lookup built at bake time).</summary>
    private readonly Dictionary<Entity, GridStream> _streams = new();
    private readonly List<long> _wanted = new();
    private readonly HashSet<long> _wantedSet = new();
    private readonly List<long> _evict = new();
    private readonly List<Entity> _deadGrids = new();
    /// <summary>Batched-bake scratch: a chunk's quads grouped by (sheet, depth), reused per chunk.</summary>
    private readonly Dictionary<(Texture2D Texture, float Depth), MeshGroup> _groups = new();
    private bool _warnedChunkCap;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Total bakes performed (test observability).</summary>
    public int BakeCount { get; private set; }

    /// <summary>Live streamed chunks across every grid (test/telemetry observability).</summary>
    public int ResidentChunkCount
    {
        get
        {
            var total = 0;
            foreach (var stream in _streams.Values) total += stream.Chunks.Count;
            return total;
        }
    }

    public TileGridBakeSystem(World world,
        Func<string, Texture2D?>? resolveTexture = null,
        Action<Entity, TilePaintValue>? configureCollider = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _resolveTexture = resolveTexture;
        _configureCollider = configureCollider;
        _bakedSet = world.GetEntities().With<BakedProductComponent>().With<ChildOfComponent>().AsSet();
        _addedSubscription = world.SubscribeEntityComponentAdded<TileGridComponent>(OnAdded);
        _changedSubscription = world.SubscribeEntityComponentChanged<TileGridComponent>(OnChanged);
    }

    private void OnAdded(in Entity entity, in TileGridComponent value) => _bakeNow.Add(entity);

    private void OnChanged(in Entity entity, in TileGridComponent oldValue, in TileGridComponent newValue) =>
        _quiet[entity] = 0;

    /// <summary>Forces a re-bake of every live grid next Update — the editor's asset-refresh hook
    /// (a re-scanned tileset PNG must re-skin the baked tiles).</summary>
    public void InvalidateAll()
    {
        _textureBySheet.Clear();
        _warnedSheets.Clear();
        using var grids = _world.GetEntities().With<TileGridComponent>().AsSet();
        foreach (var grid in grids.GetEntities()) _bakeNow.Add(grid);
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        if (_bakeNow.Count > 0)
        {
            foreach (var grid in _bakeNow)
            {
                if (grid.IsAlive) Bake(grid);
                _quiet.Remove(grid);
            }
            _bakeNow.Clear();
        }

        if (_quiet.Count > 0)
        {
            _readyBuffer.Clear();
            foreach (var (grid, frames) in _quiet)
            {
                if (!grid.IsAlive) { _readyBuffer.Add(grid); continue; }
                if (frames + 1 >= QuietFrames) _readyBuffer.Add(grid);
                else _quiet[grid] = frames + 1;
            }
            foreach (var grid in _readyBuffer)
            {
                if (grid.IsAlive) Bake(grid);
                _quiet.Remove(grid);
            }
        }

        if (FocusBounds != null) StreamChunks();
    }

    /// <summary>Regenerates one grid's derived children (dispose + re-create). Public so tests can
    /// force a bake without the subscription plumbing. Colliders bake in full here; tile sprites
    /// bake in full too UNLESS <see cref="FocusBounds"/> is streaming them (then the first
    /// <see cref="Update"/> fills the view).</summary>
    public void Bake(Entity grid)
    {
        if (!grid.IsAlive || !grid.Has<TileGridComponent>()) return;

        DisposeProducts(grid);
        _streams.Remove(grid);

        var data = grid.Get<TileGridComponent>();
        if (data.Cells.Count == 0 || data.Values.Count == 0) { BakeCount++; return; }

        var cell = Math.Max(1f, data.CellSize);
        var stream = new GridStream();
        _streams[grid] = stream;

        foreach (var value in data.Values)
        {
            if (value.Id == 0) continue;

            // Colliders: greedy-merged rectangles (cell units → local units; the grid entity's
            // transform is the anchor, children are parented so local IS the layout). Never
            // streamed — see the class doc.
            if (value.ActiveLayers is { Length: > 0 })
            {
                var rects = TileGridBaking.MergeRectangles(data.Cells, value.Id);
                var logEach = rects.Count <= 64; // a big world bakes thousands: summarise instead
                for (var i = 0; i < rects.Count; i++)
                {
                    var r = rects[i];
                    var size = new Vector2(r.Width * cell, r.Height * cell);
                    var center = new Vector2(r.X * cell, r.Y * cell) + size / 2f;

                    var collider = _world.CreateEntity();
                    collider.Set(new BakedProductComponent());
                    collider.Set(new EntityInfoComponent(value.EntityType ?? value.Name, $"{value.Name}_{i:00}"));
                    collider.Set(new TransformComponent(center));
                    collider.Set(new BoxColliderComponent(size,
                        activeLayers: new HashSet<int>(value.ActiveLayers), passive: value.Passive));
                    _configureCollider?.Invoke(collider, value);
                    collider.SetParent(grid);
                    if (logEach)
                        Logger.Debug($"[level] TileGrid collider '{value.Name}_{i:00}': world " +
                                     $"{collider.Get<TransformComponent>().WorldPosition} size {size} " +
                                     $"layers [{string.Join(",", value.ActiveLayers)}] " +
                                     $"tagged={collider.Has<ColliderTagComponent>()}");
                }
                if (!logEach)
                    Logger.Debug($"[level] TileGrid '{value.Name}': {rects.Count} merged collider(s) " +
                                 $"on layers [{string.Join(",", value.ActiveLayers)}].");
            }

            // Tile visuals: cache the value's resolved sheet + parsed rules once; the per-chunk bake
            // below (or the immediate full bake) reads this table. A missing/unresolvable sheet logs
            // once and skips visuals (colliders above still bake — paint stays functional).
            if (!string.IsNullOrEmpty(value.TilesetKey))
            {
                var texture = ResolveSheet(value.TilesetKey!);
                if (texture == null) continue;
                stream.Skins[value.Id] = new ValueSkin(
                    value, texture, TileGridBaking.ParseRules(value.AutotileRules), Math.Max(1, value.TileSize));
            }
        }

        BakeCount++;

        if (FocusBounds == null)
        {
            // No streaming: every painted chunk bakes now (the pre-streaming contract).
            foreach (var key in ChunkKeysOf(data))
            {
                if (BatchChunks) BakeChunkBatched(grid, data, stream, key);
                else BakeChunk(grid, data, stream, key);
            }
            Logger.Debug($"[level] TileGrid baked: {data.Cells.Count} cell(s), {data.Values.Count} value(s), " +
                         $"{stream.Chunks.Count} chunk(s) (unstreamed).");
            return;
        }

        Logger.Debug($"[level] TileGrid baked: {data.Cells.Count} cell(s), {data.Values.Count} value(s); " +
                     "sprites stream per chunk around the view.");
    }

    /// <summary>Brings every live grid's resident chunk set in line with <see cref="FocusBounds"/>:
    /// evict what left the margin, bake what entered (budgeted per frame).</summary>
    private void StreamChunks()
    {
        var view = FocusBounds!();
        _deadGrids.Clear();

        foreach (var (grid, stream) in _streams)
        {
            if (!grid.IsAlive || !grid.Has<TileGridComponent>()) { _deadGrids.Add(grid); continue; }
            var data = grid.Get<TileGridComponent>();
            if (data.Cells.Count == 0) continue;

            var cell = Math.Max(1f, data.CellSize);
            var anchor = grid.Has<TransformComponent>()
                ? grid.Get<TransformComponent>().WorldPosition
                : Vector2.Zero;

            // World view (+ a one-chunk margin so terrain exists before it is on screen) → the grid's
            // chunk coordinates.
            var margin = ChunkCells * cell;
            var minX = ChunkFloor((view.Left - margin - anchor.X) / cell);
            var maxX = ChunkFloor((view.Right + margin - anchor.X) / cell);
            var minY = ChunkFloor((view.Top - margin - anchor.Y) / cell);
            var maxY = ChunkFloor((view.Bottom + margin - anchor.Y) / cell);

            _wanted.Clear();
            for (var cy = minY; cy <= maxY; cy++)
            for (var cx = minX; cx <= maxX; cx++)
                _wanted.Add(TileGridComponent.Pack(cx, cy));

            // Cap the resident set nearest-view-centre first (a zoomed-way-out view asks for more
            // than a frame can hold).
            if (_wanted.Count > MaxResidentChunks)
            {
                var centreX = (view.Left + view.Right) / 2f;
                var centreY = (view.Top + view.Bottom) / 2f;
                var focusX = (centreX - anchor.X) / cell / ChunkCells;
                var focusY = (centreY - anchor.Y) / cell / ChunkCells;
                _wanted.Sort((a, b) => ChunkDistanceSquared(a, focusX, focusY)
                    .CompareTo(ChunkDistanceSquared(b, focusX, focusY)));
                _wanted.RemoveRange(MaxResidentChunks, _wanted.Count - MaxResidentChunks);
                if (!_warnedChunkCap)
                {
                    _warnedChunkCap = true;
                    Logger.Info($"[level] TileGrid streaming hit the resident-chunk cap " +
                                $"({MaxResidentChunks} x {ChunkCells}-cell chunks) — terrain outside the " +
                                "nearest chunks is not baked at this zoom.");
                }
            }

            // Evict chunks that left the wanted set.
            _wantedSet.Clear();
            foreach (var key in _wanted) _wantedSet.Add(key);
            _evict.Clear();
            foreach (var (key, _) in stream.Chunks)
                if (!_wantedSet.Contains(key)) _evict.Add(key);
            foreach (var key in _evict) UnbakeChunk(stream, key);

            // Bake what is missing, newest-arrival budget per frame (bigger on a grid's first fill,
            // so a level arrives complete rather than visibly assembling).
            var budget = stream.Filled ? ChunksPerFrame : FirstFillChunks;
            foreach (var key in _wanted)
            {
                if (budget <= 0) break;
                if (stream.Chunks.ContainsKey(key)) continue;
                var baked = BatchChunks
                    ? BakeChunkBatched(grid, data, stream, key)
                    : BakeChunk(grid, data, stream, key);
                if (baked) budget--;
            }
            stream.Filled = true;
        }

        foreach (var dead in _deadGrids)
        {
            // A dead grid cascade-disposes its PARENTED products (the colliders); its unparented
            // tiles are ours to clean up.
            if (_streams.TryGetValue(dead, out var stream))
                foreach (var (_, tiles) in stream.Chunks)
                {
                    if (tiles == null) continue;
                    foreach (var tile in tiles)
                        if (tile.IsAlive) tile.Dispose();
                }
            _streams.Remove(dead);
        }
    }

    private static int ChunkFloor(float cellCoordinate) =>
        (int)MathF.Floor(cellCoordinate / ChunkCells);

    private static float ChunkDistanceSquared(long key, float focusX, float focusY)
    {
        var (cx, cy) = TileGridComponent.Unpack(key);
        var dx = cx - focusX;
        var dy = cy - focusY;
        return dx * dx + dy * dy;
    }

    /// <summary>The chunk keys a grid's painted cells actually occupy (the unstreamed full bake).</summary>
    private static IEnumerable<long> ChunkKeysOf(TileGridComponent data)
    {
        var seen = new HashSet<long>();
        foreach (var key in data.Cells.Keys)
        {
            var (x, y) = TileGridComponent.Unpack(key);
            var chunk = TileGridComponent.Pack(FloorDiv(x, ChunkCells), FloorDiv(y, ChunkCells));
            if (seen.Add(chunk)) yield return chunk;
        }
    }

    private static int FloorDiv(int value, int divisor) =>
        value >= 0 ? value / divisor : ~(~value / divisor);

    /// <summary>Bakes one chunk's tile sprites. Returns false when the chunk holds no visual cell
    /// (recorded as resident anyway, so it is not retried every frame).</summary>
    private bool BakeChunk(Entity grid, TileGridComponent data, GridStream stream, long chunkKey)
    {
        var (chunkX, chunkY) = TileGridComponent.Unpack(chunkKey);
        var x0 = chunkX * ChunkCells;
        var y0 = chunkY * ChunkCells;
        var cell = Math.Max(1f, data.CellSize);
        // Tiles are placed in WORLD space and left UNPARENTED (see the class doc): the grid anchor is
        // folded in here instead.
        var anchor = grid.Has<TransformComponent>() ? grid.Get<TransformComponent>().WorldPosition : Vector2.Zero;
        List<Entity>? tiles = null;

        for (var y = y0; y < y0 + ChunkCells; y++)
        for (var x = x0; x < x0 + ChunkCells; x++)
        {
            if (!data.Cells.TryGetValue(TileGridComponent.Pack(x, y), out var id) || id == 0) continue;
            if (!stream.Skins.TryGetValue(id, out var skin)) continue;

            var mask = TileGridBaking.NeighborMask(data.Cells, x, y, id);
            var source = TileGridBaking.PickTile(skin.Rules, mask, x, y, skin.TileSize);

            var tile = _world.CreateEntity();
            tile.Set(new BakedProductComponent());
            tile.Set(new TransformComponent(anchor + data.CellTopLeft(x, y)));
            tile.Set(new SpriteInfoComponent
            {
                SpriteSheet = skin.Texture,
                AssetKey = skin.Value.TilesetKey,
                Source = source,
                Size = new Vector2(cell, cell),
                Color = Color.White,
                Target = RenderTargetID.Main,
                LayerDepth = skin.Value.LayerDepth,
            });
            tile.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.Main });
            // Streamed tiles are visible BY CONSTRUCTION (the streamer only bakes what the view
            // covers), so they carry their own VisibleComponent and opt out of culling — thousands of
            // per-frame bounds tests for a set that is already view-bounded.
            tile.Set<VisibleComponent>();
            tile.Set<CullingExemptComponent>();
            (tiles ??= new List<Entity>()).Add(tile);
        }

        stream.Chunks[chunkKey] = tiles;
        return tiles != null;
    }

    /// <summary>
    /// Bakes one chunk as textured mesh entities — one per distinct (sheet, depth) group, so a chunk
    /// of rock plus a lava seam is two draws instead of 256 sprites. Vertices are world-space, so the
    /// mesh needs no transform and no parent; the chunk list owns its lifetime exactly as with sprites.
    /// </summary>
    private bool BakeChunkBatched(Entity grid, TileGridComponent data, GridStream stream, long chunkKey)
    {
        var (chunkX, chunkY) = TileGridComponent.Unpack(chunkKey);
        var x0 = chunkX * ChunkCells;
        var y0 = chunkY * ChunkCells;
        var cell = Math.Max(1f, data.CellSize);
        var anchor = grid.Has<TransformComponent>() ? grid.Get<TransformComponent>().WorldPosition : Vector2.Zero;

        foreach (var (_, reused) in _groups)
        {
            reused.Vertices.Clear();
            reused.Indices.Clear();
        }
        for (var y = y0; y < y0 + ChunkCells; y++)
        for (var x = x0; x < x0 + ChunkCells; x++)
        {
            if (!data.Cells.TryGetValue(TileGridComponent.Pack(x, y), out var id) || id == 0) continue;
            if (!stream.Skins.TryGetValue(id, out var skin)) continue;

            var mask = TileGridBaking.NeighborMask(data.Cells, x, y, id);
            var source = TileGridBaking.PickTile(skin.Rules, mask, x, y, skin.TileSize);
            var key = (skin.Texture, skin.Value.LayerDepth);
            if (!_groups.TryGetValue(key, out var group)) _groups[key] = group = new MeshGroup();

            // One quad: 4 vertices, 6 indices, positions in WORLD space, UVs from the source rect.
            var origin = anchor + data.CellTopLeft(x, y);
            var texture = skin.Texture;
            var u0 = source.Left / (float)texture.Width;
            var v0 = source.Top / (float)texture.Height;
            var u1 = source.Right / (float)texture.Width;
            var v1 = source.Bottom / (float)texture.Height;
            var baseIndex = group.Vertices.Count;

            group.Vertices.Add(new VertexPositionColorTexture(
                new Vector3(origin.X, origin.Y, 0f), Color.White, new Vector2(u0, v0)));
            group.Vertices.Add(new VertexPositionColorTexture(
                new Vector3(origin.X + cell, origin.Y, 0f), Color.White, new Vector2(u1, v0)));
            group.Vertices.Add(new VertexPositionColorTexture(
                new Vector3(origin.X + cell, origin.Y + cell, 0f), Color.White, new Vector2(u1, v1)));
            group.Vertices.Add(new VertexPositionColorTexture(
                new Vector3(origin.X, origin.Y + cell, 0f), Color.White, new Vector2(u0, v1)));

            group.Indices.Add(baseIndex);
            group.Indices.Add(baseIndex + 1);
            group.Indices.Add(baseIndex + 2);
            group.Indices.Add(baseIndex);
            group.Indices.Add(baseIndex + 2);
            group.Indices.Add(baseIndex + 3);
        }

        List<Entity>? meshes = null;
        foreach (var ((texture, depth), group) in _groups)
        {
            if (group.Vertices.Count == 0) continue;
            var mesh = _world.CreateEntity();
            mesh.Set(new BakedProductComponent());
            mesh.Set(new DrawComponent
            {
                Type = DrawElementType.Mesh,
                TexturedVertices = group.Vertices.ToArray(),
                Indices = group.Indices.ToArray(),
                PrimitiveType = PrimitiveType.TriangleList,
                Texture = texture,
                Target = RenderTargetID.Main,
                LayerDepth = depth,
            });
            mesh.Set<VisibleComponent>(); // the streamer owns visibility (no culling, no transform)
            (meshes ??= new List<Entity>()).Add(mesh);
        }

        stream.Chunks[chunkKey] = meshes;
        return meshes != null;
    }

    private static void UnbakeChunk(GridStream stream, long chunkKey)
    {
        if (!stream.Chunks.Remove(chunkKey, out var tiles) || tiles == null) return;
        foreach (var tile in tiles)
            if (tile.IsAlive) tile.Dispose();
    }

    private Texture2D? ResolveSheet(string key)
    {
        if (_textureBySheet.TryGetValue(key, out var cached)) return cached;
        var texture = _resolveTexture?.Invoke(key);
        if (texture == null && _warnedSheets.Add(key))
            Logger.Warning($"[level] TileGrid tileset '{key}' did not resolve — painting bakes colliders only.");
        _textureBySheet[key] = texture;
        return texture;
    }

    private void DisposeProducts(Entity grid)
    {
        // Unparented chunk tiles are owned by their chunk lists (they are not in the ChildOf sweep).
        if (_streams.TryGetValue(grid, out var stream))
        {
            foreach (var (_, tiles) in stream.Chunks)
            {
                if (tiles == null) continue;
                foreach (var tile in tiles)
                    if (tile.IsAlive) tile.Dispose();
            }
            stream.Chunks.Clear();
        }

        _disposeBuffer.Clear();
        foreach (var baked in _bakedSet.GetEntities())
            if (baked.IsAlive && baked.Get<ChildOfComponent>().Parent == grid)
                _disposeBuffer.Add(baked);
        foreach (var baked in _disposeBuffer)
            if (baked.IsAlive) baked.Dispose();
    }

    public void Dispose()
    {
        _addedSubscription.Dispose();
        _changedSubscription.Dispose();
        _bakedSet.Dispose();
        _streams.Clear();
    }

    /// <summary>One grid's streaming state: which chunks are live (null list = chunk has no visual
    /// cells) and the per-value skin table resolved at bake time.</summary>
    private sealed class GridStream
    {
        public readonly Dictionary<long, List<Entity>?> Chunks = new();
        public readonly Dictionary<byte, ValueSkin> Skins = new();

        /// <summary>Whether this grid has had its first (bigger) fill pass.</summary>
        public bool Filled;
    }

    /// <summary>One chunk mesh under construction (reused across chunks).</summary>
    private sealed class MeshGroup
    {
        public readonly List<VertexPositionColorTexture> Vertices = new();
        public readonly List<int> Indices = new();
    }

    /// <summary>A paint value's baked-visual inputs, resolved once per bake.</summary>
    private readonly struct ValueSkin(TilePaintValue value, Texture2D texture, Point[][] rules, int tileSize)
    {
        public readonly TilePaintValue Value = value;
        public readonly Texture2D Texture = texture;
        public readonly Point[][] Rules = rules;
        public readonly int TileSize = tileSize;
    }
}
