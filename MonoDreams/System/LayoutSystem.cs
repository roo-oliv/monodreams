using System.Linq;
using DefaultEcs;
using DefaultEcs.System;
using Facebook.Yoga;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.System;

public class LayoutSystem(World world, ResolutionIndependentRenderer renderer) : ISystem<GameState>
{
    private YogaNode _rootNode = new();
    private Vector2 _rootPosition = new(-renderer.VirtualWidth / 2, -renderer.VirtualHeight / 2);

    public void Update(GameState gameState)
    {
        var rootEntities = world.GetEntities()
            .With((in LayoutNode n) => n.Parent == null)
            .AsEnumerable();
        var roots = rootEntities as Entity[] ?? rootEntities.ToArray();

        _rootNode.Clear();
        foreach (var rootEntity in roots)
        {
            BuildLayoutTree(rootEntity, _rootNode);
        }

        _rootNode.CalculateLayout();

        foreach (var rootEntity in roots)
        {
            ApplyLayout(rootEntity, _rootPosition);
        }
    }

    public bool IsEnabled { get; set; } = true;

    private void BuildLayoutTree(in Entity entity, YogaNode parentNode)
    {
        ref var layoutNode = ref entity.Get<LayoutNode>();
        
        if (!parentNode.Contains(layoutNode.Node))
        {
            layoutNode.Node.Parent?.RemoveChild(layoutNode.Node);
            parentNode.AddChild(layoutNode.Node);
        }

        // if (entity.Has<Size>())
        // {
        //     ref var size = ref entity.Get<Size>();
        //     layoutNode.Node.Width = size.Width;
        //     layoutNode.Node.Height = size.Height;
        // }

        // Recursively build tree for children
        foreach (var child in layoutNode.Children)
        {
            BuildLayoutTree(child, layoutNode.Node);
        }
    }

    private void ApplyLayout(in Entity entity, Vector2 parentPosition)
    {
        ref var layoutNode = ref entity.Get<LayoutNode>();
        var position = new Vector2(
            parentPosition.X + layoutNode.Node.LayoutX,
            parentPosition.Y + layoutNode.Node.LayoutY
        );

        if (entity.Has<Position>())
        {
            ref var entityPosition = ref entity.Get<Position>();
            entityPosition.CurrentLocation = position;
        }

        foreach (var child in layoutNode.Children)
        {
            ApplyLayout(child, position);
        }
    }
    
    public void Dispose()
    {
        _rootNode.Clear();
    }
}