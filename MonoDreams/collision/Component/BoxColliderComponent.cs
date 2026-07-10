using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoDreams.Component.Collision;

/// <summary>
/// Axis-aligned box collider. Under the colliders-as-entities model the box is a SHAPE only:
/// its pose (position / rotation / scale) comes from the collider entity's own
/// <see cref="TransformComponent"/>. The box is <b>centered</b> on the entity's
/// <c>WorldPosition</c> with extent <see cref="Size"/> scaled by <c>WorldScale</c> — the former
/// embedded <c>Bounds</c> offset is gone; to place a box off-center, position (or parent) the
/// collider entity. Rotation is intentionally ignored (the swept-AABB model treats the box as
/// axis-aligned — use a <see cref="ConvexColliderComponent"/> for a rotated hitbox).
/// </summary>
public class BoxColliderComponent(Vector2 size, HashSet<int> activeLayers = null, bool passive = false, bool enabled = true) : IColliderComponent
{
    /// <summary>The box extent (width, height) in the collider entity's local space, centered on
    /// its <c>WorldPosition</c> and scaled by <c>WorldScale</c> at world-derivation time.</summary>
    public Vector2 Size = size;
    public HashSet<int> ActiveLayers { get; set; } = activeLayers ?? [-1];
    public bool Passive { get; set; } = passive;
    public bool Enabled { get; set; } = enabled;
}
