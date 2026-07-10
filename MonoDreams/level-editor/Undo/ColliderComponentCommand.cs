#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;

namespace MonoDreams.LevelEditor.Undo;

/// <summary>
/// A reversible <b>add / remove</b> of a whole collider component (island-authoring plan §5.1 —
/// the "Add box collider / Add polygon collider / Remove collider" actions), complementing
/// <see cref="ColliderEditCommand"/> which edits a collider's FIELDS. Pure data: the entity,
/// which component type, the direction (add or remove), and a full value snapshot of the
/// component — bounds or model vertices plus <c>ActiveLayers</c> / <c>Passive</c> /
/// <c>Enabled</c> / <c>IgnoreTransformRotation</c> — taken at construction (a Remove snapshots
/// the LIVE component so undo restores it field-for-field). Apply/Revert rebuild a FRESH
/// component instance from the immutable snapshot each time (never re-attaching a cached
/// instance later edits could have mutated), so the command stays replayable; a rebuilt convex
/// refreshes <c>WorldVertices</c>/<c>BroadPhaseAABB</c> against the live transform (physics is
/// frozen in Edit — the collision premise). A dead target is a safe no-op.
/// </summary>
public sealed class ColliderComponentCommand : IEditorCommand
{
    private readonly Entity _entity;
    private readonly bool _isBox;
    private readonly bool _isAdd;

    // The value snapshot (box uses _bounds; convex uses _vertices + _ignoreRotation).
    private readonly Rectangle _bounds;
    private readonly Vector2[]? _vertices;
    private readonly bool _ignoreRotation;
    private readonly int[] _activeLayers;
    private readonly bool _passive;
    private readonly bool _enabled;

    private ColliderComponentCommand(Entity entity, bool isBox, bool isAdd,
        Rectangle bounds, Vector2[]? vertices, bool ignoreRotation,
        int[] activeLayers, bool passive, bool enabled)
    {
        _entity = entity;
        _isBox = isBox;
        _isAdd = isAdd;
        _bounds = bounds;
        _vertices = vertices;
        _ignoreRotation = ignoreRotation;
        _activeLayers = activeLayers;
        _passive = passive;
        _enabled = enabled;
    }

    /// <summary>Adds a <c>BoxColliderComponent</c> with <paramref name="bounds"/> and the
    /// component's construction defaults (all layers, enabled). <paramref name="passive"/> selects
    /// the collider's <b>static-vs-active</b> role — NOT blocker-vs-trigger: <c>true</c> is a static
    /// collider that does not initiate collisions (never moved by resolution) yet still blocks an
    /// active body — the right choice for static level geometry (footprints, walls, boundaries) AND
    /// trigger zones alike; <c>false</c> is an active collider that initiates and is displaced by
    /// resolution (a moving body). A static <b>footprint</b> must therefore pass <c>true</c>
    /// (<see cref="MonoDreams.LevelEditor.Proxy.ColliderDefaults.FootprintPassive"/>) or the prop
    /// would drift when the player walks into it. Whether a passive collider reads as a physical
    /// blocker or a fire-only trigger (island-authoring §5.3) is the game's <c>EntityInfoComponent</c>
    /// classification, not this flag. The <c>false</c> default matches
    /// <c>BoxColliderComponent</c>'s own; callers authoring static geometry pass <c>true</c>.</summary>
    public static ColliderComponentCommand AddBox(Entity entity, Rectangle bounds, bool passive = false) =>
        new(entity, isBox: true, isAdd: true, bounds, null, false, new[] { -1 }, passive, true);

    /// <summary>Adds a <c>ConvexColliderComponent</c> with <paramref name="modelVertices"/>
    /// (cloned) and the component's construction defaults. <paramref name="passive"/> selects the
    /// static-vs-active role (see <see cref="AddBox"/>): a static footprint passes <c>true</c>.</summary>
    public static ColliderComponentCommand AddConvex(Entity entity, Vector2[] modelVertices, bool passive = false) =>
        new(entity, isBox: false, isAdd: true, Rectangle.Empty,
            (Vector2[])modelVertices.Clone(), false, new[] { -1 }, passive, true);

    /// <summary>Removes the entity's live <c>BoxColliderComponent</c>, snapshotting every field
    /// so undo restores it exactly.</summary>
    public static ColliderComponentCommand RemoveBox(Entity entity)
    {
        var box = entity.Get<BoxColliderComponent>();
        // Snapshot Size as the rect extent (location unused — the box is centered on its entity).
        var bounds = new Rectangle(0, 0, (int)MathF.Round(box.Size.X), (int)MathF.Round(box.Size.Y));
        return new ColliderComponentCommand(entity, isBox: true, isAdd: false,
            bounds, null, false, box.ActiveLayers.ToArray(), box.Passive, box.Enabled);
    }

    /// <summary>Removes the entity's live <c>ConvexColliderComponent</c>, snapshotting every
    /// field so undo restores it exactly.</summary>
    public static ColliderComponentCommand RemoveConvex(Entity entity)
    {
        var convex = entity.Get<ConvexColliderComponent>();
        return new ColliderComponentCommand(entity, isBox: false, isAdd: false,
            Rectangle.Empty, (Vector2[])convex.ModelVertices.Clone(), convex.IgnoreTransformRotation,
            convex.ActiveLayers.ToArray(), convex.Passive, convex.Enabled);
    }

    public void Apply(World world)
    {
        if (_isAdd) SetComponent();
        else RemoveComponent();
    }

    public void Revert(World world)
    {
        if (_isAdd) RemoveComponent();
        else SetComponent();
    }

    private void SetComponent()
    {
        if (!_entity.IsAlive) return;

        if (_isBox)
        {
            // TODO(CE-C): the box is a centered Size on the collider entity now; the former
            // footprint offset (Bounds.Location) would move onto a child collider entity's Transform.
            _entity.Set(new BoxColliderComponent(
                new Vector2(_bounds.Width, _bounds.Height), new HashSet<int>(_activeLayers), _passive, _enabled));
            return;
        }

        if (_vertices == null) return;
        var convex = new ConvexColliderComponent(
            (Vector2[])_vertices.Clone(), new HashSet<int>(_activeLayers), _passive, _enabled,
            _ignoreRotation);
        // Physics is frozen in Edit, so nothing else derives the world data for a fresh collider
        // on a positioned entity (the constructor assumes an identity transform).
        if (_entity.Has<TransformComponent>())
            convex.UpdateWorldVertices(_entity.Get<TransformComponent>());
        _entity.Set(convex);
    }

    private void RemoveComponent()
    {
        if (!_entity.IsAlive) return;
        if (_isBox)
        {
            if (_entity.Has<BoxColliderComponent>()) _entity.Remove<BoxColliderComponent>();
        }
        else
        {
            if (_entity.Has<ConvexColliderComponent>()) _entity.Remove<ConvexColliderComponent>();
        }
    }
}
