using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component.Draw;
using MonoDreams.Examples.Component.UI;
using MonoDreams.Examples.Message;
using MonoDreams.UI;
using MonoDreams.Examples.Screens;
using MonoDreams.State;

namespace MonoDreams.Examples.System.UI;

/// <summary>
/// The level-select menu's ACTION half: it turns "a level button was activated" into a
/// <see cref="ScreenTransitionRequest"/>, and paints each button's label from its hover/focus state.
///
/// <para><b>It performs no hit-test of its own.</b> Both halves come from
/// <see cref="UIFocusSystem"/> — the single owner of the pointer pick and of the click that follows
/// it: hover/focus is read off <see cref="FocusableComponent.IsFocused"/>, and the activation edge
/// arrives as the <see cref="UIFocusActivated"/> message (pointer release on a control, or
/// Enter/Space on the keyboard-focused one). This is the arbitration the ui premise "One click, one
/// owner: pointer input needs explicit arbitration between picking layers" asks for: a second
/// <c>Rectangle.Contains(cursor)</c> sweep here would be a second answer to the same click, and it
/// would silently disagree with the pick every hover consumer (the tooltip, the hand cursor) rides.
/// It is also what buys the menu its tooltips for free — a tooltip only shows for the picked
/// entity, and this system and the tooltip now agree on what "picked" means by construction.</para>
///
/// <para>The MECHANISM (focus, press, activation) is the <c>ui</c> module's; the ACTION — which
/// screen to load with which level — stays here, in game code, exactly as the ui premises specify.</para>
/// </summary>
[With(typeof(LevelSelector), typeof(FocusableComponent), typeof(SimpleButtonComponent))]
public class ButtonInteractionSystem : AEntitySetSystem<GameState>
{
    public ButtonInteractionSystem(World world) : base(world)
    {
        // Subscribes this instance's [Subscribe] methods (the engine's idiom — see TabSystem).
        world.Subscribe(this);
    }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var levelSelector = ref entity.Get<LevelSelector>();
        ref readonly var focusable = ref entity.Get<FocusableComponent>();
        ref readonly var button = ref entity.Get<SimpleButtonComponent>();

        // UIFocusSystem already resolved what the pointer is over (and what the keyboard focused);
        // this is just the game's reading of it.
        levelSelector.IsHovered = focusable.IsFocused && levelSelector.IsClickable;

        if (button.TextEntity is null) return;

        ref var text = ref button.TextEntity.Value.Get<DynamicTextComponent>();
        text.Color = !levelSelector.IsClickable ? levelSelector.DisabledColor
            : levelSelector.IsHovered ? levelSelector.HoveredColor
            : levelSelector.DefaultColor;
    }

    /// <summary>
    /// The click (or Enter/Space) that <see cref="UIFocusSystem"/> resolved. Routed by component
    /// rather than by <c>ButtonStateComponent.Id</c> because the menu's buttons already carry the
    /// data the action needs — the id seam is for screens whose activation is a string switch.
    /// </summary>
    [Subscribe]
    private void OnActivated(in UIFocusActivated msg)
    {
        if (!msg.Focused.IsAlive || !msg.Focused.Has<LevelSelector>()) return;

        ref readonly var levelSelector = ref msg.Focused.Get<LevelSelector>();
        if (!levelSelector.IsClickable) return;

        var targetScreen = string.IsNullOrEmpty(levelSelector.TargetScreen)
            ? ScreenName.Game
            : levelSelector.TargetScreen;
        World.Publish(new ScreenTransitionRequest(targetScreen, levelSelector.LevelName));
    }
}
