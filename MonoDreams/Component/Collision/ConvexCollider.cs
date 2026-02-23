using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using MonoDreams.Extensions.Monogame;

namespace MonoDreams.Component.Collision;

/// <summary>
/// Convex polygon collider defined by an ordered set of vertices (clockwise winding).
/// ModelVertices are in local space; WorldVertices are updated each frame by the detection system.
/// BroadPhaseAABB is recomputed from WorldVertices for fast rejection before SAT.
/// </summary>
public class ConvexCollider : ICollider
{
    /// <summary>Local-space vertices defining the convex polygon shape (clockwise winding).</summary>
    public Vector2[] ModelVertices;

    /// <summary>Pre-allocated world-space vertices, updated each frame from ModelVertices + Transform.</summary>
    public Vector2[] WorldVertices;

    /// <summary>AABB computed from WorldVertices for broad-phase rejection.</summary>
    public CollisionRect BroadPhaseAABB;

    /// <summary>
    /// When true, UpdateWorldVertices ignores Transform.Rotation (treats it as 0).
    /// Used for colliders whose rotation is baked into ModelVertices (e.g. imported from Blender).
    /// </summary>
    public bool IgnoreTransformRotation;

    public HashSet<int> ActiveLayers { get; set; }
    public bool Passive { get; set; }
    public bool Enabled { get; set; }

    public ConvexCollider(Vector2[] modelVertices, HashSet<int> activeLayers = null, bool passive = false,
        bool enabled = true, bool ignoreTransformRotation = false)
    {
        if (modelVertices == null || modelVertices.Length < 3)
            throw new ArgumentException("ConvexCollider requires at least 3 vertices.", nameof(modelVertices));

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
    /// Transforms ModelVertices into WorldVertices using the entity's position, rotation, and scale.
    /// Recomputes BroadPhaseAABB afterward.
    /// </summary>
    public void UpdateWorldVertices(Transform transform)
    {
        var pos = transform.Position;
        var rot = IgnoreTransformRotation ? 0f : transform.Rotation;
        var scale = transform.Scale;
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
