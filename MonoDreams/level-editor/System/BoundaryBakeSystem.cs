#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.LevelEditor.Boundary;
using MonoDreams.LevelEditor.Component;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// Bakes a <see cref="BoundaryComponent"/> polyline (island-authoring plan §5.2) into collision:
/// one <b>thin convex quad segment collider per polyline edge</b>, as <c>ChildOf</c> children of
/// the boundary entity. A coastline is deeply concave and the engine's SAT is convex-only, so a
/// segment chain is the standard robust answer (each quad is convex).
///
/// <para><b>Event-driven, never per-frame.</b> Following the wave-repass §S2 bake shape, this system
/// subscribes to the boundary component being <b>added</b> (the boundary tool's commit, and a scene
/// load re-setting the component) and <b>changed</b> (a vertex drag / add / delete, and undo/redo,
/// all through <c>entity.Set(new BoundaryComponent(...))</c>), enqueueing the affected entity. It
/// bakes only when draining that queue in <see cref="Update"/> — an empty queue is a no-op, so
/// nothing evaluates a boundary in a normal frame. Deferring the bake to <see cref="Update"/> also
/// sidesteps the scene reader's component-set ordering: by the time the queue drains, the boundary
/// entity is fully constructed (its <c>TransformComponent</c> is set and the parent graph is wired).</para>
///
/// <para><b>Whole-boundary move re-bake (Slice 4).</b> The gizmo moves a boundary by mutating its
/// <c>TransformComponent</c> fields directly — which fires no component-changed event — so
/// <see cref="Update"/> also polls each boundary's world position every frame and enqueues a re-bake
/// when it drifts from the position it was last baked at. Without this, a moved coastline's segment
/// colliders (root-level, positioned by the copied world position) would stay at the old spot while
/// the outline + proxies followed the transform. The poll is O(#boundaries) — few and long-lived —
/// and re-bakes only on an actual move.</para>
///
/// <para><b>Bake products never serialize.</b> Every segment child carries
/// <see cref="BakedProductComponent"/>; <c>SceneWriter</c> excludes it from the membership closure
/// even inside the boundary root's <c>ChildOf</c> descendant set (the polyline is the durable truth;
/// the children regenerate on load). Re-baking first disposes the boundary's existing segment
/// children, so an edit never accumulates stale segments.</para>
///
/// <para><b>Root-level collision.</b> <c>ConvexColliderComponent.UpdateWorldVertices</c> uses the
/// entity's LOCAL <c>Position</c> (the documented root-level-only limitation), so each segment child
/// gets the boundary's world position copied onto its own <c>TransformComponent.Position</c> and its
/// <c>ModelVertices</c> in the boundary's local frame — the segment then resolves to the correct
/// world quad regardless of the (harmless) <c>ChildOf</c> transform parenting the hierarchy applies.</para>
///
/// <para><b>Runs in both run modes</b> (<c>RunNormally</c>, no Edit guard): a shipped game loading a
/// native scene with a boundary must bake it too — the bake is a scene-loading participant, not
/// Edit-only tooling. Segments are <b>passive</b> static world geometry (the WallEntityFactory
/// idiom): they never initiate a collision (so resolution never moves them), but an active body is
/// resolved out of them — they BLOCK while staying put.</para>
/// </summary>
public sealed class BoundaryBakeSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly EntitySet _bakedSet;
    private readonly EntitySet _boundarySet;
    private readonly IDisposable _addedSubscription;
    private readonly IDisposable _changedSubscription;

    // Entities whose boundary changed since the last drain (deduped). Baked on the next Update.
    private readonly HashSet<Entity> _pending = new();
    private readonly List<Entity> _drainBuffer = new();
    private readonly List<Entity> _disposeBuffer = new();

    // The world position each boundary was last baked at — polled every frame so a whole-boundary
    // MOVE (the gizmo mutates TransformComponent fields directly, which fires no component-changed
    // event) re-bakes the segments at the new position (island-authoring Slice 4).
    private readonly Dictionary<Entity, Microsoft.Xna.Framework.Vector2> _bakedWorldPosition = new();
    private readonly List<Entity> _pruneBuffer = new();

    public bool IsEnabled { get; set; } = true;

    /// <summary>The number of bake passes run so far (added + changed drains) — observability for
    /// tests / logging.</summary>
    public int BakeCount { get; private set; }

    public BoundaryBakeSystem(World world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _bakedSet = world.GetEntities()
            .With<BakedProductComponent>().With<ChildOfComponent>().AsSet();
        _boundarySet = world.GetEntities()
            .With<BoundaryComponent>().With<TransformComponent>().AsSet();
        _addedSubscription = world.SubscribeEntityComponentAdded<BoundaryComponent>(OnAddedOrChanged);
        _changedSubscription = world.SubscribeEntityComponentChanged<BoundaryComponent>(OnChanged);
    }

    private void OnAddedOrChanged(in Entity entity, in BoundaryComponent _)
    {
        if (entity.IsAlive) _pending.Add(entity);
    }

    private void OnChanged(in Entity entity, in BoundaryComponent _, in BoundaryComponent __)
    {
        if (entity.IsAlive) _pending.Add(entity);
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        // Whole-boundary MOVE re-bake (Slice 4): the gizmo moves a boundary by mutating its
        // TransformComponent fields directly (no component-changed event fires), so poll the world
        // position and enqueue a re-bake when it drifts — a moved coastline blocks at its new spot.
        EnqueueMovedBoundaries();

        if (_pending.Count == 0) return;

        _drainBuffer.Clear();
        _drainBuffer.AddRange(_pending);
        _pending.Clear();

        foreach (var entity in _drainBuffer)
            if (entity.IsAlive && entity.Has<BoundaryComponent>())
                Bake(entity);
    }

    private void EnqueueMovedBoundaries()
    {
        foreach (var boundary in _boundarySet.GetEntities())
        {
            var world = boundary.Get<TransformComponent>().Position;
            // Only re-bake on an actual move; a not-yet-baked boundary is the added-event's job.
            if (_bakedWorldPosition.TryGetValue(boundary, out var last) && last != world)
                _pending.Add(boundary);
        }

        // Forget positions of disposed boundaries (rare — boundaries are long-lived).
        if (_bakedWorldPosition.Count == 0) return;
        _pruneBuffer.Clear();
        foreach (var key in _bakedWorldPosition.Keys)
            if (!key.IsAlive) _pruneBuffer.Add(key);
        foreach (var dead in _pruneBuffer) _bakedWorldPosition.Remove(dead);
    }

    /// <summary>Regenerates the segment colliders for one boundary: dispose its existing baked
    /// children, then create one non-passive convex quad collider per polyline edge. Public so a
    /// test can force a bake without wiring the subscription plumbing.</summary>
    public void Bake(Entity boundary)
    {
        if (!boundary.IsAlive || !boundary.Has<BoundaryComponent>()) return;

        DisposeSegments(boundary);

        var component = boundary.Get<BoundaryComponent>();
        if (component.Points == null || component.Points.Length < BoundaryGeometry.MinPoints) return;

        // Segment quads are in the boundary's LOCAL frame; copy the boundary's world position onto
        // each child so root-level collision (which uses the local Position field) places them right.
        var worldPosition = boundary.Has<TransformComponent>()
            ? boundary.Get<TransformComponent>().Position
            : Microsoft.Xna.Framework.Vector2.Zero;
        _bakedWorldPosition[boundary] = worldPosition; // the move-poll baseline (Slice 4)

        var quads = BoundaryGeometry.EdgeQuads(component.Points, component.Thickness);
        foreach (var quad in quads)
        {
            var segment = _world.CreateEntity();
            segment.Set(new BakedProductComponent()); // never serialized; excluded from membership
            segment.Set(new TransformComponent(worldPosition));
            // Passive = static world geometry (the WallEntityFactory idiom): a passive collider
            // never initiates a collision (so it is never moved by resolution — the player is the
            // non-passive mover), but the active player IS resolved out of it, so it BLOCKS while
            // staying put. All layers; the collider clones the quad internally.
            segment.Set(new ConvexColliderComponent(quad, passive: true));
            // ChildOf for lifecycle (DisposeOrphans cleans them with the boundary) + membership
            // grouping. The transform-parent sync HierarchySystem applies is harmless: collision
            // uses the local Position field (root-level), which already holds the world position.
            segment.Set(new ChildOfComponent(boundary));
        }

        BakeCount++;
    }

    private void DisposeSegments(Entity boundary)
    {
        _disposeBuffer.Clear();
        foreach (var baked in _bakedSet.GetEntities())
            if (baked.IsAlive && baked.Get<ChildOfComponent>().Parent == boundary)
                _disposeBuffer.Add(baked);
        foreach (var baked in _disposeBuffer)
            if (baked.IsAlive) baked.Dispose();
    }

    public void Dispose()
    {
        _addedSubscription.Dispose();
        _changedSubscription.Dispose();
        _bakedSet.Dispose();
        _boundarySet.Dispose();
        GC.SuppressFinalize(this);
    }
}
