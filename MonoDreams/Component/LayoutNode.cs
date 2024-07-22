#nullable enable
using System.Collections.Generic;
using DefaultEcs;
using Facebook.Yoga;

namespace MonoDreams.Component;

public class LayoutNode(YogaNode? node)
{
    public YogaNode Node { get; } = node ?? new YogaNode();
    public Entity? Parent { get; set; }
    public List<Entity> Children { get; } = [];
}