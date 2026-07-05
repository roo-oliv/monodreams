using System;
using System.Collections.Generic;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Serialization;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects PS4 <b>manifest-driven boot</b>: the game reads the bundled <c>game.mdproj</c> and boots its
/// <c>startScene</c> — but only when that start scene resolves to a bundled native <c>.mdscene</c>
/// (otherwise the caller keeps its default boot, so a not-yet-migrated <c>startScene</c> is back-compat).
/// Pure — injected reader/probe, no window, no disk.
///
/// Covers the level-editor premise "The game boots native scenes native-first via LoadLevelRequest"
/// (the manifest-boot half).
/// </summary>
public class ManifestBootTests
{
    // ---- ResolveStartScene: the three-part guard ----

    [Fact]
    public void ResolveStartScene_ReturnsStartScene_WhenNativeSceneExists()
    {
        var manifest = new GameProject { StartScene = "island" };
        Assert.Equal("island", ManifestBoot.ResolveStartScene(manifest, id => id == "island"));
    }

    [Fact]
    public void ResolveStartScene_ReturnsNull_WhenStartSceneHasNoNativeFileYet()
    {
        // The Examples case today: startScene="island" but island.mdscene is not committed until PS5.
        var manifest = new GameProject { StartScene = "island" };
        Assert.Null(ManifestBoot.ResolveStartScene(manifest, _ => false));
    }

    [Fact]
    public void ResolveStartScene_ReturnsNull_WhenNoManifest()
    {
        Assert.Null(ManifestBoot.ResolveStartScene(null, _ => true));
    }

    [Fact]
    public void ResolveStartScene_ReturnsNull_WhenStartSceneEmpty()
    {
        Assert.Null(ManifestBoot.ResolveStartScene(new GameProject { StartScene = "" }, _ => true));
    }

    // ---- TryReadManifest: parses via CanonicalJson; never throws ----

    [Fact]
    public void TryReadManifest_ParsesBundledManifest_ViaInjectedReader()
    {
        var manifest = new GameProject { StartScene = "island", LevelsDir = "Levels", AssetRoots = new[] { "Island" } };
        var json = CanonicalJson.Serialize(manifest);
        var files = new Dictionary<string, string> { ["Content/game.mdproj"] = json };

        var read = ManifestBoot.TryReadManifest("Content", path => files.TryGetValue(path, out var v) ? v : null);

        Assert.NotNull(read);
        Assert.Equal("island", read!.StartScene);
        Assert.Equal("Levels", read.LevelsDir);
    }

    [Fact]
    public void TryReadManifest_ReturnsNull_WhenAbsent()
    {
        Assert.Null(ManifestBoot.TryReadManifest("Content", _ => null));
    }

    [Fact]
    public void TryReadManifest_ReturnsNull_WhenMalformed_NeverThrows()
    {
        Assert.Null(ManifestBoot.TryReadManifest("Content", _ => "{ not valid json"));
    }

    // ---- Path helpers ----

    [Fact]
    public void NativeLevelLoader_ContentRelativePath_UsesLevelsDirAndExtension()
    {
        Assert.Equal(global::System.IO.Path.Combine("Levels", "island.mdscene"),
            NativeLevelLoader.ContentRelativePath("island"));
    }
}
