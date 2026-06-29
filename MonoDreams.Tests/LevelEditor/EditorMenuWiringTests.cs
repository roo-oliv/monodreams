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
/// Protects the editor-menu-wiring contract: the level-selection menu's per-level <b>Edit</b> button
/// publishes a <see cref="ScreenTransitionRequest"/> targeting <see cref="ScreenName.LevelEditor"/>
/// with that level's id — reusing the existing transition path (the handler then stashes a
/// <c>RequestedLevelComponent</c> and loads the editor screen, which boots with that level's content).
///
/// <para>Asserted at the message/handler level (no full UI render): a hand-built <c>LevelSelector</c>
/// button + a hovered, just-released cursor driven through the real <see cref="ButtonInteractionSystem"/>,
/// asserting the published message. This is the same generalized path the Play / InfiniteRunner buttons
/// use — only the button's <c>TargetScreen</c> data differs.</para>
/// </summary>
public class EditorMenuWiringTests
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
    public void EditButton_Publishes_LevelEditorTransition_WithLevelId()
    {
        using var world = new World();
        MakeButton(world, levelName: "Blender_Level", targetScreen: ScreenName.LevelEditor);
        MakeCursorOverButton(world);

        ScreenTransitionRequest? published = null;
        world.Subscribe((in ScreenTransitionRequest r) => published = r);

        using var system = new ButtonInteractionSystem(world);
        system.Update(new GameState(new GameTime()));

        Assert.NotNull(published);
        Assert.Equal(ScreenName.LevelEditor, published!.Value.ScreenName);
        Assert.Equal("Blender_Level", published.Value.LevelIdentifier);
    }

    [Fact]
    public void PlayButton_DefaultsToGameScreen_WhenTargetUnset()
    {
        // Sanity that the generalized resolution is unchanged: an empty TargetScreen still routes to Game.
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
}
