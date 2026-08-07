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
///
/// <para>The drop-a-PNG wave adds three groups: the <c>(NxM)</c> filename <b>auto-grid</b> (sliced
/// from the PNG's IHDR header — the fixtures are 24 header bytes and nothing else, so a passing test
/// proves the scan never decodes), the <c>.anim</c> <b>folder folding</b> (one animated entry, frames
/// in natural-numeric order), and the <b>`file:` ladder</b> — source tree → build output → the MGCB
/// content key for the same path minus its extension → the loud magenta placeholder. Names the premise
/// "The `file:` ladder degrades dev machine → packaged platform, and never assumes a filesystem" in
/// MonoDreams/level-editor/docs/premises.md.</para>
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

        /// <summary>
        /// Writes a "PNG" whose bytes are EXACTLY the 24-byte signature + IHDR prefix declaring
        /// <paramref name="width"/>×<paramref name="height"/> — and nothing else (no IDAT, no IEND).
        /// Such a file can never be DECODED, so an auto-grid test that reads the right dimensions
        /// from it proves the scan reads the fixed-offset header and nothing more.
        /// </summary>
        public void AddPngHeader(string relativePath, int width, int height)
        {
            var header = new byte[24];
            // 8-byte PNG signature: \x89 P N G \r \n \x1A \n
            header[0] = 0x89; header[1] = (byte)'P'; header[2] = (byte)'N'; header[3] = (byte)'G';
            header[4] = 0x0D; header[5] = 0x0A; header[6] = 0x1A; header[7] = 0x0A;
            // The first chunk MUST be IHDR: 4-byte length (13) + 4-byte type.
            header[8] = 0; header[9] = 0; header[10] = 0; header[11] = 13;
            header[12] = (byte)'I'; header[13] = (byte)'H'; header[14] = (byte)'D'; header[15] = (byte)'R';
            // Then the big-endian width + height — the only two fields the scan wants.
            WriteBigEndian(header, 16, width);
            WriteBigEndian(header, 20, height);

            var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, header);
        }

        private static void WriteBigEndian(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
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
    public void RescanTest_PicksUpANewlyDroppedFile()
    {
        using var dir = new TempAssetDir();
        dir.AddFile("props/tree01.png");

        var catalog = AssetCatalog.Scan(dir.Root, "Island");
        Assert.True(catalog.CanRescan);
        Assert.Single(catalog.Entries);
        Assert.False(catalog.TryGet("Island/props/stone.png", out _));

        // Drop a new PNG and rescan: the catalog picks it up in place (Slice 4 refresh) without
        // reconstructing — the palette holds this same instance.
        dir.AddFile("props/stone.png");
        Assert.True(catalog.Rescan());

        Assert.Equal(2, catalog.Entries.Count);
        Assert.True(catalog.TryGet("Island/props/stone.png", out var stone));
        Assert.Equal("stone", stone.Label);
    }

    [Fact]
    public void RescanTest_NoScanRootIsALoudNoOp()
    {
        // A directly-constructed catalog (e.g. a unit test) has no scan root → cannot rescan.
        var catalog = new AssetCatalog(new[]
        {
            new AssetCatalogEntry("Island/props/a.png", null, null, "a", "props"),
        });
        Assert.False(catalog.CanRescan);
        Assert.False(catalog.Rescan());
        Assert.Single(catalog.Entries); // unchanged
    }

    [Fact]
    public void InvalidateTest_ReDecodesAfterTheCacheIsCleared()
    {
        var loader = new FileAssetTextureLoader(
            openStream: _ => new MemoryStream(new byte[] { 1 }),
            decode: _ => null, // the decode CALL is what we count
            createPlaceholder: () => null);

        loader.Load("file:Island/props/tree01.png");
        loader.Load("file:Island/props/tree01.png"); // memoized
        Assert.Equal(1, loader.DecodeCount);

        // A refresh clears the cache: the next load re-opens + re-decodes (picks up a changed file).
        loader.Invalidate();
        loader.Load("file:Island/props/tree01.png");
        Assert.Equal(2, loader.DecodeCount);
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

    // ---- Tileset auto-grid: the `(NxM)` filename convention, sliced from HEADER BYTES ONLY ----

    [Fact]
    public void AutoGridTest_NxMMarkerSlicesTheSheetFromItsIhdrHeader()
    {
        using var dir = new TempAssetDir();
        // The fixture is 24 bytes of PNG header and NOTHING else: a decoder would choke on it, so a
        // correct grid here can only have come from the fixed-offset IHDR read (the scan never
        // decodes — that is the whole point of the drop-a-PNG loop staying O(1) at startup).
        dir.AddPngHeader("tiles/Terrain (32x32).png", width: 64, height: 96);

        var catalog = AssetCatalog.Scan(dir.Root, "Island");

        // 64x96 / 32x32 = 2 columns x 3 rows, named row-major rRRcCC.
        Assert.Equal(6, catalog.Entries.Count);
        Assert.Equal(
            new[] { "r00c00", "r00c01", "r01c00", "r01c01", "r02c00", "r02c01" },
            catalog.Entries.Select(e => e.RegionName!).ToArray());
        Assert.Equal(
            new[]
            {
                "Terrain (32x32)#r00c00", "Terrain (32x32)#r00c01",
                "Terrain (32x32)#r01c00", "Terrain (32x32)#r01c01",
                "Terrain (32x32)#r02c00", "Terrain (32x32)#r02c01",
            },
            catalog.Entries.Select(e => e.Label).ToArray());
        Assert.Equal(
            new[]
            {
                new Rectangle(0, 0, 32, 32), new Rectangle(32, 0, 32, 32),
                new Rectangle(0, 32, 32, 32), new Rectangle(32, 32, 32, 32),
                new Rectangle(0, 64, 32, 32), new Rectangle(32, 64, 32, 32),
            },
            catalog.Entries.Select(e => e.Region!.Value).ToArray());

        // Every cell is a region OF THE SAME SHEET: one PNG path, the region rect on the entry.
        foreach (var entry in catalog.Entries)
        {
            Assert.Equal("Island/tiles/Terrain (32x32).png", entry.RelativePath);
            Assert.Equal("tiles", entry.Folder);
            Assert.False(entry.IsSequence);
        }
        Assert.Equal("file:Island/tiles/Terrain (32x32).png#r01c01",
            catalog.Entries.Single(e => e.RegionName == "r01c01").AssetKey);
    }

    [Fact]
    public void AutoGridTest_FallsBackToTheWholePngWhenItCannotSliceCleanly()
    {
        using var dir = new TempAssetDir();
        // (a) The sheet does not divide evenly into the marked cell size (50 % 32 != 0).
        dir.AddPngHeader("tiles/Ragged (32x32).png", width: 50, height: 40);
        // (b) The marked cell size would explode past the MaxAutoGridCells safety cap
        // (32x32 / 1x1 = 1024 cells): a palette of thousands of cards is unusable, not helpful.
        dir.AddPngHeader("tiles/Pixels (1x1).png", width: 32, height: 32);

        var catalog = AssetCatalog.Scan(dir.Root, "Island");

        // Both fall back LOUDLY to one whole-PNG entry each — the art is still placeable, never hidden.
        Assert.Equal(2, catalog.Entries.Count);
        var cellsIfSliced = 32 * 32; // what the (1x1) marker would carve the 32x32 sheet into
        Assert.True(cellsIfSliced > AssetCatalog.MaxAutoGridCells, "the (1x1) fixture must exceed the cap");
        foreach (var entry in catalog.Entries)
        {
            Assert.Null(entry.RegionName);
            Assert.Null(entry.Region);
        }
        Assert.Equal(new[] { "Pixels (1x1)", "Ragged (32x32)" },
            catalog.Entries.Select(e => e.Label).ToArray());
    }

    // ---- Animation folders: a `.anim` directory folds into ONE sequence entry ----

    [Fact]
    public void AnimFolderTest_FoldsToOneSequenceEntryInNaturalNumericOrder()
    {
        using var dir = new TempAssetDir();
        dir.AddFile("fx/Torch" + AssetCatalog.AnimDirectorySuffix + "/1.png");
        dir.AddFile("fx/Torch" + AssetCatalog.AnimDirectorySuffix + "/2.png");
        dir.AddFile("fx/Torch" + AssetCatalog.AnimDirectorySuffix + "/10.png");
        dir.AddFile("fx/Lantern.png"); // a loose sibling of the folder: still its own static prop

        var catalog = AssetCatalog.Scan(dir.Root, "Island");

        // Four PNGs → two palette entries: the folded animation + the loose sibling.
        Assert.Equal(2, catalog.Entries.Count);

        var torch = catalog.Entries.Single(e => e.Label == "Torch"); // the `.anim` suffix is stripped
        Assert.True(torch.IsSequence);
        // Natural-numeric, so 10 sorts AFTER 2 (a plain string sort would give 1, 10, 2 and the
        // animation would play out of order).
        Assert.Equal(
            new[]
            {
                "Island/fx/Torch.anim/1.png",
                "Island/fx/Torch.anim/2.png",
                "Island/fx/Torch.anim/10.png",
            },
            torch.SequenceFrames!);
        // The entry's own path IS frame 0 — its thumbnail/ghost/frame-0 texture.
        Assert.Equal("Island/fx/Torch.anim/1.png", torch.RelativePath);
        Assert.Equal("file:Island/fx/Torch.anim/1.png", torch.AssetKey);
        Assert.Equal("fx", torch.Folder); // the folder the `.anim` directory lives in, not the directory
        Assert.Null(torch.RegionName);

        // The loose sibling is untouched by the folding.
        var lantern = catalog.Entries.Single(e => e.Label == "Lantern");
        Assert.False(lantern.IsSequence);
        Assert.Null(lantern.SequenceFrames);
        Assert.Equal("Island/fx/Lantern.png", lantern.RelativePath);
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

    // ---- The `file:` ladder: source tree → build output → MGCB content key → loud placeholder ----

    [Fact]
    public void FileLadderTest_FallsThroughToTheContentKeyWithTheExtensionStripped()
    {
        // The packaged-platform simulation: the FILESYSTEM rung is disabled (on web/mobile/console
        // there is no source tree and no readable drop folder), so the only rung left is the MGCB
        // content key for the same path minus its extension — the .xnb that shipped in the bundle.
        var resolved = new List<string>();
        var loader = new FileAssetTextureLoader(
            openStream: _ => null,
            decode: _ => null,
            createPlaceholder: () => null,
            resolveContentKey: key => { resolved.Add(key); return null; });

        loader.Load("file:Island/props/tree01.png");

        // The content rung was tried, with the ".png" dropped exactly as MGCB drops it when it builds.
        Assert.Equal(new[] { "Island/props/tree01" }, resolved);
        // Every rung missed → the loud magenta path, recorded once.
        Assert.Equal(new[] { "Island/props/tree01.png" }, loader.MissingPaths);

        // The whole ladder is memoized: a second Load re-walks nothing (the content resolver is not
        // re-invoked, and the miss is not re-recorded).
        loader.Load("file:Island/props/tree01.png");
        Assert.Single(resolved);
        Assert.Single(loader.MissingPaths);
    }

    [Fact]
    public void ContentKeyServingTest_NonFileKeysGoStraightToTheResolver_Memoized()
    {
        // A NON-file: key (a plain MGCB content key) never touches the filesystem rungs at all — a
        // mixed-key consumer (a prefab thumbnail whose sprite already graduated to content) resolves
        // through this one loader.
        var resolved = new List<string>();
        var placeholderRequests = 0;
        var loader = new FileAssetTextureLoader(
            openStream: _ => throw new IOException("a content key must never hit the filesystem"),
            decode: _ => null,
            createPlaceholder: () => { placeholderRequests++; return null; },
            resolveContentKey: key => { resolved.Add(key); return null; });

        loader.Load("Atlas/TX Player");
        loader.Load("Atlas/TX Player");

        // The RAW key (no extension to strip, no scheme to parse), asked for exactly once.
        Assert.Equal(new[] { "Atlas/TX Player" }, resolved);
        // A null resolution still yields the shared placeholder, built at most once.
        Assert.Equal(1, placeholderRequests);
        Assert.Equal(0, loader.DecodeCount);
        Assert.Empty(loader.MissingPaths); // a content-key miss is not a missing FILE
    }

    [Fact]
    public void ContentKeyServingTest_WithoutAResolverTheLegacyPlaceholderBehaviorIsUnchanged()
    {
        var placeholderRequests = 0;
        var loader = new FileAssetTextureLoader(
            openStream: _ => throw new IOException("a content key must never hit the filesystem"),
            decode: _ => null,
            createPlaceholder: () => { placeholderRequests++; return null; });

        loader.Load("Atlas/TX Player");
        loader.Load("Atlas/TX Player");

        // No resolver wired: a non-file: key is a composition error → the loud shared placeholder,
        // exactly as before the ladder existed.
        Assert.Equal(1, placeholderRequests);
        Assert.Equal(0, loader.DecodeCount);
        Assert.Empty(loader.MissingPaths);
    }

    [Fact]
    public void AbsoluteContentRootTest_OpensARealStreamInsteadOfThrowing()
    {
        // Regression: TitleContainer.OpenStream throws on a ROOTED path by contract, and the editor's
        // source-content-tree loader hands it exactly that — an absolute project root. The rung must
        // detect the rooted combination and read the file directly, or the editor dies on boot.
        using var dir = new TempAssetDir();
        dir.AddFile("a.png");
        Assert.True(Path.IsPathRooted(dir.Root));

        using var stream = FileAssetTextureLoader.OpenContentStream(dir.Root, "a.png");

        Assert.NotNull(stream);
        Assert.True(stream!.CanRead);
        Assert.NotEqual(-1, stream.ReadByte()); // a real, readable stream — bytes came back
    }
}
