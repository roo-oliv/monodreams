using System;
using MonoDreams.LevelEditor.Serialization;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the PS2 project manifest (<see cref="GameProject"/> / <c>game.mdproj</c>): it round-trips
/// through the SAME canonical serializer scenes use (<see cref="CanonicalJson"/>), so the manifest is
/// byte-stable and diffable, and <c>assetRoots</c> preserves its authored order (it is a plain array,
/// not subject to the sorted-map converter). Pure — no window, no disk.
/// </summary>
public class GameProjectTests
{
    [Fact]
    public void CanonicalRoundTrip_PreservesFieldsAndAssetRootOrder()
    {
        var project = new GameProject
        {
            FormatVersion = 1,
            StartScene = "island",
            LevelsDir = "Levels",
            AssetRoots = new[] { "Island", "Atlas", "Objects" },
        };

        var json = CanonicalJson.Serialize(project);
        var back = CanonicalJson.Deserialize<GameProject>(json);

        Assert.NotNull(back);
        Assert.Equal(1, back!.FormatVersion);
        Assert.Equal("island", back.StartScene);
        Assert.Equal("Levels", back.LevelsDir);
        Assert.Equal(new[] { "Island", "Atlas", "Objects" }, back.AssetRoots); // order preserved
    }

    [Fact]
    public void Serialize_SameProjectTwice_IsByteIdentical()
    {
        var project = new GameProject
        {
            StartScene = "island",
            AssetRoots = new[] { "Island", "Atlas", "Objects" },
        };
        Assert.Equal(CanonicalJson.Serialize(project), CanonicalJson.Serialize(project));
    }

    [Fact]
    public void AssetRoots_AreNotReordered_AuthoredOrderWins()
    {
        // A deliberately non-alphabetical order must survive: arrays are not the sorted-map
        // converter's business (that only sorts Dictionary<string,_> keys).
        var project = new GameProject { AssetRoots = new[] { "Objects", "Island", "Atlas" } };

        var json = CanonicalJson.Serialize(project);
        var back = CanonicalJson.Deserialize<GameProject>(json);

        Assert.Equal(new[] { "Objects", "Island", "Atlas" }, back!.AssetRoots);
        var iObjects = json.IndexOf("Objects", StringComparison.Ordinal);
        var iIsland = json.IndexOf("Island", StringComparison.Ordinal);
        var iAtlas = json.IndexOf("Atlas", StringComparison.Ordinal);
        Assert.True(iObjects < iIsland && iIsland < iAtlas, "assetRoots must serialize in authored order");
    }

    /// <summary>
    /// Locks the exact canonical bytes for the example manifest — the same shape the committed
    /// <c>MonoDreams.Examples.Core/Content/game.mdproj</c> is hand-authored to, so a load → save is a
    /// byte fixed point (2-space indent, LF, declaration-order fields, trailing newline).
    /// </summary>
    [Fact]
    public void Serialize_MatchesTheCanonicalShape()
    {
        var project = new GameProject
        {
            FormatVersion = 1,
            StartScene = "island",
            LevelsDir = "Levels",
            AssetRoots = new[] { "Island", "Atlas", "Objects" },
        };

        var expected =
            "{\n" +
            "  \"formatVersion\": 1,\n" +
            "  \"startScene\": \"island\",\n" +
            "  \"levelsDir\": \"Levels\",\n" +
            "  \"assetRoots\": [\n" +
            "    \"Island\",\n" +
            "    \"Atlas\",\n" +
            "    \"Objects\"\n" +
            "  ]\n" +
            "}\n";

        Assert.Equal(expected, CanonicalJson.Serialize(project));
    }
}
