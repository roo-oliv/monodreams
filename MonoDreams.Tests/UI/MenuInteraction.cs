using DefaultEcs;
using MonoDreams.Examples.System.UI;
using MonoDreams.Input;
using MonoDreams.State;
using MonoDreams.UI;

namespace MonoDreams.Tests.Ui;

/// <summary>
/// The level-selection menu's interaction pair, exactly as <c>LevelSelectionScreen</c> composes it:
/// <see cref="UIFocusSystem"/> (the ONE pointer pick, the hover/focus flags and the
/// <c>UIFocusActivated</c> edge) followed by the game's <see cref="ButtonInteractionSystem"/> (the
/// action). A test that drives a menu button must drive BOTH — since issue #115 the game system
/// runs no hit-test of its own, which is the point of the ui premise "One click, one owner".
/// </summary>
internal static class MenuInteraction
{
    /// <summary>A nav action nothing maps: these tests drive the POINTER. In the real screen the
    /// menu's keyboard navigation comes from the game's own <c>InputMappingSystem</c>.</summary>
    private sealed class Unbound : AInputState { }

    /// <summary>The focus system with unbound keyboard navigation.</summary>
    public static UIFocusSystem Focus(World world) => new(
        world, new Unbound(), new Unbound(), new Unbound(), new Unbound(),
        new Unbound(), new Unbound(), new Unbound());

    /// <summary>One frame of the pair, in pipeline order.</summary>
    public static void Tick(UIFocusSystem focus, ButtonInteractionSystem buttons, GameState state)
    {
        focus.Update(state);
        buttons.Update(state);
    }
}
