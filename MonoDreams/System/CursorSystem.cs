using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System;

public class CursorSystem : AEntitySetSystem<GameState>
{
    private readonly Camera _camera;

    public CursorSystem(World world, Camera camera, IParallelRunner runner)
        : base(world.GetEntities().With<CursorController>().With<PlayerInput>().AsSet(), runner)
    {
        _camera = camera;
    }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var position = ref entity.Get<Position>();
        ref var playerInput = ref entity.Get<PlayerInput>();
        
        var cameraCursorPosition = _camera.ScreenToWorld(playerInput.CursorPosition);
        
        position.NextLocation = cameraCursorPosition;
    }
}