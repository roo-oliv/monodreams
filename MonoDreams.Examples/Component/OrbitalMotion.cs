using Microsoft.Xna.Framework;

namespace MonoDreams.Examples.Component;

/// <summary>
/// Component for entities that orbit around their parent transform.
/// </summary>
public class OrbitalMotion
{
    public float Angle { get; set; }
    public float Radius { get; set; }
    public float Speed { get; set; }  // radians per second (negative = clockwise)
    public Vector2 CenterOffset { get; set; }  // offset from parent center
}
