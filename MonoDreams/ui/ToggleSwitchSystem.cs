using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.UI;

/// Mirrors a <see cref="ToggleSwitchComponent"/>'s on/off state onto its
/// linked sprite entity's <c>SpriteInfoComponent.Source</c> rectangle, so
/// the sprite renders the matching frame.
[With(typeof(ToggleSwitchComponent))]
public class ToggleSwitchSystem(World world) : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref readonly var toggle = ref entity.Get<ToggleSwitchComponent>();
        if (!toggle.SpriteEntity.IsAlive || !toggle.SpriteEntity.Has<SpriteInfoComponent>()) return;
        ref var sprite = ref toggle.SpriteEntity.Get<SpriteInfoComponent>();
        sprite.Source = toggle.On ? toggle.OnSource : toggle.OffSource;
    }
}
