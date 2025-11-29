using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;
using Visible = MonoDreams.Examples.Component.Draw.Visible;

namespace MonoDreams.Examples.System.Draw;

/// <summary>
/// Prepares mesh DrawComponents by applying world transforms.
/// This allows meshes to respect parent-child transform hierarchies.
/// </summary>
[With(typeof(DrawComponent), typeof(Transform), typeof(Visible))]
public class MeshPrepSystem(World world) : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var drawComponent = ref entity.Get<DrawComponent>();

        // Only process mesh draw components
        if (drawComponent.Type != DrawElementType.Mesh)
            return;

        ref readonly var transform = ref entity.Get<Transform>();

        // Store the world transform matrix for mesh rendering
        // This will be applied in MasterRenderSystem via BasicEffect.World
        drawComponent.WorldMatrix = transform.WorldMatrix;
    }
}
