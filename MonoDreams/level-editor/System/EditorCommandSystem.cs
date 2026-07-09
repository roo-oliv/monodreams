#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Draw;
using MonoDreams.LevelEditor.Boundary;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Proxy;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.Undo;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.System;

/// <summary>
/// The applying system for the editor command/undo machinery: in <see cref="RunMode.Edit"/> it
/// translates the designer's intent — the within-band ordering nudges (bring forward / send back,
/// optionally on PageUp/PageDown), plus the toolbar/menu <b>selection-edit actions</b> (add/remove
/// collider, add vertex) and the public <see cref="DeleteSelection"/> / <see cref="AddEmptyEntity"/> —
/// into operations on the <see cref="EditorHistory"/>. ECS purity: the commands are data +
/// apply/revert (see <see cref="IEditorCommand"/>); this system only sequences them — it holds no
/// mutation logic of its own.
///
/// <para><b>Delete / undo / redo are keyboard-driven by <c>EditorShortcutSystem</c> (UX3-E), not here.</b>
/// The editor keyboard bindings were consolidated into the ONE <c>EditorShortcuts</c> table, which calls
/// <see cref="DeleteSelection"/> and the shared <c>EditorHistory.Undo/Redo</c> directly — so this system
/// no longer reads delete/undo/redo input edges. Its <see cref="Update"/> handles only the optional
/// order-nudge predicates; every other action is a public method the shortcut table / toolbar / context
/// menu / headless ops call.</para>
///
/// <para><b>Within-band ordering (plan §4.2).</b> <see cref="BringForward"/>/<see cref="SendBack"/>
/// nudge the selection's SOURCE sort fields by <see cref="OrderStep"/>, clamped inside the band
/// the screen-supplied <see cref="DrawLayerMap"/> resolves (<c>TryGetBandRange</c>): a plain band
/// nudges <c>SpriteInfo.LayerDepth</c>; a <b>Y-sorted</b> band nudges <c>YSortDepthBias</c> and
/// NEVER <c>LayerDepth</c> — Y-sort participation is an exact-match lookup on the band value
/// (<c>TryGetYSortRange</c>), so a nudged depth would silently drop the sprite out of Y-sorting.
/// One click = one <see cref="SpriteSortEditCommand"/> = one undo step; a nudge already at the
/// band edge pushes nothing.</para>
///
/// <para><b>Collider authoring (plan §5.1).</b> <see cref="AddBoxCollider"/> applies the
/// footprint default (<see cref="ColliderDefaults.FootprintBounds"/> — full sprite width × the
/// bottom quarter, feet-anchored), <see cref="AddConvexCollider"/> a hexagon inscribed in that
/// footprint; <see cref="RemoveCollider"/> removes the selected proxy's bound collider (or every
/// collider when the selection is the entity itself, as one composite undo entry) — all through
/// snapshotting <see cref="ColliderComponentCommand"/>s, so undo restores the removed component
/// field-for-field. <see cref="AddVertex"/> inserts an edge midpoint (after the selected vertex,
/// or into the longest edge) — collinear by construction, so always convex-legal.</para>
///
/// <para><b>Delete is proxy-aware.</b> When the selection is a collider proxy, Delete must NOT
/// dispose the transient proxy entity (an un-undoable no-op that would just despawn the family):
/// it retargets — a <see cref="ProxyBindingKind.ConvexVertex"/> proxy deletes THAT vertex
/// (guarded: a convex collider keeps ≥ 3 vertices), a whole-shape proxy removes its bound
/// collider component. Afterwards the selection moves to the surviving family anchor (the shape
/// proxy for a vertex delete, else the owner) so the editing session continues. Only a plain
/// entity selection deletes the entity (the Wave-4a snapshotting
/// <see cref="DeleteEntityCommand"/>).</para>
///
/// <para><b>Edit-guarded, registered <see cref="MonoDreams.System.EditTimeBehavior.RunNormally"/></b>
/// (entry <c>editor.commands</c>): inert in Play — the public action methods are also called from
/// the toolbar/headless dispatch, so each guards itself loudly. The transform-edit and create
/// commands are pushed by other paths (the gizmo's coalescing API, the palette's
/// <see cref="CreateEntityCommand"/>).</para>
/// </summary>
public sealed class EditorCommandSystem : ISystem<GameState>
{
    /// <summary>The within-band ordering quantum: how much one Bring forward / Send back click
    /// nudges the SOURCE <c>LayerDepth</c> (plain band) or <c>YSortDepthBias</c> (Y-sorted band).
    /// Small relative to any band width; repeated clicks walk to the clamped band edge.</summary>
    public const float OrderStep = 0.001f;

