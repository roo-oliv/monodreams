using Microsoft.Xna.Framework;

namespace MonoDreams.Component;

public class CameraFollowTargetComponent
{
    public float DampingX { get; set; } = 5.0f;
    public float DampingY { get; set; } = 5.0f;
    public float MaxDistanceX { get; set; } = 100.0f;
    public float MaxDistanceY { get; set; } = 100.0f;
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Optional world-space rectangle the camera is kept within. When set,
    /// <see cref="MonoDreams.System.Camera.CameraFollowSystem"/> clamps the *target*
    /// to these bounds before smoothing, so the camera eases toward an in-bounds goal
    /// and never aims past the rectangle's edges (e.g. to keep the view within a level).
    /// Because the target is clamped rather than the resolved position, a camera that
    /// starts outside the bounds eases smoothly back inside instead of snapping to the
    /// edge. When null the camera follows freely.
    /// </summary>
    public Rectangle? Bounds { get; set; } = null;
}
