using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.State;
using MonoDreams.UI;

namespace MonoDreams.Demos.UI;

/// Hover detection + click dispatch for <see cref="DemoButtonComponent"/>.
/// On click it publishes <see cref="DemoButtonClicked"/> with the button's id;
/// the owning screen subscribes and routes by id.
/// Uses <c>CursorInputComponent.VirtualPosition</c> for HUD-target buttons so
/// camera movement does not desync the cursor from on-screen UI.
[With(typeof(DemoButtonComponent), typeof(TransformComponent), typeof(SimpleButtonComponent))]
public class DemoButtonInteractionSystem(World world) : AEntitySetSystem<GameState>(world)
{
    private readonly EntitySet _cursors = world.GetEntities().With<CursorInputComponent>().AsSet();

    protected override void Update(GameState state, in Entity entity)
    {
        ref var demoButton = ref entity.Get<DemoButtonComponent>();
        ref readonly var transform = ref entity.Get<TransformComponent>();
        ref readonly var button = ref entity.Get<SimpleButtonComponent>();

        var cursors = _cursors.GetEntities();
        if (cursors.Length == 0) return;
        ref readonly var cursor = ref cursors[0].Get<CursorInputComponent>();

        ref var outline = ref entity.Get<SimpleButtonComponent>();

        // Disabled wins over everything: paint the muted colors, never hover, never click.
        if (demoButton.IsDisabled)
        {
            demoButton.IsHovered = false;
            if (button.TextEntity is { } disabledText)
                disabledText.Get<DynamicTextComponent>().Color = demoButton.DisabledColor;
            outline.Color = demoButton.DisabledColor;
            if (demoButton.DefaultFillColor.A > 0)
                outline.FillColor = demoButton.DisabledFillColor;
            return;
        }

        var bounds = new Rectangle(
            (int)transform.WorldPosition.X,
            (int)transform.WorldPosition.Y,
            (int)button.Size.X,
            (int)button.Size.Y);

        var cursorPos = button.Target == RenderTargetID.HUD
            ? cursor.VirtualPosition
            : cursor.WorldPosition;

        demoButton.IsHovered = bounds.Contains(cursorPos);

        // Active wins over hover. Hover wins over default.
        var color = demoButton.IsActive
            ? demoButton.ActiveColor
            : demoButton.IsHovered ? demoButton.HoveredColor : demoButton.DefaultColor;

        if (button.TextEntity is { } textEntity)
        {
            ref var text = ref textEntity.Get<DynamicTextComponent>();
            // A constant text override (grey-fill menu buttons) keeps the label dark while the
            // border still tracks state; otherwise the label tracks the state color.
            text.Color = demoButton.TextColorOverride.A > 0 ? demoButton.TextColorOverride : color;
        }

        // Also recolor the outline so the active row's border tracks the same accent.
        outline.Color = color;

        // If the button has a fill palette, update the fill to track hover/active state too.
        if (demoButton.DefaultFillColor.A > 0)
        {
            outline.FillColor = demoButton.IsActive
                ? demoButton.ActiveFillColor
                : demoButton.IsHovered ? demoButton.HoveredFillColor : demoButton.DefaultFillColor;
        }

        if (demoButton.IsHovered && cursor.LeftButtonReleased)
        {
            World.Publish(new DemoButtonClicked(demoButton.Id));
        }
    }
}