    private readonly World _world;
    private readonly EditorHistory _history;
    private readonly SceneSerializer _serializer;
    private readonly DrawLayerMap? _layers;
    private readonly Camera? _camera;
    private readonly EntitySet _selectedSet;
    private readonly EntitySet _proxySet;
    private readonly Func<GameState, bool>? _orderForwardRequested;
    private readonly Func<GameState, bool>? _orderBackRequested;

    public bool IsEnabled { get; set; } = true;

    /// <param name="layers">The screen's <see cref="DrawLayerMap"/> — the band edges the ordering
    /// actions clamp inside. Null (a composition without layers) makes the ordering actions loud
    /// no-ops.</param>
    /// <param name="orderForwardRequested">Optional keyboard nudge (e.g. PageUp) for
    /// <see cref="BringForward"/>. Delete/undo/redo are NOT here — they are keyboard-driven by
    /// <c>EditorShortcutSystem</c> (UX3-E), which calls <see cref="DeleteSelection"/> and the shared
    /// history directly.</param>
    /// <param name="orderBackRequested">Optional keyboard nudge (e.g. PageDown) for
    /// <see cref="SendBack"/>.</param>
    /// <param name="camera">The editor view camera — <see cref="AddEmptyEntity"/> positions a new empty
    /// entity at the current view centre (<c>Camera.Position</c>). Null (a composition without a camera,
    /// or a unit test) falls back to the world origin.</param>
    public EditorCommandSystem(
        World world,
        EditorHistory history,
        SceneSerializer serializer,
        DrawLayerMap? layers = null,
        Func<GameState, bool>? orderForwardRequested = null,
        Func<GameState, bool>? orderBackRequested = null,
        Camera? camera = null)
    {
        _world = world;
        _history = history;
        _serializer = serializer;
        _layers = layers;
        _camera = camera;
        _selectedSet = world.GetEntities().With<SelectedComponent>().AsSet();
        _proxySet = world.GetEntities().With<GizmoProxyComponent>().AsSet();
        _orderForwardRequested = orderForwardRequested;
        _orderBackRequested = orderBackRequested;
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;
        if (state.RunMode != RunMode.Edit) return; // Edit-guarded: inert in Play

        // Delete / undo / redo are driven by EditorShortcutSystem (the consolidated shortcut table).
        // This system's per-frame input surface is only the optional order-nudge predicates.
        if (_orderForwardRequested?.Invoke(state) == true) BringForward(state);
        if (_orderBackRequested?.Invoke(state) == true) SendBack(state);
    }

    // ---- Delete (proxy-aware) ----

    /// <summary>Deletes what the selection MEANS (see the class doc): a vertex proxy deletes its
    /// vertex, a shape proxy removes its collider, a plain entity is deleted whole (snapshotting
    /// sub-graph command). Public so the headless <c>collider:deleteVertex</c> op and the Delete
    /// key share one path.</summary>
    public void DeleteSelection(GameState state)
    {
        if (!GuardEdit(state, "Delete")) return;
        if (!TryGetSelected(out var selected)) return;

        // The camera rig (UX2-E) is not deletable — it is editor-materialized from scene.camera on every
        // load, never scene content. Refuse loudly rather than tearing down the authored-camera entity.
        if (selected.Has<CameraRigComponent>())
        {
            Logger.Warning(
                "[level-editor] Delete refused: the camera rig is not deletable — it is materialized " +
                "from scene.camera on every load. Move it, or edit the camera through the Inspector.");
            return;
        }

        // Instance-children guardrail (PF-D): a prefab-owned CHILD (or a proxy bound to one) is not
        // deletable in a scene. The instance ROOT stays deletable (it carries the marker, so it is NOT
        // "owned" — deleting it removes the whole instance, snapshot-undoable as any delete).
        var deleteTarget = selected.Has<GizmoProxyComponent>() ? selected.Get<GizmoProxyComponent>().Target : selected;
        if (PrefabGuards.IsPrefabOwned(deleteTarget))
        {
            Logger.Warning(PrefabGuards.Refusal("Delete"));
            return;
        }

        if (selected.Has<GizmoProxyComponent>())
        {
            var binding = selected.Get<GizmoProxyComponent>();
            var owner = binding.Target;
            if (!owner.IsAlive) return;

            switch (binding.Kind)
            {
                case ProxyBindingKind.ConvexVertex:
                    DeleteVertex(selected, owner, binding.Index);
                    return;
                case ProxyBindingKind.BoundaryVertex:
                    DeleteBoundaryVertex(selected, owner, binding.Index);
                    return;
                case ProxyBindingKind.BoxColliderBounds:
                    if (!owner.Has<BoxColliderComponent>()) return;
                    _history.Push(ColliderComponentCommand.RemoveBox(owner));
                    Reselect(selected, owner);
                    return;
                case ProxyBindingKind.ConvexColliderShape:
                    if (!owner.Has<ConvexColliderComponent>()) return;
                    _history.Push(ColliderComponentCommand.RemoveConvex(owner));
                    Reselect(selected, owner);
                    return;
            }
        }

        _history.Push(new DeleteEntityCommand(_world, selected, _serializer));
    }

