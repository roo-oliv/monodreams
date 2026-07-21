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
using MonoDreams.LevelEditor.UI;
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
/// <para><b>Collider authoring (colliders-as-entities).</b> A collider is its own ENTITY, so
/// <see cref="AddBoxCollider"/> / <see cref="AddConvexCollider"/> create a CHILD collider entity of
/// the selection through <see cref="CreateEntityCommand"/> (auto-named "BoxCollider"/"PolyCollider",
/// footprint-shaped via <see cref="ColliderDefaults"/>, passive, selected after creation), and
/// <see cref="RemoveCollider"/> ("−Col") DELETES the selected collider entity (the snapshotting
/// delete — the component-remove command retired). <see cref="AddVertex"/> inserts an edge midpoint
/// into the selected convex collider entity's polygon (after the selected vertex, or into the longest
/// edge) — collinear by construction, so always convex-legal — via <see cref="ColliderEditCommand"/>.</para>
///
/// <para><b>Delete retargets a sub-element proxy.</b> When the selection is a sub-element proxy (a
/// vertex handle), Delete must NOT dispose the transient proxy entity: it retargets — a
/// <see cref="ProxyBindingKind.ConvexVertex"/> proxy deletes THAT vertex (guarded: a convex collider
/// keeps ≥ 3 vertices), a <see cref="ProxyBindingKind.BoundaryVertex"/> proxy deletes that boundary
/// point (≥ 2 guard), with the selection moving back to the owner so the editing session continues.
/// A baked product refuses Delete (it regenerates from its source). Every other selection — a plain
/// entity, INCLUDING a collider entity — is deleted whole (the snapshotting
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
    private readonly Func<Entity, bool>? _isScreenInfrastructure;
    private readonly Func<Entity>? _prefabContextRoot;
    private readonly EditorNotifications? _notifications;

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
    /// <param name="isScreenInfrastructure">PF-F: the predicate that flags screen-held KeepAlive
    /// infrastructure (the dialogue-UI root a system references live). Delete REFUSES such an entity
    /// everywhere (the crash fix — deleting it NREs the owning system); null (a test) disables the guard.</param>
    /// <param name="prefabContextRoot">PF-F: resolves the prefab root to auto-parent a new
    /// <see cref="AddEmptyEntity"/> under, when the active context is a prefab (else <c>default</c> — a
    /// scene keeps the new entity a root). Null disables auto-parenting.</param>
    public EditorCommandSystem(
        World world,
        EditorHistory history,
        SceneSerializer serializer,
        DrawLayerMap? layers = null,
        Func<GameState, bool>? orderForwardRequested = null,
        Func<GameState, bool>? orderBackRequested = null,
        Camera? camera = null,
        Func<Entity, bool>? isScreenInfrastructure = null,
        Func<Entity>? prefabContextRoot = null,
        EditorNotifications? notifications = null)
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
        _isScreenInfrastructure = isScreenInfrastructure;
        _prefabContextRoot = prefabContextRoot;
        _notifications = notifications;
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

    /// <summary>Deletes what the selection MEANS (see the class doc): a vertex/boundary-point proxy
    /// deletes that sub-element, a bake product is refused, and a plain entity — INCLUDING a collider
    /// entity — is deleted whole (the snapshotting sub-graph command). Public so the headless
    /// <c>collider:deleteVertex</c> op and the Delete key share one path.</summary>
    public void DeleteSelection(GameState state)
    {
        if (!GuardEdit(state, "Delete")) return;
        if (!TryGetSelected(out var selected)) return;

        // The LAST camera entity is not deletable (CM one-camera rule): a scene needs a camera (the reader
        // ensures one, the writer refuses a second). Refuse loudly rather than stranding the scene
        // camera-less. Deleting a camera while another exists is fine — but the writer rule keeps that from
        // ever persisting, so in practice this refuses deleting the scene's only camera.
        if (selected.Has<CameraComponent>() && IsLastCamera(selected))
        {
            Logger.Warning(
                "[level-editor] Delete refused: scenes need a camera — this is the only camera entity. " +
                "Move it, or edit the camera through the Inspector.");
            _notifications?.Notify("scenes need a camera", EditorNotifySeverity.Danger);
            return;
        }

        // Instance-children guardrail (PF-D): a prefab-owned CHILD (or a proxy bound to one) is not
        // deletable in a scene. The instance ROOT stays deletable (it carries the marker, so it is NOT
        // "owned" — deleting it removes the whole instance, snapshot-undoable as any delete).
        var deleteTarget = selected.Has<GizmoProxyComponent>() ? selected.Get<GizmoProxyComponent>().Target : selected;
        if (PrefabGuards.IsPrefabOwned(deleteTarget))
        {
            RefusePrefabOwned("Delete");
            return;
        }

        // Screen-infrastructure guard (PF-F, THE CRASH FIX): a KeepAlive entity the screen holds by
        // reference (e.g. the dialogue-UI root a live system points at) is NOT deletable — disposing it
        // strands that system on a dead handle (an NRE). Refuse loud + status everywhere delete routes
        // (command / menu / Delete key). The last camera entity (checked above) has its own tailored refusal.
        if (_isScreenInfrastructure?.Invoke(deleteTarget) == true)
        {
            Logger.Warning(
                "[level-editor] Delete refused: this entity is screen infrastructure (held live by a " +
                "screen system) — it cannot be deleted.");
            _notifications?.Notify("screen infrastructure - cannot be deleted", EditorNotifySeverity.Danger);
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
            }
            // A thickness handle (or any other sub-element proxy) has no Delete meaning — no-op.
            return;
        }

        // Baked-product guardrail (colliders-as-entities): a boundary's baked segment regenerates
        // from its source, so deleting it is meaningless (it comes back on the next bake) — refuse
        // and point at the source. Deleting the boundary removes its segments (they cascade).
        if (selected.Has<BakedProductComponent>())
        {
            Logger.Warning(
                "[level-editor] Delete refused: this is a baked product — it regenerates from its " +
                "source. Delete the source (e.g. the boundary) instead.");
            _notifications?.Notify("baked product - delete its source instead", EditorNotifySeverity.Warning);
            return;
        }

        // A plain entity — including a collider ENTITY (colliders-as-entities) — is deleted whole via
        // the snapshotting sub-graph command.
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

        // The deleted vertex's proxy despawns next sync — move the selection back to the collider
        // ENTITY (its remaining vertex handles stay up) so the editing session continues.
        Reselect(vertexProxy, owner);
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

    /// <summary>Add Collider ▸ Box: creates a CHILD box collider ENTITY of the selection (auto-named
    /// "BoxCollider"), positioned + sized to the parent's sprite footprint (full width × bottom
    /// quarter, feet-anchored — <see cref="ColliderDefaults.BoxChild"/>) or a 32×32 default for a
    /// sprite-less parent, passive (a static blocker). Undoable — one <see cref="CreateEntityCommand"/>;
    /// the child is selected after creation. A body may have N colliders, so there is no "already
    /// present" guard.</summary>
    public void AddBoxCollider(GameState state) => AddColliderChild(state, isBox: true);

    /// <summary>Add Collider ▸ Polygon: creates a CHILD convex collider ENTITY of the selection
    /// (auto-named "PolyCollider"), shaped as a hexagon inscribed in the parent's footprint (or a
    /// small default for a sprite-less parent), passive. Undoable; selected after creation.</summary>
    public void AddConvexCollider(GameState state) => AddColliderChild(state, isBox: false);

    /// <summary>Shared Add-Collider body: builds the child collider entity under the selection's
    /// owner through <see cref="CreateEntityCommand"/> (which parents it — dropping the save-root tag
    /// — so it serializes inside the parent's closure), refreshes a convex child's world data (physics
    /// is frozen in Edit), and selects it.</summary>
    private void AddColliderChild(GameState state, bool isBox)
    {
        var action = isBox ? "Add box collider" : "Add polygon collider";
        if (!GuardEdit(state, action)) return;
        if (!TryGetSelectionOwner(action, out _, out var parent)) return;

        var name = isBox ? "BoxCollider" : "PolyCollider";
        var hasSprite = parent.Has<SpriteInfoComponent>();
        var sprite = hasSprite ? parent.Get<SpriteInfoComponent>() : default;
        var created = default(Entity);
        _history.Push(new CreateEntityCommand(_world, _serializer, w =>
        {
            var e = w.CreateEntity();
            e.Set(new EntityInfoComponent(name));
            if (isBox)
            {
                var (center, size) = hasSprite ? ColliderDefaults.BoxChild(sprite) : ColliderDefaults.FallbackBoxChild;
                e.Set(new TransformComponent(center));
                // Footprints are passive static blockers (ColliderDefaults.FootprintPassive): a static
                // collider blocks an active body without being pushed by resolution.
                e.Set(new BoxColliderComponent(size, passive: ColliderDefaults.FootprintPassive));
            }
            else
            {
                var (center, verts) = hasSprite ? ColliderDefaults.HexagonChild(sprite) : ColliderDefaults.FallbackHexagonChild();
                e.Set(new TransformComponent(center));
                e.Set(new ConvexColliderComponent(verts, passive: ColliderDefaults.FootprintPassive));
            }
            created = e;
            return e;
        }, parentTo: parent));

        if (!created.IsAlive) return;
        // A fresh convex collider's world data assumes an identity transform; the child is now
        // parented + offset, so refresh against its world transform (nothing else does in Edit).
        if (created.Has<ConvexColliderComponent>() && created.Has<TransformComponent>())
            created.Get<ConvexColliderComponent>().UpdateWorldVertices(created.Get<TransformComponent>());
        SelectOnly(created);
    }

    /// <summary>"−Col": deletes the selected collider ENTITY (colliders-as-entities retired the
    /// component-remove command — the normal snapshotting delete is the mechanism now). Resolves a
    /// selected sub-element proxy (a vertex handle) to its owner collider, then routes through
    /// <see cref="DeleteSelection"/> so the prefab-owned / baked-product / screen-infra guards + the
    /// snapshot-undo all apply. Loud no-op when the selection is not a collider entity.</summary>
    public void RemoveCollider(GameState state)
    {
        const string action = "Remove collider";
        if (!GuardEdit(state, action)) return;
        if (!TryGetSelected(out var selected))
        {
            Logger.Warning($"[level-editor] {action}: nothing is selected.");
            return;
        }

        var target = selected.Has<GizmoProxyComponent>() ? selected.Get<GizmoProxyComponent>().Target : selected;
        if (!target.IsAlive || !(target.Has<BoxColliderComponent>() || target.Has<ConvexColliderComponent>()))
        {
            Logger.Warning($"[level-editor] {action}: select a collider entity to remove.");
            return;
        }

        // Delete the collider ENTITY itself (not a sub-element): make it the selection, then reuse
        // DeleteSelection's guarded, snapshotting delete.
        if (!selected.Equals(target)) SelectOnly(target);
        DeleteSelection(state);
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
        // PF-F: in a prefab context, parent the new empty under the single prefab root (single-root
        // assembly); default (a scene) keeps it a save-root.
        var parentTo = _prefabContextRoot?.Invoke() ?? default;
        _history.Push(new CreateEntityCommand(_world, _serializer, world =>
        {
            var e = world.CreateEntity();
            e.Set(new TransformComponent(position));
            e.Set(new EntityInfoComponent("Empty"));
            return e;
        }, parentTo));
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

    /// <summary>Whether <paramref name="candidate"/> is the ONLY live camera entity in the world — the
    /// delete guard refuses deleting it (CM: a scene needs a camera).</summary>
    private bool IsLastCamera(Entity candidate)
    {
        using var cameras = _world.GetEntities().With<CameraComponent>().AsSet();
        foreach (var e in cameras.GetEntities())
            if (e.IsAlive && !e.Equals(candidate)) return false; // another camera exists
        return true;
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
            RefusePrefabOwned(action);
            return false;
        }
        return true;
    }

    /// <summary>Refuses a mutation on a prefab-owned child — logs the shared hint AND raises a status
    /// notification (PF-F: guardrail hints surface, not just log).</summary>
    private void RefusePrefabOwned(string action)
    {
        Logger.Warning(PrefabGuards.Refusal(action));
        _notifications?.Notify("prefab child - open the prefab or Unpack", EditorNotifySeverity.Warning);
    }

    private static void Reselect(Entity from, Entity to)
    {
        if (from.IsAlive && from.Has<SelectedComponent>()) from.Remove<SelectedComponent>();
        if (to.IsAlive && !to.Has<SelectedComponent>()) to.Set(new SelectedComponent());
    }

    /// <summary>Single-selects <paramref name="target"/>: clears every OTHER selection tag and sets
    /// it on the target — used to auto-select a freshly-created collider child, and to retarget the
    /// "−Col" delete onto the collider entity itself.</summary>
    private void SelectOnly(Entity target)
    {
        List<Entity>? toClear = null;
        foreach (var e in _selectedSet.GetEntities())
            if (!e.Equals(target)) (toClear ??= new List<Entity>()).Add(e);
        if (toClear != null)
            foreach (var e in toClear)
                if (e.IsAlive && e.Has<SelectedComponent>())
                    e.Remove<SelectedComponent>();
        if (target.IsAlive && !target.Has<SelectedComponent>())
            target.Set(new SelectedComponent());
    }

    public void Dispose()
    {
        _selectedSet.Dispose();
        _proxySet.Dispose();
        GC.SuppressFinalize(this);
    }
}
