#nullable enable
using DefaultEcs;
using Facebook.Yoga;
using MonoDreams.Component;

namespace MonoDreams.Extensions.ECS;

public static class LayoutExtensions
{
    public static Entity CreateLayoutEntity(this World world, YogaNode? yogaNode = null, Entity? parent = null)
    {
        var entity = world.CreateEntity();
        var layoutNode = new LayoutNode(yogaNode ?? new YogaNode());
        entity.Set(layoutNode);

        if (parent != null && parent.Value.Has<LayoutNode>())
        {
            layoutNode.Parent = parent.Value;
            parent.Value.Get<LayoutNode>().Children.Add(entity);
        }

        return entity;
    }
}