    private void DeleteVertex(Entity vertexProxy, Entity owner, int index)
    {
        if (!owner.Has<ConvexColliderComponent>()) return;
        var vertices = owner.Get<ConvexColliderComponent>().ModelVertices;
        if (vertices == null || index < 0 || index >= vertices.Length) return;
        if (vertices.Length <= 3)
        {
            Logger.Warning(
                "[level-editor] Delete vertex refused: a convex collider keeps at least 3 " +
                "vertices. Remove the collider instead.");
            return;
        }

        var after = new Vector2[vertices.Length - 1];
        for (int i = 0, j = 0; i < vertices.Length; i++)
            if (i != index)
                after[j++] = vertices[i];
        _history.Push(ColliderEditCommand.ForConvex(owner, after));

        // The deleted vertex's proxy despawns next sync — move the selection to the shape proxy
        // (vertex handles stay up) so the editing session continues, else to the owner.
        var shapeProxy = FindProxy(owner, ProxyBindingKind.ConvexColliderShape);
        Reselect(vertexProxy, shapeProxy.IsAlive ? shapeProxy : owner);
    }

    private void DeleteBoundaryVertex(Entity vertexProxy, Entity owner, int index)
    {
        if (!owner.Has<BoundaryComponent>()) return;
        var points = owner.Get<BoundaryComponent>().Points;
        if (points == null || index < 0 || index >= points.Length) return;
        if (points.Length <= BoundaryGeometry.MinPoints)
        {
            Logger.Warning(
                $"[level-editor] Delete boundary vertex refused: a boundary keeps at least " +
                $"{BoundaryGeometry.MinPoints} points. Delete the boundary instead.");
            return;
        }

        var after = new Vector2[points.Length - 1];
        for (int i = 0, j = 0; i < points.Length; i++)
            if (i != index)
                after[j++] = points[i];
        // BoundaryEditCommand re-fires the bake; the vertex proxy despawns next sync — reselect the
        // boundary so its (resized) vertex handles stay up and the session continues.
        _history.Push(BoundaryEditCommand.For(owner, after));
        Reselect(vertexProxy, owner);
    }

    // ---- Within-band ordering (plan §4.2) ----

    /// <summary>Nudges the selection one <see cref="OrderStep"/> toward the FRONT of its band.</summary>
    public void BringForward(GameState state) => NudgeOrder(state, +1);

    /// <summary>Nudges the selection one <see cref="OrderStep"/> toward the BACK of its band.</summary>
    public void SendBack(GameState state) => NudgeOrder(state, -1);

