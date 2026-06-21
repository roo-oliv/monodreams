using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.UI;

/// <summary>
/// Resolves a button's per-state look and drives its press animation. For every entity carrying
/// both a <see cref="SimpleButtonComponent"/> and a <see cref="ButtonStateComponent"/> it picks
/// colors from the <see cref="ButtonTheme"/> based on disabled / pressed / focused (hover or
/// keyboard focus, read from an optional <see cref="FocusableComponent"/>) / normal, writes them
/// onto the button outline + fill and the linked label text, and eases
/// <see cref="ButtonStateComponent.VisualScale"/> toward the press target so
/// <c>ButtonMeshPrepSystem</c> renders a subtle "pop". The gold focus ring follows a
/// <c>:focus-visible</c> model — it shows for an active control or KEYBOARD focus
/// (<c>IsFocused &amp;&amp; FocusVisible</c>), while a mouse hover changes only the background fill.
///
/// <para>This is the visual half of the module's button support; <see cref="UIFocusSystem"/> is
/// the input half. Together they generalize what each game previously wrote by hand (the demos'
/// <c>DemoButtonInteractionSystem</c>), with the action still dispatched by the game via
/// <see cref="UIFocusActivated"/>.</para>
/// </summary>
[With(typeof(SimpleButtonComponent), typeof(ButtonStateComponent))]
public sealed class ButtonVisualSystem(World world, ButtonTheme theme, float animSpeed = 18f)
    : AEntitySetSystem<GameState>(world)
{
    protected override void Update(GameState state, in Entity entity)
    {
        ref var bs = ref entity.Get<ButtonStateComponent>();
        ref var btn = ref entity.Get<SimpleButtonComponent>();

        var hasFocusable = entity.Has<FocusableComponent>();
        var focused = hasFocusable && entity.Get<FocusableComponent>().IsFocused;
        var focusVisible = hasFocusable && entity.Get<FocusableComponent>().FocusVisible;

        // :focus-visible model. The background HOVER FILL shows whenever the control is focused
        // (pointer hover OR keyboard) or active — hover should look interactive. The gold FOCUS RING,
        // however, shows only for an active control (e.g. the current tab) or KEYBOARD focus
        // (IsFocused && FocusVisible) — a mouse hover must NOT add the ring.
        var highlighted = focused || bs.IsActive;          // drives the hover fill
        var ringed = bs.IsActive || (focused && focusVisible); // drives the gold focus ring

        var palette = theme.For(bs.Variant);
        var colors = bs.IsDisabled ? palette.Disabled
            : bs.IsPressed ? palette.Pressed
            : highlighted ? palette.Hover
            : palette.Normal;

        btn.FillColor = colors.Fill;

        // The gold focus ring is drawn on every variant (even Link, which has no outline at rest) so
        // keyboard focus / active selection is always visible; otherwise the variant's own outline
        // shows (Secondary draws a border; the rest draw none). Pointer hover lands here (no ring),
        // changing only the fill above.
        var baseBorder = bs.Variant == ButtonVariant.Secondary ? 2f : 0f;
        if (ringed && !bs.IsDisabled)
        {
            btn.LineThickness = theme.FocusRingThickness;
            btn.Color = theme.FocusRingColor;
        }
        else
        {
            btn.LineThickness = baseBorder;
            btn.Color = colors.Outline;
        }

        if (btn.TextEntity is { } text && text.IsAlive && text.Has<DynamicTextComponent>())
            text.Get<DynamicTextComponent>().Color = colors.Text;

        // Ease the press "pop": shrink slightly while held, spring back to full size otherwise.
        var current = bs.VisualScale <= 0f ? 1f : bs.VisualScale;
        var target = !bs.IsDisabled && bs.IsPressed ? 0.94f : 1f;
        current = MathHelper.Lerp(current, target, MathHelper.Clamp(animSpeed * state.Time, 0f, 1f));
        bs.VisualScale = current;
        btn.VisualScale = current;
    }
}
