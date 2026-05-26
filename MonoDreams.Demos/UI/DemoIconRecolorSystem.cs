using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.Demos.UI;

/// Mirrors a <see cref="DemoButtonComponent"/>'s hover/active state onto the
/// linked icon sprite. If <see cref="IconRecolorTarget.DefaultSource"/> is set
/// (along with HoverSource / ActiveSource), the sprite's source rectangle is
/// swapped to the matching state. Otherwise the sprite's tint color is swapped.
[With(typeof(DemoButtonComponent), typeof(IconRecolorTarget))]
public class DemoIconRecolorSystem(World world) : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref readonly var button = ref entity.Get<DemoButtonComponent>();
        ref readonly var link = ref entity.Get<IconRecolorTarget>();
        if (!link.Icon.IsAlive || !link.Icon.Has<SpriteInfoComponent>()) return;

        ref var sprite = ref link.Icon.Get<SpriteInfoComponent>();

        if (link.DefaultSource.HasValue)
        {
            // Source-rect swap mode (sprite-sheet states).
            sprite.Source =
                button.IsActive  && link.ActiveSource.HasValue  ? link.ActiveSource.Value
              : button.IsHovered && link.HoverSource.HasValue   ? link.HoverSource.Value
              :                                                   link.DefaultSource.Value;
        }
        else
        {
            // Tint-swap mode (single-frame icons).
            sprite.Color = button.IsActive
                ? button.ActiveColor
                : button.IsHovered ? button.HoveredColor : button.DefaultColor;
        }
    }
}