    private void NudgeOrder(GameState state, int direction)
    {
        const string action = "Bring forward / send back";
        if (!GuardEdit(state, action)) return;
        if (!TryGetSelectionOwner(action, out _, out var owner)) return;
        if (_layers == null)
        {
            Logger.Warning($"[level-editor] {action}: this composition supplies no DrawLayerMap.");
            return;
        }
        if (!owner.Has<SpriteInfoComponent>())
        {
            Logger.Warning($"[level-editor] {action}: the selection has no sprite to order.");
            return;
        }

        ref var sprite = ref owner.Get<SpriteInfoComponent>();
        if (!_layers.TryGetBandRange(sprite.LayerDepth, out _, out var min, out var max, out var ySorted))
        {
            Logger.Warning(
                $"[level-editor] {action}: LayerDepth {sprite.LayerDepth:0.####} falls in no " +
                "layer band of the supplied DrawLayerMap.");
            return;
        }

        if (ySorted)
        {
            // NEVER nudge LayerDepth on a Y-sorted band: Y-sort participation is an exact-match
            // lookup on the band value. The bias is the designed-for deterministic front/back
            // knob, applied after the Y interpolation; ±(band width) pins fully front/back.
            var range = max - min;
            var after = Math.Clamp(sprite.YSortDepthBias + direction * OrderStep, -range, range);
            if (after == sprite.YSortDepthBias) return; // already pinned at the edge
            _history.Push(new SpriteSortEditCommand(
                owner, sprite.LayerDepth, sprite.LayerDepth, sprite.YSortDepthBias, after));
        }
        else
        {
            var after = Math.Clamp(sprite.LayerDepth + direction * OrderStep, min, max);
            if (after == sprite.LayerDepth) return; // already clamped at the band edge
            _history.Push(new SpriteSortEditCommand(
                owner, sprite.LayerDepth, after, sprite.YSortDepthBias, sprite.YSortDepthBias));
        }
    }

    // ---- Collider add / remove (plan §5.1) ----

    /// <summary>Adds the footprint-default box collider to the selection's owner (loud no-op if
    /// one exists).</summary>
    public void AddBoxCollider(GameState state)
    {
        const string action = "Add box collider";
        if (!GuardEdit(state, action)) return;
        if (!TryGetSelectionOwner(action, out _, out var owner)) return;
        if (owner.Has<BoxColliderComponent>())
        {
            Logger.Warning($"[level-editor] {action}: the selection already has a box collider.");
            return;
        }

        var bounds = owner.Has<SpriteInfoComponent>()
            ? ColliderDefaults.FootprintBounds(owner.Get<SpriteInfoComponent>())
            : ColliderDefaults.FallbackFootprint;
        // Footprints are passive static blockers (see ColliderDefaults.FootprintPassive): a static
        // prop must block the player without being pushed by collision resolution.
        _history.Push(ColliderComponentCommand.AddBox(owner, bounds, ColliderDefaults.FootprintPassive));
    }

    /// <summary>Adds the default polygon collider (a footprint-inscribed hexagon) to the
    /// selection's owner (loud no-op if a convex collider exists).</summary>
    public void AddConvexCollider(GameState state)
    {
        const string action = "Add polygon collider";
        if (!GuardEdit(state, action)) return;
        if (!TryGetSelectionOwner(action, out _, out var owner)) return;
        if (owner.Has<ConvexColliderComponent>())
        {
            Logger.Warning($"[level-editor] {action}: the selection already has a polygon collider.");
            return;
        }

        var hexagon = owner.Has<SpriteInfoComponent>()
            ? ColliderDefaults.FootprintHexagon(owner.Get<SpriteInfoComponent>())
            : ColliderDefaults.FallbackHexagon();
        // Footprints are passive static blockers (see ColliderDefaults.FootprintPassive).
        _history.Push(ColliderComponentCommand.AddConvex(owner, hexagon, ColliderDefaults.FootprintPassive));
    }

