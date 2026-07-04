#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using MonoDreams.LevelEditor.Assets;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the island-authoring Slice 1 asset intake (plan §2): the drop-folder catalog scan
/// (recursive PNGs + <c>*.slices.json</c> sidecar regions; scan reads only the directory + the
/// sidecars — no PNG is ever opened), the <see cref="FileAssetKey"/> <c>file:</c> scheme, and the
/// <see cref="FileAssetTextureLoader"/> contract: lazy + memoized loads, and missing file =
/// recorded + the shared placeholder (never an invisible sprite). All disk fixtures are tiny
/// generated files in a per-test temp directory — no dependence on downloaded (gitignored,
/// license-encumbered) packs; no GraphicsDevice (the loader's decode/placeholder functions are
/// the injectable test seam).
/// </summary>
public class AssetCatalogTests
{
    /// <summary>A throwaway on-disk fixture folder; PNG contents are irrelevant to the scan.</summary>
    private sealed class TempAssetDir : IDisposable
    {
        public string Root { get; } =
            Path.Combine(Path.GetTempPath(), "monodreams-asset-catalog-" + Guid.NewGuid().ToString("N"));

        public void AddFile(string relativePath, string contents = "png-bytes")
        {
            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
        }
    }

    // ---- Catalog scan: folders + sidecar regions; deterministic order; whole-PNG fallback ----

    [Fact]
    public void CatalogScanTest_FoldersAndSidecarRegions()
    {
        using var dir = new TempAssetDir();
        dir.AddFile("props/tree01.png");
        dir.AddFile("ground/grass.png");
        dir.AddFile("props/sheet.png");
        dir.AddFile("props/sheet.png" + AssetCatalog.SliceSidecarSuffix,
            """{ "regions": [ { "name": "trunk", "x": 0, "y": 0, "w": 32, "h": 48 }, { "name": "crown", "x": 32, "y": 0, "w": 48, "h": 48 } ] }""");
        dir.AddFile("notes.txt"); // non-PNG: ignored

        var catalog = AssetCatalog.Scan(dir.Root, "Island");

        // 2 whole PNGs + 2 sheet regions (the sliced sheet itself is NOT an entry).
        Assert.Equal(4, catalog.Entries.Count);
        Assert.DoesNotContain(catalog.Entries, e => e.RegionName == null && e.RelativePath.EndsWith("sheet.png"));

        var tree = catalog.Entries.Single(e => e.Label == "tree01");
        Assert.Equal("Island/props/tree01.png", tree.RelativePath);
        Assert.Equal("props", tree.Folder);
        Assert.Null(tree.Region);
        Assert.Equal("file:Island/props/tree01.png", tree.AssetKey);

        var grass = catalog.Entries.Single(e => e.Label == "grass");
        Assert.Equal("ground", grass.Folder);

        var trunk = catalog.Entries.Single(e => e.RegionName == "trunk");
        Assert.Equal(new Rectangle(0, 0, 32, 48), trunk.Region);
        Assert.Equal("file:Island/props/sheet.png#trunk", trunk.AssetKey);
        Assert.Equal("Island/props/sheet.png#trunk", trunk.Id);

        // Deterministic order: folder first (ground < props), then label.
        Assert.Equal(new[] { "grass", "sheet#crown", "sheet#trunk", "tree01" },
            catalog.Entries.Select(e => e.Label).ToArray());

        // Lookup accepts both the bare id and the full file: key.
        Assert.True(catalog.TryGet("Island/props/tree01.png", out var byId));
        Assert.Same(tree, byId);
        Assert.True(catalog.TryGet("file:Island/props/sheet.png#trunk", out var byKey));
        Assert.Same(trunk, byKey);
        Assert.False(catalog.TryGet("Island/props/nope.png", out _));
    }

    [Fact]
    public void CatalogScanTest_MissingRootYieldsEmptyCatalog()
    {
        var catalog = AssetCatalog.Scan(
            Path.Combine(Path.GetTempPath(), "monodreams-does-not-exist-" + Guid.NewGuid().ToString("N")),
            "Island");
        Assert.Empty(catalog.Entries);
    }

