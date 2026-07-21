using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Extensions.Monogame;
using MonoDreams.Message;
using MonoDreams.State;

namespace MonoDreams.System.Collision;

/// <summary>
/// Collision detection system supporting both BoxColliderComponent (swept AABB) and ConvexColliderComponent (SAT).
/// Uses ColliderTagComponent marker component to query entities with either collider type.
/// Broadphase is a uniform spatial grid rebuilt each frame: colliders are bucketed by their
/// (movement-expanded) world AABB and only same-cell pairs reach the narrowphase, turning the
/// former all-pairs O(n²) sweep into roughly O(n) for evenly distributed colliders. The emitted
/// CollisionMessage set is unchanged — the grid only skips pairs that could never overlap.
/// Runs single-threaded because instance-level polygon buffers (_boxPolyBufA/_boxPolyBufB)
/// are not thread-safe.
/// </summary>
public class TransformCollisionDetectionSystem<TCollisionMessage> : ISystem<GameState>
    where TCollisionMessage : ICollisionMessage
{
    private readonly World _world;
    private readonly EntitySet _activeSet;
    private readonly CreateCollisionMessageDelegate<TCollisionMessage> _createCollisionMessage;

    // ─── broadphase grid (rebuilt each frame) ─────────────────────────────────
    // A uniform spatial grid replaces the old all-pairs (O(n²)) sweep: colliders
    // are bucketed by their world AABB and only same-cell pairs are tested. Cell
    // size adapts to the average collider AABB so small colliders occupy ~one cell
    // while the few large ones span several. Any positive cell size is correct;
    // this only balances cells-per-collider against colliders-per-cell.
    private const float MinCellSize = 8f;
    private const float CellSizeFactor = 2f;

    private readonly List<ColliderEntry> _entries = new();
    private readonly Dictionary<long, List<int>> _grid = new();
    private readonly List<List<int>> _cellPool = new();
    private readonly HashSet<long> _testedPairs = new();
    private int _cellsUsed;
    private float _cellSize;

    /// Per-frame snapshot of one collider ENTITY: its world AABB (expanded by its BODY's frame
    /// movement) plus the flags the pair loop needs, captured once so the hot loop never re-fetches
    /// components. <see cref="Body"/> is the resolved owning body (<see cref="ColliderBody"/>) — the
    /// message carries it and its <see cref="Delta"/> drives the swept test (a collider child's own
    /// local transform does not move; the body carries the world movement).
    private struct ColliderEntry
    {
        public Entity Entity;   // the collider entity
        public Entity Body;     // resolved owning body (message + swept delta)
        public IColliderComponent Collider;
        public bool HasBox;
        public bool Active;     // non-passive; only enabled colliders are recorded
        public Vector2 Min;
        public Vector2 Max;
        public Vector2 Delta;   // the body's movement this frame (swept + grid expansion)
    }

    public bool IsEnabled { get; set; } = true;

    public TransformCollisionDetectionSystem(
        World world,
        CreateCollisionMessageDelegate<TCollisionMessage> createCollisionMessage)
    {
        _world = world;
        _createCollisionMessage = createCollisionMessage;

        // Auto-tag entities when they get a collider component
        world.SubscribeEntityComponentAdded<BoxColliderComponent>(OnBoxColliderAdded);
        world.SubscribeEntityComponentAdded<ConvexColliderComponent>(OnConvexColliderAdded);

        // ColliderTagComponent unifies BoxColliderComponent and ConvexColliderComponent into a single query, but each
        // type has its own passive/enabled semantics — those are checked at runtime in the
        // collision loop via GetCollider() rather than at query level. This one set holds
        // every collider; the grid build classifies them into active/target roles per frame.
        _activeSet = world.GetEntities()
            .With<ColliderTagComponent>()
            .With<TransformComponent>()
            .AsSet();
    }

    private static void OnBoxColliderAdded(in Entity entity, in BoxColliderComponent _)
    {
        if (!entity.Has<ColliderTagComponent>()) entity.Set<ColliderTagComponent>();
    }

    private static void OnConvexColliderAdded(in Entity entity, in ConvexColliderComponent _)
    {
        if (!entity.Has<ColliderTagComponent>()) entity.Set<ColliderTagComponent>();
    }

    public void Update(GameState state)
    {
        BuildEntries();
        if (_entries.Count == 0) return;

        BuildGrid();
        TestCandidatePairs();
    }

    /// One O(n) pass over every collider: refresh convex world vertices, snapshot
    /// each enabled collider's world AABB (expanded by its <see cref="TransformComponent.Delta"/>
    /// so the swept box path can never be pruned), and accumulate the average extent
    /// used to size grid cells.
    private void BuildEntries()
    {
        _entries.Clear();
        var extentSum = 0f;

        foreach (var entity in _activeSet.GetEntities())
        {
            var transform = entity.Get<TransformComponent>();

            // Keep the old contract: refresh world vertices for ALL convex colliders,
            // including disabled ones (matches the pre-grid behavior).
            // TODO: static entities whose transforms never change could skip this with a dirty flag.
            if (entity.Has<ConvexColliderComponent>())
                entity.Get<ConvexColliderComponent>().UpdateWorldVertices(transform);

            var collider = GetCollider(entity);
            if (collider == null || !collider.Enabled) continue;

            var hasBox = entity.Has<BoxColliderComponent>();
            var aabb = hasBox
                ? SATCollision.BoxWorldRect(entity.Get<BoxColliderComponent>(), transform)
                : entity.Get<ConvexColliderComponent>().BroadPhaseAABB;

            // Movement for the swept path + grid expansion is the BODY's delta, not the collider's:
            // a collider child rides its parent, so its own local Delta is ~0 while the body carries
            // the frame's world movement. A standalone collider is its own body (delta == its own).
            var body = ColliderBody.Resolve(entity);
            var delta = body.Has<TransformComponent>() ? body.Get<TransformComponent>().Delta : Vector2.Zero;

            // Expand by this frame's movement so a fast mover shares a cell with
            // anything along its swept path (the box-vs-box narrowphase is swept).
            var min = aabb.Position;
            var max = aabb.Position + aabb.Size;
            if (delta.X >= 0f) max.X += delta.X; else min.X += delta.X;
            if (delta.Y >= 0f) max.Y += delta.Y; else min.Y += delta.Y;

            extentSum += (max.X - min.X) + (max.Y - min.Y);
            _entries.Add(new ColliderEntry
            {
                Entity = entity,
                Body = body,
                Collider = collider,
                HasBox = hasBox,
                Active = !collider.Passive,
                Min = min,
                Max = max,
                Delta = delta,
            });
        }

        // avg of (width + height) / 2 across recorded colliders.
        _cellSize = MathF.Max(MinCellSize, extentSum / (2f * _entries.Count) * CellSizeFactor);
    }

    /// Buckets every recorded collider into each grid cell its expanded AABB overlaps.
    /// Cell lists are pooled and reused across frames to avoid per-frame allocation.
    private void BuildGrid()
    {
        _grid.Clear();
        _cellsUsed = 0;
        var inv = 1f / _cellSize;

        for (var idx = 0; idx < _entries.Count; idx++)
        {
            var e = _entries[idx];
            var cx0 = (int)MathF.Floor(e.Min.X * inv);
            var cx1 = (int)MathF.Floor(e.Max.X * inv);
            var cy0 = (int)MathF.Floor(e.Min.Y * inv);
            var cy1 = (int)MathF.Floor(e.Max.Y * inv);

            for (var cx = cx0; cx <= cx1; cx++)
            for (var cy = cy0; cy <= cy1; cy++)
            {
                var key = ((long)cx << 32) | (uint)cy;
                if (!_grid.TryGetValue(key, out var cell))
                {
                    cell = RentCell();
                    _grid[key] = cell;
                }
                cell.Add(idx);
            }
        }
    }

    /// Tests ordered collider pairs that share a cell, deduped across multi-cell
    /// overlaps. Because two colliders with overlapping AABBs always share a cell,
    /// this reproduces the old all-pairs result minus pairs whose AABBs can't
    /// overlap (which produced no message anyway) — detection output is unchanged.
    private void TestCandidatePairs()
    {
        _testedPairs.Clear();

        foreach (var kv in _grid)
        {
            var members = kv.Value;
            for (var a = 0; a < members.Count; a++)
            for (var b = 0; b < members.Count; b++)
            {
                if (a == b) continue;
                var i = members[a];
                var j = members[b];

                var ea = _entries[i];
                if (!ea.Active) continue;                            // A initiates only if non-passive
                var eb = _entries[j];
                if (!eb.Collider.SharesLayerWith(ea.Collider)) continue;

                // Dedup on the ORDERED pair: (A,B) and (B,A) are intentionally kept
                // distinct (consumers may rely on both symmetric messages), but the
                // same ordered pair found in two shared cells is tested once.
                var key = ((long)i << 32) | (uint)j;
                if (!_testedPairs.Add(key)) continue;

                if (ea.HasBox && eb.HasBox)
                    TestBoxVsBox(ea, eb);
                else
                    TestSAT(ea, eb);
            }
        }
    }

    /// Rents a cleared, reusable cell list from the pool (grown on demand).
    private List<int> RentCell()
    {
        if (_cellsUsed == _cellPool.Count) _cellPool.Add(new List<int>());
        var cell = _cellPool[_cellsUsed++];
        cell.Clear();
        return cell;
    }

    private void TestBoxVsBox(in ColliderEntry a, in ColliderEntry b)
    {
        var boxA = a.Entity.Get<BoxColliderComponent>();
        var transformA = a.Entity.Get<TransformComponent>();
        var dynamicRect = SATCollision.BoxWorldRect(boxA, transformA);
        var displacement = a.Delta; // the body's movement (a collider child rides its parent)

        var boxB = b.Entity.Get<BoxColliderComponent>();
        var transformB = b.Entity.Get<TransformComponent>();
        var targetRect = SATCollision.BoxWorldRect(boxB, transformB);

        var collides = DynamicRectVsRect(
            dynamicRect, displacement, targetRect,
            out var contactPoint, out var contactNormal, out var contactTime);

        if (!collides || contactNormal == Vector2.Zero) return;

        foreach (var layer in b.Collider.SharedLayers(a.Collider))
        {
            _world.Publish(_createCollisionMessage(a.Entity, b.Entity, a.Body, b.Body, contactPoint, contactNormal, contactTime, 0f, layer));
        }
    }

    // Reusable buffers for box-to-polygon conversion (avoids stackalloc scope issues)
    private readonly Vector2[] _boxPolyBufA = new Vector2[4];
    private readonly Vector2[] _boxPolyBufB = new Vector2[4];

    private void TestSAT(in ColliderEntry a, in ColliderEntry b)
    {
        var hasBoxA = a.HasBox;
        var hasBoxB = b.HasBox;

        // Broad-phase AABB rejection first (cheap)
        var aabbA = hasBoxA
            ? SATCollision.BoxWorldRect(a.Entity.Get<BoxColliderComponent>(), a.Entity.Get<TransformComponent>())
            : a.Entity.Get<ConvexColliderComponent>().BroadPhaseAABB;
        var aabbB = hasBoxB
            ? SATCollision.BoxWorldRect(b.Entity.Get<BoxColliderComponent>(), b.Entity.Get<TransformComponent>())
            : b.Entity.Get<ConvexColliderComponent>().BroadPhaseAABB;

        if (!aabbA.Intersects(aabbB)) return;

        // Get world-space polygons for both collider entities
        Vector2[] polyA;
        Vector2[] polyB;

        if (hasBoxA)
        {
            SATCollision.BoxToPolygon(a.Entity.Get<BoxColliderComponent>(), a.Entity.Get<TransformComponent>(), _boxPolyBufA);
            polyA = _boxPolyBufA;
        }
        else
        {
            polyA = a.Entity.Get<ConvexColliderComponent>().WorldVertices;
        }

        if (hasBoxB)
        {
            SATCollision.BoxToPolygon(b.Entity.Get<BoxColliderComponent>(), b.Entity.Get<TransformComponent>(), _boxPolyBufB);
            polyB = _boxPolyBufB;
        }
        else
        {
            polyB = b.Entity.Get<ConvexColliderComponent>().WorldVertices;
        }

        if (!SATCollision.PolygonVsPolygon(polyA, polyB, out var contactNormal, out var penetrationDepth)) return;

        // Contact point: centroid-midpoint approximation (not an exact contact point for SAT)
        var contactPoint = (SATCollision.PolygonCenter(polyA) + SATCollision.PolygonCenter(polyB)) / 2f;

        foreach (var layer in b.Collider.SharedLayers(a.Collider))
        {
            _world.Publish(_createCollisionMessage(a.Entity, b.Entity, a.Body, b.Body, contactPoint, contactNormal, 0f, penetrationDepth, layer));
        }
    }

    private static bool RayVsRect(
        Vector2 rayOrigin, Vector2 rayDirection, CollisionRect target, out Vector2 contactPoint,
        out Vector2 contactNormal, out float closestHit)
    {
        closestHit = float.NaN;
        contactNormal = Vector2.Zero;
        contactPoint = Vector2.Zero;

        var inverseDirection = Vector2.One / rayDirection;
        var closestDistance = (target.Position - rayOrigin) * inverseDirection;
        var furthestDistance = (target.Position + target.Size - rayOrigin) * inverseDirection;

        if (float.IsNaN(furthestDistance.Y) || float.IsNaN(furthestDistance.X)) return false;
        if (float.IsNaN(closestDistance.Y) || float.IsNaN(closestDistance.X)) return false;

        if (closestDistance.X > furthestDistance.X) (closestDistance.X, furthestDistance.X) = (furthestDistance.X, closestDistance.X);
        if (closestDistance.Y > furthestDistance.Y) (closestDistance.Y, furthestDistance.Y) = (furthestDistance.Y, closestDistance.Y);

        if (closestDistance.X > furthestDistance.Y || closestDistance.Y > furthestDistance.X) return false;

        closestHit = Math.Max(closestDistance.X, closestDistance.Y);
        var furthestHit = Math.Min(furthestDistance.X, furthestDistance.Y);

        if (furthestHit < 0) return false;

        contactPoint = rayOrigin + closestHit * rayDirection;

        if (closestDistance.X > closestDistance.Y)
            contactNormal = inverseDirection.X < 0 ? new Vector2(1, 0) : new Vector2(-1, 0);
        else if (closestDistance.X < closestDistance.Y)
            contactNormal = inverseDirection.Y < 0 ? new Vector2(0, 1) : new Vector2(0, -1);

        return true;
    }

    public static bool DynamicRectVsRect(
        in CollisionRect dynamicRect, in Vector2 displacement, in CollisionRect staticRect,
        out Vector2 contactPoint, out Vector2 contactNormal, out float contactTime)
    {
        if (displacement is { X: 0, Y: 0 })
        {
            contactPoint = Vector2.Zero;
            contactNormal = Vector2.Zero;
            contactTime = 0;
            return dynamicRect.Intersects(staticRect);
        }

        var expandedTarget = new CollisionRect(
            staticRect.Position - dynamicRect.Size / 2,
            staticRect.Size + dynamicRect.Size);

        var potentialCollision = RayVsRect(
            dynamicRect.Center, displacement, expandedTarget, out contactPoint,
            out contactNormal, out contactTime);

        return potentialCollision && contactTime < 1.0f;
    }

    private static IColliderComponent GetCollider(Entity entity)
    {
        if (entity.Has<BoxColliderComponent>()) return entity.Get<BoxColliderComponent>();
        if (entity.Has<ConvexColliderComponent>()) return entity.Get<ConvexColliderComponent>();
        return null;
    }

    public void Dispose()
    {
        _activeSet?.Dispose();
    }
}
