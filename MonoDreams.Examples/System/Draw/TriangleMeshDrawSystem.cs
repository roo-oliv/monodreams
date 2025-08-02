using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Draw;

[With(typeof(TriangleMeshInfo), typeof(Transform))]
public class TriangleMeshPrepSystem(World world) : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref readonly var meshInfo = ref entity.Get<TriangleMeshInfo>();
        ref readonly var transform = ref entity.Get<Transform>();
        
        // Create or update DrawComponent for triangles
        if (!entity.Has<DrawComponent>())
        {
            entity.Set(new DrawComponent());
        }
        
        ref var drawComponent = ref entity.Get<DrawComponent>();
        
        drawComponent.Type = DrawElementType.Triangles;
        drawComponent.Target = meshInfo.Target;
        drawComponent.Position = transform.CurrentPosition;
        drawComponent.Rotation = transform.CurrentRotation;
        drawComponent.LayerDepth = meshInfo.LayerDepth;
        drawComponent.Vertices = meshInfo.Vertices;
        drawComponent.Indices = meshInfo.Indices;
        drawComponent.PrimitiveType = meshInfo.PrimitiveType;
    }
}
