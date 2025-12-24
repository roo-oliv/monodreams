using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Examples.Component.Layout;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Layout;

/// <summary>
/// System that measures intrinsic sizes of slot content using callbacks
/// and updates their layout nodes with the measured dimensions.
/// Must run BEFORE AutoLayoutSystem.
/// </summary>
[With(typeof(LayoutSlot))]
public partial class IntrinsicSizingSystem : AEntitySetSystem<GameState>
{
    public IntrinsicSizingSystem(World world) : base(world)
    {
    }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var slot = ref entity.Get<LayoutSlot>();

        // Only process slots that need remeasuring and have a measurer
        if (!slot.NeedsRemeasure) return;
        if (slot.SizeMeasurer == null) return;
        if (!slot.Content.HasValue || !slot.Content.Value.IsAlive) return;

        // Invoke the sizing callback
        var size = slot.SizeMeasurer(slot.Content.Value);

        // Update layout node dimensions
        slot.Node.Width = size.X;
        slot.Node.Height = size.Y;
        slot.Node.WidthAuto = false;
        slot.Node.HeightAuto = false;

        slot.NeedsRemeasure = false;
    }
}
