using System.Collections.Generic;
using System.Linq;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.Screen;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the pure <see cref="SceneCatalog"/> (UX-C §3.2): merging screen bindings + unclaimed
/// scene files into one ordered list, claiming, dangling backups, the unresolved degrade (screens
/// only), current-entry detection, and the pure dirty gate (<see cref="SceneCatalog.DecideSwitch"/>).
/// No world, no filesystem — the scene-id list is injected.
/// </summary>
public class SceneCatalogTests
{
    // The Examples shape: a bound menu, the level host, a bound runner (registration order).
    private static IReadOnlyList<(string, ScreenInfo)> Screens() => new[]
    {
        ("LevelSelection", new ScreenInfo("Level Selection", "level_selection")),
        ("Game", new ScreenInfo("Game", BoundSceneId: null, HostsSceneFiles: true)),
        ("InfiniteRunner", new ScreenInfo("Infinite Runner", "infinite_runner")),
    };

    [Fact]
    public void BoundScreensFirst_ThenUnclaimedFiles_HostedByTheHost()
    {
        var entries = SceneCatalog.Build(Screens(),
            sceneIds: new[] { "island", "cove", "level_selection" }, // level_selection is claimed by the menu
            currentScreenName: "Game", currentSceneId: "island", projectResolved: true);

        // Bound entries in registration order (Game has no BoundSceneId → skipped), then unclaimed
        // files ordinal-sorted.
        Assert.Equal(new[] { "level_selection", "infinite_runner", "cove", "island" },
            entries.Select(e => e.Key).ToArray());
        // The claimed id appears only once — as the bound-screen entry, not a duplicate file entry.
        Assert.Single(entries, e => e.Key == "level_selection");
        Assert.Equal("Level Selection", entries.First(e => e.Key == "level_selection").Label);
        // Files are attributed to the HostsSceneFiles screen; their label is the scene id.
        var island = entries.First(e => e.Key == "island");
        Assert.Equal("Game", island.ScreenName);
        Assert.Equal("island", island.Label);
    }

    [Fact]
    public void CurrentEntry_DetectedByScreenAndScene()
    {
        var onGame = SceneCatalog.Build(Screens(), new[] { "island" }, "Game", "island", true);
        Assert.True(onGame.Single(e => e.Key == "island").IsCurrent);
        Assert.False(onGame.Single(e => e.Key == "level_selection").IsCurrent);

        var onMenu = SceneCatalog.Build(Screens(), new[] { "island" }, "LevelSelection", "level_selection", true);
        Assert.True(onMenu.Single(e => e.Key == "level_selection").IsCurrent);
        Assert.False(onMenu.Single(e => e.Key == "island").IsCurrent);

        // A current scene with no matching entry (a brand-new untitled) marks nothing current.
        var none = SceneCatalog.Build(Screens(), new[] { "island" }, "Game", "untitled", true);
        Assert.DoesNotContain(none, e => e.IsCurrent);
    }

    [Fact]
    public void UnresolvedProject_IsScreensOnly()
    {
        var entries = SceneCatalog.Build(Screens(),
            sceneIds: new[] { "island", "cove" }, // ignored when unresolved
            currentScreenName: "Game", currentSceneId: null, projectResolved: false);
        Assert.Equal(new[] { "level_selection", "infinite_runner" }, entries.Select(e => e.Key).ToArray());
    }

    [Fact]
    public void DanglingBackupScene_ShowsUpAsAHostedEntry()
    {
        // A scene not tied to any binding "opens" for free by appearing under the host.
        var entries = SceneCatalog.Build(Screens(), new[] { "island-backup" }, "Game", "island", true);
        var backup = Assert.Single(entries, e => e.Key == "island-backup");
        Assert.Equal("Game", backup.ScreenName);
        Assert.Equal("island-backup", backup.Label);
    }

    [Fact]
    public void NoHostScreen_YieldsNoFileEntries()
    {
        var noHost = new[] { ("Menu", new ScreenInfo("Menu", "menu")) };
        var entries = SceneCatalog.Build(noHost, new[] { "island" }, "Menu", "menu", true);
        Assert.Single(entries); // only the bound Menu — the file has no screen to open on
        Assert.DoesNotContain(entries, e => e.Key == "island");
    }

    [Fact]
    public void AllPlainScreens_YieldEmptyCatalog()
    {
        // The Demos shape: no bound screens, no project → nothing to list.
        var demos = new[]
        {
            ("launcher", new ScreenInfo("Launcher")),
            ("camera", new ScreenInfo("Camera Demo")),
        };
        Assert.Empty(SceneCatalog.Build(demos, new[] { "x" }, "launcher", null, projectResolved: false));
    }

    [Theory]
    [InlineData(true, true, SceneSwitchDecision.NoOp)]    // current entry → no-op regardless of dirty
    [InlineData(true, false, SceneSwitchDecision.NoOp)]
    [InlineData(false, false, SceneSwitchDecision.Switch)] // not current + clean → switch immediately
    [InlineData(false, true, SceneSwitchDecision.Confirm)] // not current + dirty → confirm first
    public void DecideSwitch_TruthTable(bool isCurrent, bool isDirty, SceneSwitchDecision expected)
    {
        var entry = new SceneCatalogEntry("k", "L", "S", "k", isCurrent);
        Assert.Equal(expected, SceneCatalog.DecideSwitch(entry, isDirty));
    }
}
