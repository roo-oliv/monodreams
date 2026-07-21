namespace MonoDreams.Component.Collision;

/// <summary>
/// Empty marker component auto-applied to any collider ENTITY (one carrying a BoxColliderComponent
/// or ConvexColliderComponent). Used by the detection system to query collider entities of either
/// shape through a single tag. A collider is its own entity now, so this tags the collider entity
/// itself — not a "body" that owns it.
/// </summary>
public struct ColliderTagComponent;
