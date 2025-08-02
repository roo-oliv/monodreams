using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Examples.Component.Cursor;
using MonoDreams.State;
using MonoGame.SplineFlower.Spline.Types;

namespace MonoDreams.Examples.System.Camera;

[With(typeof(CursorInput))]
public class CameraInputSystem(World world, MonoDreams.Component.Camera camera) : AEntitySetSystem<GameState>(world)
{
    public bool IsEnabled { get; set; } = true;

    protected override void Update(GameState state, in Entity entity)
    {
        var cursorInput = entity.Get<CursorInput>();

        camera.Zoom += cursorInput.ScrollWheelDelta * 0.001f; // Adjust zoom sensitivity as needed
    }
    
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}