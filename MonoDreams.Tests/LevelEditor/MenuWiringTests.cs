using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Cursor;
using MonoDreams.Component.Draw;
using MonoDreams.Examples.Component.UI;
using MonoDreams.Examples.Message;
using MonoDreams.Examples.Screens;
using MonoDreams.Examples.System.UI;
using MonoDreams.State;
using MonoDreams.UI;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the menu-wiring contract after the entry-path removal: the level-selection menu has
/// ONLY Play buttons (a level button publishes a <see cref="ScreenTransitionRequest"/> for the
/// game screen; the runner button routes via its <c>TargetScreen</c>) — the editor is entered
/// exclusively through the <c>--editor</c> / <c>MONODREAMS_EDITOR=1</c> run configuration and
/// driven by the transport (see <c>EditorTransportTests</c>). The per-level "Edit" buttons and
/// <c>ScreenName.LevelEditor</c> are gone; their absence is compile-enforced (the constant no
/// longer exists), and this file keeps the generalized transition path covered.
/// </summary>
public class MenuWiringTests
{
    private static Entity MakeButton(World world, string levelName, string targetScreen)
    {
        var transform = new TransformComponent(Vector2.Zero);
        var button = world.CreateEntity();
        button.Set(transform);
        button.Set(new SimpleButtonComponent { Size = new Vector2(100, 40), Target = RenderTargetID.Main });
        button.Set(new LevelSelector
        {
            LevelName = levelName,
            TargetScreen = targetScreen,
            IsClickable = true,
            IsHovered = false,
        });
        return button;
    }

    private static Entity MakeCursorOverButton(World world)
    {
        var cursor = world.CreateEntity();
        cursor.Set(new CursorControllerComponent(CursorType.Default));
        cursor.Set(new CursorInputComponent
        {
            WorldPosition = new Vector2(10, 10), // inside the 100×40 button at origin
            LeftButtonReleased = true,           // the click fires on release
        });
        return cursor;
    }

    [Fact]
    public void PlayButton_DefaultsToGameScreen_WhenTargetUnset()
    {
        using var world = new World();
        MakeButton(world, levelName: "Level_0", targetScreen: null);
        MakeCursorOverButton(world);

        ScreenTransitionRequest? published = null;
        world.Subscribe((in ScreenTransitionRequest r) => published = r);

        using var system = new ButtonInteractionSystem(world);
        system.Update(new GameState(new GameTime()));

        Assert.NotNull(published);
        Assert.Equal(ScreenName.Game, published!.Value.ScreenName);
        Assert.Equal("Level_0", published.Value.LevelIdentifier);
    }

    [Fact]
    public void Button_WithATargetScreen_RoutesThere()
    {
        // The same generalized resolution the removed Edit buttons used to piggyback on: only the
        // TargetScreen data differs (the runner button is the remaining user).
        using var world = new World();
        MakeButton(world, levelName: null, targetScreen: ScreenName.InfiniteRunner);
        MakeCursorOverButton(world);

        ScreenTransitionRequest? published = null;
        world.Subscribe((in ScreenTransitionRequest r) => published = r);

        using var system = new ButtonInteractionSystem(world);
        system.Update(new GameState(new GameTime()));

        Assert.NotNull(published);
        Assert.Equal(ScreenName.InfiniteRunner, published!.Value.ScreenName);
    }
}
