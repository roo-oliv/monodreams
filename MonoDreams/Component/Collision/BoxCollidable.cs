using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoDreams.Component.Collision;

public class BoxCollidable(Rectangle bounds, HashSet<int> activeLayers = null, bool passive = false, bool enabled = true) : ICollidable
{
    public Rectangle Bounds = bounds;
    public HashSet<int> ActiveLayers { get; set; } = activeLayers ?? [-1];
    public bool Passive { get; set; } = passive;
    public bool Enabled { get; set; } = enabled;
}
