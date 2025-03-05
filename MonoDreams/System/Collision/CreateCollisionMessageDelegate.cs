using DefaultEcs;
using Microsoft.Xna.Framework;

namespace MonoDreams.System.Collision;

public delegate TCollisionMessage CreateCollisionMessageDelegate<out TCollisionMessage>(
    Entity entity,
    Entity target,
    Vector2 contactPoint,
    Vector2 contactNormal,
    float contactTime,
    int layer);