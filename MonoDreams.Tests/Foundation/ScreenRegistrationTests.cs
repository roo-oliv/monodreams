using System;
using MonoDreams.Screen;
using Xunit;

namespace MonoDreams.Tests.Foundation;

/// <summary>
/// Protects the foundation seam UX-C added: <see cref="ScreenController.RegisterScreen(string,Func{IGameScreen},ScreenInfo)"/>
/// + <see cref="ScreenController.RegisteredScreens"/>. The controller's other collaborators
/// (Game / renderer / camera / …) are never touched by registration, so the tests construct it with
/// nulls and never invoke a creator — registration + enumeration is pure bookkeeping.
/// </summary>
public class ScreenRegistrationTests
{
    private static ScreenController NewController() => new(null!, null!, null!, null!, null!, null!);

    [Fact]
    public void DefaultOverload_RecordsDefaultInfo()
    {
        var c = NewController();
        c.RegisterScreen("Game", () => null!);

        var entry = Assert.Single(c.RegisteredScreens);
        Assert.Equal("Game", entry.Name);
        Assert.Equal("Game", entry.Info.DisplayName); // display name defaults to the screen name
        Assert.Null(entry.Info.BoundSceneId);
        Assert.False(entry.Info.HostsSceneFiles);
    }

    [Fact]
    public void ExplicitInfo_IsEnumeratedInRegistrationOrder()
    {
        var c = NewController();
        c.RegisterScreen("Menu", () => null!, new ScreenInfo("Level Selection", "level_selection"));
        c.RegisterScreen("Game", () => null!, new ScreenInfo("Game", BoundSceneId: null, HostsSceneFiles: true));
        c.RegisterScreen("Runner", () => null!, new ScreenInfo("Infinite Runner", "infinite_runner"));

        Assert.Collection(c.RegisteredScreens,
            e => { Assert.Equal("Menu", e.Name); Assert.Equal("Level Selection", e.Info.DisplayName); Assert.Equal("level_selection", e.Info.BoundSceneId); },
            e => { Assert.Equal("Game", e.Name); Assert.Null(e.Info.BoundSceneId); Assert.True(e.Info.HostsSceneFiles); },
            e => { Assert.Equal("Runner", e.Name); Assert.Equal("infinite_runner", e.Info.BoundSceneId); });
    }

    [Fact]
    public void DuplicateName_Throws_ForEitherOverload()
    {
        var c = NewController();
        c.RegisterScreen("Game", () => null!);
        Assert.Throws<ArgumentException>(() => c.RegisterScreen("Game", () => null!));
        Assert.Throws<ArgumentException>(() => c.RegisterScreen("Game", () => null!, new ScreenInfo("Game 2")));
        Assert.Single(c.RegisteredScreens); // the failed adds left the registry unchanged
    }

    [Fact]
    public void RegisteredScreens_IsEmptyBeforeAnyRegistration()
    {
        Assert.Empty(NewController().RegisteredScreens);
    }
}
