#nullable enable
using System;
using System.IO;
using MonoDreams.LevelEditor.Assets;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the per-asset band marks (FW3, island-authoring §3.3): a mark set on a catalog entry is
/// persisted into <c>asset-bands.json</c> in the drop folder and <b>survives an editor restart</b>
/// (a fresh <see cref="AssetBandConfig.Load"/> still resolves it). The config round-trips through the
/// canonical byte-stable JSON policy, and a directly-constructed (rootless) config keeps its marks in
/// memory with a loud no-op <see cref="AssetBandConfig.Save"/>. Tiny on-disk fixtures in a per-test
/// temp dir — no GraphicsDevice, no downloaded packs.
/// </summary>
public class AssetBandConfigTests
{
    private sealed class TempDir : IDisposable
    {
        public string Root { get; } =
            Path.Combine(Path.GetTempPath(), "monodreams-asset-bands-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Root);
        public string ConfigPath => Path.Combine(Root, AssetBandConfig.FileName);
        public void Dispose() { try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ } }
    }

    [Fact]
    public void SetBand_PersistsAndSurvivesReload()
    {
        using var dir = new TempDir();

        // A fresh drop folder has no marks.
        var config = AssetBandConfig.Load(dir.Root);
        Assert.True(config.CanPersist);
        Assert.False(config.TryGetBand("Island/props/tree01.png", out _));

        // Mark an entry → the config file appears on disk...
        config.SetBand("Island/props/tree01.png", "Ground");
        Assert.True(File.Exists(dir.ConfigPath));

        // ...and a FRESH load (the "restart") still resolves it. An unmarked entry stays unmarked.
        var reloaded = AssetBandConfig.Load(dir.Root);
        Assert.True(reloaded.TryGetBand("Island/props/tree01.png", out var band));
        Assert.Equal("Ground", band);
        Assert.False(reloaded.TryGetBand("Island/props/stone.png", out _));
    }

    [Fact]
    public void ClearBand_RemovesTheMark_Persisting()
    {
        using var dir = new TempDir();
        var config = AssetBandConfig.Load(dir.Root);
        config.SetBand("Island/props/tree01.png", "Props");
        config.ClearBand("Island/props/tree01.png");

        var reloaded = AssetBandConfig.Load(dir.Root);
        Assert.False(reloaded.TryGetBand("Island/props/tree01.png", out _));
    }

    [Fact]
    public void Config_RoundTripsCanonicalBytes()
    {
        using var dir = new TempDir();
        var config = AssetBandConfig.Load(dir.Root);
        config.SetBand("Island/props/tree01.png", "Props");
        config.SetBand("Island/ground/grass.png", "Ground");

        var firstBytes = File.ReadAllText(dir.ConfigPath);

        // Load → save again is byte-identical (canonical, ordinal-key-sorted).
        var reloaded = AssetBandConfig.Load(dir.Root);
        reloaded.Save();
        Assert.Equal(firstBytes, File.ReadAllText(dir.ConfigPath));
        // Ordinal key order: "Island/ground/..." sorts before "Island/props/...".
        Assert.True(firstBytes.IndexOf("ground", StringComparison.Ordinal)
                    < firstBytes.IndexOf("props", StringComparison.Ordinal));
    }

    [Fact]
    public void MalformedConfig_FallsBackToEmpty_NoThrow()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.ConfigPath, "{ not json !!");

        var config = AssetBandConfig.Load(dir.Root); // loud fallback, no crash
        Assert.Empty(config.MarkedIds);
        Assert.True(config.CanPersist);
    }

    [Fact]
    public void InMemoryConfig_KeepsMarks_ButCannotPersist()
    {
        var config = new AssetBandConfig();
        Assert.False(config.CanPersist);

        config.SetBand("Island/props/tree01.png", "Ground"); // Save is a loud no-op
        Assert.True(config.TryGetBand("Island/props/tree01.png", out var band));
        Assert.Equal("Ground", band);
    }
}
