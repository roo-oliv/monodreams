using MonoDreams.Examples.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.Screen;

namespace MonoDreams.Examples.Screens;

/// <summary>
/// The Examples switch hand-off the editor's Scenes panel drives (UX-C): it reuses the game's existing
/// <see cref="RequestedLevelComponent"/> + <see cref="ScreenController.LoadScreen"/> path, so the editor
/// module never references a game screen type — the callback is the seam, exactly like
/// <c>EditorTransport.Reload</c>. The requested level is set <b>only</b> for the level-parameterized host
/// (the Game screen, which reads it in <c>Load</c>); a bound screen (menu / runner) loads its own scene
/// via its optional-scene-load in <c>Load</c>, so it needs no requested level. Any stale
/// <see cref="RequestedLevelComponent"/> is cleared first so the menu's own <c>AddService</c> (which does
/// not remove-first) can never double-register.
/// </summary>
public static class EditorSceneSwitch
{
    public static void Switch(ScreenController screenController, SceneCatalogEntry entry)
    {
        var services = screenController.Game.Services;
        services.RemoveService(typeof(RequestedLevelComponent));
        if (entry.ScreenName == ScreenName.Game)
            services.AddService(new RequestedLevelComponent(entry.SceneId));
        screenController.LoadScreen(entry.ScreenName);
    }
}