    /// <summary>Removes the selected proxy's bound collider, or — when the selection is the
    /// entity itself — every collider it carries (one composite undo entry). Loud no-op when
    /// there is nothing to remove.</summary>
    public void RemoveCollider(GameState state)
    {
        const string action = "Remove collider";
        if (!GuardEdit(state, action)) return;
        if (!TryGetSelected(out var selected))
        {
            Logger.Warning($"[level-editor] {action}: nothing is selected.");
            return;
        }

        // Instance-children guardrail (PF-D): refuse a collider edit on a prefab-owned child (the ROOT
        // stays editable — component edits on it become overrides via the diff).
        var colliderTarget = selected.Has<GizmoProxyComponent>() ? selected.Get<GizmoProxyComponent>().Target : selected;
        if (PrefabGuards.IsPrefabOwned(colliderTarget))
        {
            Logger.Warning(PrefabGuards.Refusal(action));
            return;
        }

        if (selected.Has<GizmoProxyComponent>())
        {
            var binding = selected.Get<GizmoProxyComponent>();
            var owner = binding.Target;
            if (!owner.IsAlive) return;
            switch (binding.Kind)
            {
                case ProxyBindingKind.BoxColliderBounds when owner.Has<BoxColliderComponent>():
                    _history.Push(ColliderComponentCommand.RemoveBox(owner));
                    break;
                case ProxyBindingKind.ConvexColliderShape when owner.Has<ConvexColliderComponent>():
                case ProxyBindingKind.ConvexVertex when owner.Has<ConvexColliderComponent>():
                    _history.Push(ColliderComponentCommand.RemoveConvex(owner));
                    break;
                default:
                    return;
            }
            Reselect(selected, owner);
            return;
        }

        var hasBox = selected.Has<BoxColliderComponent>();
        var hasConvex = selected.Has<ConvexColliderComponent>();
        if (!hasBox && !hasConvex)
        {
            Logger.Warning($"[level-editor] {action}: the selection has no collider.");
            return;
        }

        if (hasBox && hasConvex)
        {
            // One undo entry for both removals (snapshot first, then push the composite once —
            // Push applies it).
            _history.Push(new CompositeCommand(new List<IEditorCommand>
            {
                ColliderComponentCommand.RemoveBox(selected),
                ColliderComponentCommand.RemoveConvex(selected),
            }));
        }
        else if (hasBox)
        {
            _history.Push(ColliderComponentCommand.RemoveBox(selected));
        }
        else
        {
            _history.Push(ColliderComponentCommand.RemoveConvex(selected));
        }
    }

    /// <summary>Inserts a vertex into the selection's convex collider: the midpoint of the edge
    /// AFTER the selected vertex proxy, or of the longest edge when the shape (or the entity) is
    /// selected. A midpoint is collinear with its edge, so the result is always convex-legal;
    /// dragging it outward gives it shape. One undoable step.</summary>
    public void AddVertex(GameState state)
    {
        const string action = "Add vertex";
        if (!GuardEdit(state, action)) return;
        if (!TryGetSelectionOwner(action, out var selected, out var owner)) return;

        // A boundary is an open polyline (island-authoring §5.2): insert a midpoint into its Points
        // instead of the convex ModelVertices.
        if (owner.Has<BoundaryComponent>())
        {
            AddBoundaryVertex(action, selected, owner);
            return;
        }

        if (!owner.Has<ConvexColliderComponent>())
        {
            Logger.Warning($"[level-editor] {action}: the selection has no polygon collider.");
            return;
        }

        var vertices = owner.Get<ConvexColliderComponent>().ModelVertices;
        if (vertices == null || vertices.Length < 3) return;

        // The edge to split: after the selected vertex, else the longest (room to work in).
        var edgeStart = -1;
        if (selected.Has<GizmoProxyComponent>())
        {
            var binding = selected.Get<GizmoProxyComponent>();
            if (binding.Kind == ProxyBindingKind.ConvexVertex
                && binding.Index >= 0 && binding.Index < vertices.Length)
                edgeStart = binding.Index;
        }
        if (edgeStart < 0)
        {
            var longest = -1f;
            for (var i = 0; i < vertices.Length; i++)
            {
                var lengthSq = Vector2.DistanceSquared(vertices[i], vertices[(i + 1) % vertices.Length]);
                if (lengthSq > longest)
                {
                    longest = lengthSq;
                    edgeStart = i;
                }
            }
        }

        var midpoint = (vertices[edgeStart] + vertices[(edgeStart + 1) % vertices.Length]) / 2f;
        var after = new Vector2[vertices.Length + 1];
        for (int i = 0, j = 0; i < vertices.Length; i++)
        {
            after[j++] = vertices[i];
            if (i == edgeStart) after[j++] = midpoint;
        }
        _history.Push(ColliderEditCommand.ForConvex(owner, after));
    }

