using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.System.Draw;

/// <summary>
/// Prepares mesh DrawComponents by applying world transforms.
/// This allows meshes to respect parent-child transform hierarchies.
/// </summary>
[With(typeof(DrawComponent), typeof(TransformComponent), typeof(VisibleComponent))]
public class MeshPrepSystem(World world) : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var drawComponent = ref entity.Get<DrawComponent>();

        // Only process mesh draw components
        if (drawComponent.Type != DrawElementType.Mesh)
            return;

        ref readonly var transform = ref entity.Get<TransformComponent>();

        // Store the world transform matrix for mesh rendering
        // This will be applied in MasterRenderSystem via BasicEffect.World
        drawComponent.WorldMatrix = transform.WorldMatrix;
    }
}
