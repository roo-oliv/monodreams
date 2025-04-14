using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Draw;

[With(typeof(DrawComponent))]
public abstract class DrawPrepSystemBase(World world, bool useParallel = false)
    : AEntitySetSystem<GameState>(world, useParallel)
{
    // This method might be overridden if systems need finer control
    // or if multiple systems add to the same DrawComponent type.
    protected virtual void ClearPreviousDrawables(ref DrawComponent drawComponent)
    {
        // Simple approach: Clear everything. Assumes this system is solely
        // responsible for the DrawElements it adds.
        // More complex scenarios might involve filtering Drawables by type/source system.
        drawComponent.Drawables.Clear();
        // If multiple Prep systems run in parallel and add to the same DrawComponent,
        // clearing like this is problematic. A dedicated sequential ClearSystem before
        // the parallel prep systems would be safer. For simplicity now, we clear here.
    }
}