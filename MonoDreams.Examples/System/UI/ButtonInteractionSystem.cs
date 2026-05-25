using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Cursor;
using MonoDreams.Examples.Component.UI;
using MonoDreams.Examples.Message;
using MonoDreams.UI;
using MonoDreams.Examples.Screens;
using MonoDreams.State;

namespace MonoDreams.Examples.System.UI;

/// <summary>
/// Handles button hover detection and click interactions for level selector buttons.
/// </summary>
[With(typeof(LevelSelector), typeof(TransformComponent), typeof(SimpleButtonComponent))]
public class ButtonInteractionSystem(World world) : AEntitySetSystem<GameState>(world)
{
    private EntitySet _cursorEntities = world.GetEntities().With<CursorInputComponent>().AsSet();

    protected override void Update(GameState state, in Entity entity)
    {
        ref var levelSelector = ref entity.Get<LevelSelector>();
        ref readonly var transform = ref entity.Get<TransformComponent>();
        ref readonly var button = ref entity.Get<SimpleButtonComponent>();

        // Get cursor input
        var entities = _cursorEntities.GetEntities();
        if (entities.Length == 0) return;

        var cursorEntity = entities[0];
        if (!cursorEntity.Has<CursorInputComponent>()) return;

        ref readonly var cursor = ref cursorEntity.Get<CursorInputComponent>();

        var buttonBounds = new Rectangle(
            (int)transform.WorldPosition.X,
            (int)transform.WorldPosition.Y,
            (int)button.Size.X,
            (int)button.Size.Y);

        bool isMouseOver = buttonBounds.Contains(cursor.WorldPosition);

        // Update hover state
        levelSelector.IsHovered = isMouseOver && levelSelector.IsClickable;

        // Update text color based on state
        if (button.TextEntity is not null)
        {
            ref var text = ref button.TextEntity.Value.Get<DynamicTextComponent>();

            if (!levelSelector.IsClickable)
            {
                text.Color = levelSelector.DisabledColor;
            }
            else if (levelSelector.IsHovered)
            {
                text.Color = levelSelector.HoveredColor;
            }
            else
            {
                text.Color = levelSelector.DefaultColor;
            }
        }

        // Handle click - request screen transition to target screen (or Game by default) with level
        if (levelSelector.IsHovered && cursor.LeftButtonReleased)
        {
            var targetScreen = string.IsNullOrEmpty(levelSelector.TargetScreen)
                ? ScreenName.Game
                : levelSelector.TargetScreen;
            World.Publish(new ScreenTransitionRequest(targetScreen, levelSelector.LevelName));
        }
    }
}
