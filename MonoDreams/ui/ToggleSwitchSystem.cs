using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.UI;

/// Mirrors a <see cref="ToggleSwitchComponent"/>'s on/off state onto its linked checkmark
/// entity: when on, the checkmark's mesh is filled from <c>CheckmarkMesh</c>; when off, the
/// mesh is emptied so <c>MasterRenderSystem</c> skips it (UI/HUD always render regardless of
/// <c>VisibleComponent</c>, so the mesh contents are the visibility toggle).
[With(typeof(ToggleSwitchComponent))]
public class ToggleSwitchSystem(World world) : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref readonly var toggle = ref entity.Get<ToggleSwitchComponent>();
        if (!toggle.CheckmarkEntity.IsAlive || !toggle.CheckmarkEntity.Has<DrawComponent>()) return;

        ref var draw = ref toggle.CheckmarkEntity.Get<DrawComponent>();
        if (toggle.On)
        {
            draw.SetMeshData(toggle.CheckmarkMesh);
        }
        else
        {
            draw.Type = DrawElementType.Mesh;
            draw.Vertices = [];
            draw.Indices = [];
        }
    }
}