    [Fact]
    public void CatalogScanTest_MalformedSidecarFallsBackToWholePng()
    {
        using var dir = new TempAssetDir();
        dir.AddFile("props/sheet.png");
        dir.AddFile("props/sheet.png" + AssetCatalog.SliceSidecarSuffix, "{ not json !!");

        var catalog = AssetCatalog.Scan(dir.Root, "Island");

        // Loud fallback: the sheet is still placeable as one whole-PNG entry.
        var entry = Assert.Single(catalog.Entries);
        Assert.Null(entry.RegionName);
        Assert.Equal("Island/props/sheet.png", entry.RelativePath);
    }

    // ---- file: AssetKey scheme ----

    [Fact]
    public void FileAssetKeyTest_ComposeParseRoundTrip()
    {
        Assert.Equal("file:Island/props/tree01.png", FileAssetKey.Compose("Island\\props\\tree01.png"));
        Assert.Equal("file:Island/props/sheet.png#trunk", FileAssetKey.Compose("Island/props/sheet.png", "trunk"));

        Assert.True(FileAssetKey.IsFileKey("file:Island/a.png"));
        Assert.False(FileAssetKey.IsFileKey("Atlas/TX Player")); // a content key
        Assert.False(FileAssetKey.IsFileKey(null));

        Assert.True(FileAssetKey.TryParse("file:Island/props/sheet.png#trunk", out var path, out var region));
        Assert.Equal("Island/props/sheet.png", path);
        Assert.Equal("trunk", region);

        Assert.True(FileAssetKey.TryParse("file:Island/props/tree01.png", out path, out region));
        Assert.Equal("Island/props/tree01.png", path);
        Assert.Null(region);

        Assert.False(FileAssetKey.TryParse("Atlas/TX Player", out _, out _));
        Assert.False(FileAssetKey.TryParse("file:", out _, out _));
    }

    // ---- FileAssetTextureLoader: lazy + memoized; missing → placeholder (never invisible) ----

    [Fact]
    public void LazyTextureLoadTest_DecodeOncePerPath()
    {
        var opened = new List<string>();
        var loader = new FileAssetTextureLoader(
            openStream: path => { opened.Add(path); return new MemoryStream(new byte[] { 1 }); },
            decode: _ => null, // a real Texture2D needs a GraphicsDevice; the decode CALL is what we count
            createPlaceholder: () => null);

        Assert.Equal(0, loader.DecodeCount); // constructing (like the catalog scan) decodes nothing

        loader.Load("file:Island/props/tree01.png");
        loader.Load("file:Island/props/tree01.png");
        loader.Load("file:Island/props/sheet.png#trunk");
        loader.Load("file:Island/props/sheet.png#crown"); // same sheet: same texture, no re-decode

        Assert.Equal(2, loader.DecodeCount); // tree01 + sheet, each exactly once
        Assert.Equal(new[] { "Island/props/tree01.png", "Island/props/sheet.png" }, opened);
    }

    [Fact]
    public void MissingAssetFileTest_RecordsPathAndUsesSharedPlaceholder()
    {
        var placeholderRequests = 0;
        var loader = new FileAssetTextureLoader(
            openStream: _ => null, // the file is missing
            decode: _ => null,
            createPlaceholder: () => { placeholderRequests++; return null; });

        loader.Load("file:Island/props/tree01.png");
        loader.Load("file:Island/props/tree01.png"); // memoized — no second warning path
        loader.Load("file:Island/ground/grass.png");

        // Both missing paths recorded once each (they render the magenta placeholder, loudly).
        Assert.Equal(new[] { "Island/props/tree01.png", "Island/ground/grass.png" }, loader.MissingPaths);
        // The placeholder is shared: created at most once however many files are missing.
        Assert.Equal(1, placeholderRequests);
        Assert.Equal(0, loader.DecodeCount);
    }
}
