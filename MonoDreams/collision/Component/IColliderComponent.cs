using System.Collections.Generic;
using System.Linq;

namespace MonoDreams.Component.Collision;

public interface IColliderComponent
{
    HashSet<int> ActiveLayers { get; set; }
    public bool Passive { get; set; }
    public bool Enabled { get; set; }
    
    public bool SharesLayerWith(IColliderComponent other) =>
        (ActiveLayers.Contains(-1) && other.ActiveLayers.Count > 0)
        || (other.ActiveLayers.Contains(-1) && ActiveLayers.Count > 0)
        || ActiveLayers.Overlaps(other.ActiveLayers);

    public IEnumerable<int> SharedLayers(IColliderComponent other)
    {
        if (ActiveLayers.Contains(-1) && other.ActiveLayers.Count > 0) return other.ActiveLayers;
        if (other.ActiveLayers.Contains(-1) && ActiveLayers.Count > 0) return ActiveLayers;
        return ActiveLayers.Intersect(other.ActiveLayers);
    }
}