    /// <summary>Inserts a point into the selection's boundary polyline: the midpoint of the edge
    /// AFTER the selected vertex proxy, or of the longest edge otherwise. One undoable step
    /// (re-fires the bake).</summary>
    private void AddBoundaryVertex(string action, Entity selected, Entity owner)
    {
        var points = owner.Get<BoundaryComponent>().Points;
        if (points == null || points.Length < BoundaryGeometry.MinPoints) return;

        var edgeStart = -1;
        if (selected.Has<GizmoProxyComponent>())
        {
            var binding = selected.Get<GizmoProxyComponent>();
            if (binding.Kind == ProxyBindingKind.BoundaryVertex
                && binding.Index >= 0 && binding.Index < points.Length - 1)
                edgeStart = binding.Index;
        }
        if (edgeStart < 0)
        {
            var longest = -1f;
            for (var i = 0; i < points.Length - 1; i++) // open polyline: no closing edge
            {
                var lengthSq = Vector2.DistanceSquared(points[i], points[i + 1]);
                if (lengthSq > longest) { longest = lengthSq; edgeStart = i; }
            }
        }
        if (edgeStart < 0) return;

        var midpoint = (points[edgeStart] + points[edgeStart + 1]) / 2f;
        var after = new Vector2[points.Length + 1];
        for (int i = 0, j = 0; i < points.Length; i++)
        {
            after[j++] = points[i];
            if (i == edgeStart) after[j++] = midpoint;
        }
        _history.Push(BoundaryEditCommand.For(owner, after));
    }

    // ---- Add empty entity (UX2-D §4, Entities-panel context menu) ----

    /// <summary>Creates a new empty save-root entity at the current view centre (<c>Camera.Position</c>,
    /// else the world origin): a <c>TransformComponent</c> + <c>EntityInfoComponent("Empty")</c>, tagged
    /// <c>SceneObjectComponent</c> by the <see cref="CreateEntityCommand"/> so it appears in the entity
    /// tree, is selectable/inspectable, and serializes as a root. Undoable — one history entry (the
    /// command reverts by disposing the created sub-graph). Public so the Entities-panel context menu and
    /// the headless <c>menu:pick add-empty</c> op share one path.</summary>
    public void AddEmptyEntity(GameState state)
    {
        const string action = "Add empty entity";
        if (!GuardEdit(state, action)) return;
        var position = _camera?.Position ?? Vector2.Zero;
        _history.Push(new CreateEntityCommand(_world, _serializer, world =>
        {
            var e = world.CreateEntity();
            e.Set(new TransformComponent(position));
            e.Set(new EntityInfoComponent("Empty"));
            return e;
        }));
    }

    // ---- Shared plumbing ----

    private static bool GuardEdit(GameState state, string action)
    {
        if (state.RunMode == RunMode.Edit) return true;
        Logger.Warning($"[level-editor] {action} is an editing action — pause the transport first.");
        return false;
    }

    private bool TryGetSelected(out Entity selected)
    {
        foreach (var e in _selectedSet.GetEntities())
        {
            if (!e.IsAlive) continue;
            selected = e;
            return true;
        }
        selected = default;
        return false;
    }

    /// <summary>The selection resolved to the GAME entity the action targets: the selected
    /// entity itself, or the bound owner when the selection is a collider proxy.</summary>
    private bool TryGetSelectionOwner(string action, out Entity selected, out Entity owner)
    {
        owner = default;
        if (!TryGetSelected(out selected))
        {
            Logger.Warning($"[level-editor] {action}: nothing is selected.");
            return false;
        }
        owner = selected.Has<GizmoProxyComponent>()
            ? selected.Get<GizmoProxyComponent>().Target
            : selected;
        if (!owner.IsAlive)
        {
            Logger.Warning($"[level-editor] {action}: the selected proxy's owner is gone.");
            return false;
        }
        // Instance-children guardrail (PF-D): order-nudge / collider-add on a prefab-owned child is
        // refused (the shared resolver for those ops); the instance ROOT stays editable.
        if (PrefabGuards.IsPrefabOwned(owner))
        {
            Logger.Warning(PrefabGuards.Refusal(action));
            return false;
        }
        return true;
    }

    private Entity FindProxy(Entity owner, ProxyBindingKind kind)
    {
        foreach (var proxy in _proxySet.GetEntities())
        {
            var binding = proxy.Get<GizmoProxyComponent>();
            if (binding.Target == owner && binding.Kind == kind) return proxy;
        }
        return default;
    }

    private static void Reselect(Entity from, Entity to)
    {
        if (from.IsAlive && from.Has<SelectedComponent>()) from.Remove<SelectedComponent>();
        if (to.IsAlive && !to.Has<SelectedComponent>()) to.Set(new SelectedComponent());
    }

    public void Dispose()
    {
        _selectedSet.Dispose();
        _proxySet.Dispose();
        GC.SuppressFinalize(this);
    }
}
