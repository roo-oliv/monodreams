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
    /// component's construction defaults (all layers, physical, enabled).</summary>
    public static ColliderComponentCommand AddBox(Entity entity, Rectangle bounds) =>
        new(entity, isBox: true, isAdd: true, bounds, null, false, new[] { -1 }, false, true);

    /// <summary>Adds a <c>ConvexColliderComponent</c> with <paramref name="modelVertices"/>
    /// (cloned) and the component's construction defaults.</summary>
    public static ColliderComponentCommand AddConvex(Entity entity, Vector2[] modelVertices) =>
        new(entity, isBox: false, isAdd: true, Rectangle.Empty,
            (Vector2[])modelVertices.Clone(), false, new[] { -1 }, false, true);

    /// <summary>Removes the entity's live <c>BoxColliderComponent</c>, snapshotting every field
    /// so undo restores it exactly.</summary>
    public static ColliderComponentCommand RemoveBox(Entity entity)
    {
        var box = entity.Get<BoxColliderComponent>();
        return new ColliderComponentCommand(entity, isBox: true, isAdd: false,
            box.Bounds, null, false, box.ActiveLayers.ToArray(), box.Passive, box.Enabled);
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
            _entity.Set(new BoxColliderComponent(
                _bounds, new HashSet<int>(_activeLayers), _passive, _enabled));
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
