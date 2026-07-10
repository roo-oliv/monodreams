using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MonoDreams.Extensions.Monogame;

namespace MonoDreams.Component.Collision;

/// <summary>
/// Convex polygon collider defined by an ordered set of vertices (clockwise winding).
/// ModelVertices are in the collider ENTITY's local space; WorldVertices are updated each frame by
/// the detection system from that entity's own <see cref="TransformComponent"/> world transform
/// (colliders-as-entities model). BroadPhaseAABB is recomputed from WorldVertices for fast rejection
/// before SAT.
/// </summary>
public class ConvexColliderComponent : IColliderComponent
{
    /// <summary>Local-space vertices defining the convex polygon shape (clockwise winding).</summary>
    public Vector2[] ModelVertices;

    /// <summary>Pre-allocated world-space vertices, updated each frame from ModelVertices + TransformComponent.</summary>
    public Vector2[] WorldVertices;

    /// <summary>AABB computed from WorldVertices for broad-phase rejection.</summary>
    public CollisionRect BroadPhaseAABB;

    /// <summary>
    /// When true, UpdateWorldVertices ignores TransformComponent.Rotation (treats it as 0).
    /// Used for colliders whose rotation is baked into ModelVertices (e.g. imported from Blender).
    /// </summary>
    public bool IgnoreTransformRotation;

    public HashSet<int> ActiveLayers { get; set; }
    public bool Passive { get; set; }
    public bool Enabled { get; set; }

    public ConvexColliderComponent(Vector2[] modelVertices, HashSet<int> activeLayers = null, bool passive = false,
        bool enabled = true, bool ignoreTransformRotation = false)
    {
        if (modelVertices == null || modelVertices.Length < 3)
            throw new ArgumentException("ConvexColliderComponent requires at least 3 vertices.", nameof(modelVertices));

        ModelVertices = modelVertices;
        WorldVertices = new Vector2[modelVertices.Length];
        Array.Copy(modelVertices, WorldVertices, modelVertices.Length);
        BroadPhaseAABB = SATCollision.ComputeAABB(WorldVertices);
        ActiveLayers = activeLayers ?? [-1];
        Passive = passive;
        Enabled = enabled;
        IgnoreTransformRotation = ignoreTransformRotation;
    }

    /// <summary>
    /// Transforms ModelVertices into WorldVertices using the entity's WORLD position, rotation, and
    /// scale (<c>TransformComponent.WorldPosition</c>/<c>WorldRotation</c>/<c>WorldScale</c>), so a
    /// collider authored on a CHILD entity (e.g. a prefab instance's child) sits at its world
    /// location, not at its parent-relative local one. Recomputes BroadPhaseAABB afterward. For a
    /// root-level entity the world transform equals the local one, so this is byte-identical to the
    /// former local-only derivation (position == world position, rotation/scale unchanged).
    /// The world transform is only fresh after the entity's matrix link is set + any moved ancestor
    /// is un-dirtied — foundation's <c>HierarchySystem</c> contract (see foundation premise
    /// "HierarchySystem must run ahead of any system reading WorldPosition").
    /// </summary>
    public void UpdateWorldVertices(TransformComponent transform)
    {
        var pos = transform.WorldPosition;
        var rot = IgnoreTransformRotation ? 0f : transform.WorldRotation;
        var scale = transform.WorldScale;
        var cos = MathF.Cos(rot);
        var sin = MathF.Sin(rot);

        for (var i = 0; i < ModelVertices.Length; i++)
        {
            var v = ModelVertices[i];
            // Scale, then rotate, then translate
            var sx = v.X * scale.X;
            var sy = v.Y * scale.Y;
            WorldVertices[i] = new Vector2(
                sx * cos - sy * sin + pos.X,
                sx * sin + sy * cos + pos.Y);
        }

        BroadPhaseAABB = SATCollision.ComputeAABB(WorldVertices);
    }
}
