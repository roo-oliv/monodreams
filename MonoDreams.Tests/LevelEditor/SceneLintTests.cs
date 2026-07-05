using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MonoDreams.LevelEditor.Serialization;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the PS6 <b>ship-readiness lint</b> (project-persistence plan §7): a scene is
/// "ship-ready / fully portable" iff it has ZERO <c>file:</c> AssetKeys (all graduated to MGCB
/// content keys). Two halves: (1) the pure analyzer <see cref="SceneLint"/> flags <c>file:</c> keys
/// and passes on content-key-only scenes; (2) a scan of the committed
/// <c>MonoDreams.Examples.Core/Content/Levels/**/*.mdscene</c> asserts every shipped level is
/// ship-clean (Blender_Level + sample use only content-key AssetKeys — PS5).
///
/// Pure logic — hand-built <see cref="SceneData"/> and committed source files; no world, no
/// <c>GraphicsDevice</c>, no PlatformServices mutation. Covers the level-editor premise
/// "A scene is ship-ready when it has zero file: AssetKeys".
/// </summary>
public class SceneLintTests
{
    /// <summary>Builds a one-entity scene whose <c>core.SpriteInfo</c> carries <paramref name="assetKey"/>.</summary>
    private static SceneData SceneWithSpriteKey(string assetKey) => new()
    {
        Entities =
        {
            new SceneEntityData
            {
                Id = 0,
                Components =
                {
                    ["core.SpriteInfo"] = CanonicalJson.SerializeToElement(new { assetKey, source = new[] { 0, 0, 16, 16 } }),
                    ["core.Transform"] = CanonicalJson.SerializeToElement(new { position = new[] { 0f, 0f } }),
                },
            },
        },
    };

    [Fact]
    public void FindFileAssetKeys_FlagsTheFileScheme_WithEntityAndComponentContext()
    {
        var scene = SceneWithSpriteKey("file:Island/props/tree01.png");

        var findings = SceneLint.FindFileAssetKeys(scene);

        var finding = Assert.Single(findings);
        Assert.Equal(0, finding.EntityIndex);
        Assert.Equal("core.SpriteInfo", finding.ComponentKey);
        Assert.Equal("file:Island/props/tree01.png", finding.AssetKey);
        Assert.False(SceneLint.IsShipReady(scene));
    }

    [Fact]
    public void IsShipReady_TrueForContentKeyOnly_FalseForAnyFileKey()
    {
        Assert.True(SceneLint.IsShipReady(SceneWithSpriteKey("Atlas/TX Player")));
        Assert.False(SceneLint.IsShipReady(SceneWithSpriteKey("file:Island/props/tree01.png")));
    }

    [Fact]
    public void FindFileAssetKeys_EmptyOrNullScene_IsShipReady()
    {
        Assert.Empty(SceneLint.FindFileAssetKeys(new SceneData()));
        Assert.Empty(SceneLint.FindFileAssetKeys(null));
        Assert.True(SceneLint.IsShipReady(new SceneData()));
    }

    [Fact]
    public void FindFileAssetKeys_ScansNestedArraysAndObjects()
    {
        // A file: key buried in a nested array/object is still found (the walk is recursive, so the
        // lint catches any future file-scheme reference, not only the top-level assetKey field).
        var scene = new SceneData
        {
            Entities =
            {
                new SceneEntityData
                {
                    Id = 0,
                    Components =
                    {
                        ["game.Custom"] = CanonicalJson.SerializeToElement(new
                        {
                            nested = new { refs = new[] { "content:ok", "file:Island/nested.png" } },
                        }),
                    },
                },
            },
        };

        var finding = Assert.Single(SceneLint.FindFileAssetKeys(scene));
        Assert.Equal("file:Island/nested.png", finding.AssetKey);
    }

    // ---- The committed Examples levels are ship-clean (zero file: keys) ----

    [Fact]
    public void AllCommittedExamplesLevels_AreShipClean_ZeroFileKeys()
    {
        var levelsDir = RepoPath("MonoDreams.Examples.Core/Content/Levels");
        var files = Directory.GetFiles(levelsDir, "*.mdscene", SearchOption.AllDirectories);
        Assert.NotEmpty(files); // there IS at least one committed level to check (sample + Blender_Level)

        var offenders = new List<string>();
        foreach (var file in files)
        {
            var scene = CanonicalJson.Deserialize<SceneData>(File.ReadAllText(file));
            var findings = SceneLint.FindFileAssetKeys(scene);
            if (findings.Count > 0)
                offenders.Add($"{Path.GetFileName(file)}: {string.Join(", ", findings.Select(f => f.AssetKey))}");
        }

        Assert.True(offenders.Count == 0,
            "Committed levels must be ship-clean (zero file: keys). Offenders: " + string.Join(" | ", offenders));
    }

    /// <summary>Resolves a repo-relative path by walking up from the test base dir to the repo root
    /// (the directory containing <c>MonoDreams.Examples.Core</c>).</summary>
    private static string RepoPath(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "MonoDreams.Examples.Core")))
            dir = Directory.GetParent(dir)?.FullName;
        Assert.NotNull(dir);
        return Path.Combine(dir!, relative.Replace('/', Path.DirectorySeparatorChar));
    }
}
