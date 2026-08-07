#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Assets;

/// <summary>
/// The editor's asset catalog (island-authoring plan §2): a scan of the asset drop folder
/// (<c>Content/Island/</c> by convention — gitignored; see its committed <c>MANIFEST.md</c>) into
/// one <see cref="AssetCatalogEntry"/> per PNG, plus one entry per named region of any PNG that
/// ships a <c>&lt;image&gt;.slices.json</c> sidecar
/// (<c>{"regions":[{"name":"trunk","x":0,"y":0,"w":32,"h":48}]}</c>). A sliced sheet contributes
/// its regions only (the raw sheet is not itself a placeable prop).
///
/// <para><b>Tileset auto-grid (pixel-art wave).</b> A sheet whose file name carries a
/// <c>(NxM)</c> cell-size marker (the asset-pack convention — <c>Terrain (32x32).png</c>) and has
/// no sidecar is auto-sliced into a full row-major grid of <c>rRRcCC</c> regions. Cell dimensions
/// come from the marker; sheet dimensions come from the PNG's IHDR header (a 24-byte read — the
/// scan still never DECODES a texture).</para>
///
/// <para><b>Animation folders (pixel-art wave).</b> A directory whose name ends in <c>.anim</c>
/// (e.g. <c>Chest Open.anim/01.png…10.png</c>) collapses into ONE animated entry: frame 0 is the
/// entry's texture/thumbnail and every member PNG (natural-numeric order) becomes a
/// <c>SpriteAnimationComponent</c> frame at placement (see <see cref="AssetCatalogEntry.SequenceFrames"/>
/// and <see cref="SpritePropFactory"/>). Explicit-by-convention — numbered VARIANTS
/// (<c>tree01.png</c>, <c>tree02.png</c>) stay separate props unless the artist folders them.</para>
///
/// <para><b>The scan reads only the directory listing, the sidecar JSONs, and (for auto-grid
/// sheets) the PNG's fixed-offset IHDR dimensions</b> — it never decodes a texture. Textures are
/// loaded lazily, on first use, by <see cref="FileAssetTextureLoader"/>.</para>
///
/// <para><b>Why System.IO here and TitleContainer for the loads:</b> <c>TitleContainer</c> is a
/// stream-only API — it cannot enumerate a directory — so the scan uses host-filesystem
/// enumeration over the content output dir (desktop-editor-first; the web/shipping path is the
/// MGCB graduation recorded in <see cref="FileAssetKey"/>'s doc). Each individual texture load
/// then goes through <c>TitleContainer.OpenStream</c> (the same portable content-stream seam the
/// scene reader uses), so the load path itself stays backend-uniform.</para>
///
/// <para>Fail-soft, loud: a missing drop folder yields an empty catalog (logged Info — a fresh
/// checkout has no packs downloaded yet); a malformed sidecar logs a Warning and the sheet falls
/// back to a whole-PNG entry, so bad JSON never hides the art.</para>
/// </summary>
public sealed class AssetCatalog
{
    /// <summary>The sidecar suffix appended to a PNG's file name (<c>sheet.png.slices.json</c>).</summary>
    public const string SliceSidecarSuffix = ".slices.json";

    private Dictionary<string, AssetCatalogEntry> _byId;
    private readonly string? _rootAbsolutePath;
    private readonly string _keyRoot;

    public AssetCatalog(IReadOnlyList<AssetCatalogEntry> entries)
        : this(entries, rootAbsolutePath: null, keyRoot: string.Empty)
    {
    }

    private AssetCatalog(IReadOnlyList<AssetCatalogEntry> entries, string? rootAbsolutePath, string keyRoot)
    {
        _rootAbsolutePath = rootAbsolutePath;
        _keyRoot = keyRoot;
        Entries = entries;
        _byId = BuildIndex(entries);
    }

    /// <summary>Every catalog entry, ordered by folder then label (deterministic across scans).</summary>
    public IReadOnlyList<AssetCatalogEntry> Entries { get; private set; }

    /// <summary>Whether this catalog remembers its scan root and can <see cref="Rescan"/> live
    /// (island-authoring Slice 4 refresh). A catalog constructed directly (a unit test) cannot.</summary>
    public bool CanRescan => _rootAbsolutePath != null;

    /// <summary>The absolute path of the drop folder this catalog was scanned from, or null for a
    /// directly-constructed (test) catalog. The per-asset band config (<see cref="AssetBandConfig"/>)
    /// lives here, alongside the assets.</summary>
    public string? RootAbsolutePath => _rootAbsolutePath;

    /// <summary>
    /// Re-scans the drop folder this catalog was <see cref="Scan"/>ned from and replaces its
    /// entries in place (Slice 4 refresh button), so a newly-dropped or renamed PNG appears
    /// without an editor restart. Returns false — loud — for a catalog with no scan root
    /// (constructed directly, e.g. in a test). The palette rebuilds its rows from the new entries.
    /// </summary>
    public bool Rescan()
    {
        if (_rootAbsolutePath == null)
        {
            Logger.Warning("[level-editor] Asset catalog rescan skipped: this catalog has no scan root.");
            return false;
        }

        Entries = ScanEntries(_rootAbsolutePath, _keyRoot);
        _byId = BuildIndex(Entries);
        return true;
    }

    private static Dictionary<string, AssetCatalogEntry> BuildIndex(IReadOnlyList<AssetCatalogEntry> entries)
    {
        var byId = new Dictionary<string, AssetCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            byId[entry.Id] = entry;
        return byId;
    }

    /// <summary>Looks an entry up by its <see cref="AssetCatalogEntry.Id"/> or its full
    /// <c>file:</c> AssetKey (both accepted, case-insensitive).</summary>
    public bool TryGet(string idOrAssetKey, out AssetCatalogEntry entry)
    {
        var id = idOrAssetKey;
        if (FileAssetKey.TryParse(idOrAssetKey, out var rel, out var region))
            id = region == null ? rel! : rel + FileAssetKey.RegionSeparator + region;
        return _byId.TryGetValue(id!, out entry!);
    }

    /// <summary>
    /// Scans <paramref name="rootAbsolutePath"/> (recursively) for PNGs + slice sidecars into a
    /// catalog whose entries' <see cref="AssetCatalogEntry.RelativePath"/>s are rooted at
    /// <paramref name="keyRoot"/> (e.g. <c>"Island"</c> → <c>"Island/props/tree01.png"</c> —
    /// content-root-relative, matching what <c>TitleContainer</c> resolves under the content dir).
    /// A missing root directory yields an empty catalog.
    /// </summary>
    public static AssetCatalog Scan(string rootAbsolutePath, string keyRoot) =>
        new(ScanEntries(rootAbsolutePath, keyRoot), rootAbsolutePath, keyRoot);

    /// <summary>The ordered entry list for a drop folder (the pure scan; <see cref="Scan"/> and
    /// <see cref="Rescan"/> share it).</summary>
    private static List<AssetCatalogEntry> ScanEntries(string rootAbsolutePath, string keyRoot)
    {
        var entries = new List<AssetCatalogEntry>();

        if (!Directory.Exists(rootAbsolutePath))
        {
            Logger.Info($"[level-editor] Asset folder '{rootAbsolutePath}' not found — the palette " +
                        $"starts empty. Drop PNGs under it (key root '{keyRoot}').");
            return entries;
        }

        var pngs = Directory
            .EnumerateFiles(rootAbsolutePath, "*.png", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase) // deterministic across platforms
            .ToList();

        // Animation folders first: every PNG inside a `<name>.anim` directory belongs to ONE
        // animated entry (frames in natural-numeric order), not to the per-PNG loop below.
        var animGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var loosePngs = new List<string>();
        foreach (var absolutePath in pngs)
        {
            var animDir = FindAnimDirectory(rootAbsolutePath, absolutePath);
            if (animDir != null)
            {
                if (!animGroups.TryGetValue(animDir, out var frames))
                    animGroups[animDir] = frames = new List<string>();
                frames.Add(absolutePath);
            }
            else
            {
                loosePngs.Add(absolutePath);
            }
        }

        foreach (var (animDir, frames) in animGroups.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            frames.Sort(CompareNaturalNumeric);
            var framePaths = frames
                .Select(f => ComposeRelativePath(rootAbsolutePath, keyRoot, f))
                .ToList();
            var dirName = Path.GetFileName(animDir);
            var label = dirName.Substring(0, dirName.Length - AnimDirectorySuffix.Length);
            entries.Add(new AssetCatalogEntry(framePaths[0], regionName: null, region: null,
                label: label, FolderOf(rootAbsolutePath, animDir), sequenceFrames: framePaths));
        }

        foreach (var absolutePath in loosePngs)
        {
            var relativePath = ComposeRelativePath(rootAbsolutePath, keyRoot, absolutePath);
            var stem = Path.GetFileNameWithoutExtension(absolutePath);
            var folder = FolderOf(rootAbsolutePath, absolutePath);

            var regions = TryReadSidecar(absolutePath);
            if (regions is { Count: > 0 })
            {
                // A sliced sheet contributes its regions only — the sheet is not a prop.
                foreach (var region in regions)
                    entries.Add(new AssetCatalogEntry(
                        relativePath, region.Name,
                        new Microsoft.Xna.Framework.Rectangle(region.X, region.Y, region.W, region.H),
                        label: $"{stem}#{region.Name}", folder));
            }
            else if (TryAutoGridRegions(absolutePath, stem, out var cells))
            {
                // A `(NxM)`-marked tileset with no sidecar: one entry per grid cell.
                foreach (var (name, rect) in cells)
                    entries.Add(new AssetCatalogEntry(relativePath, name, rect,
                        label: $"{stem}#{name}", folder));
            }
            else
            {
                entries.Add(new AssetCatalogEntry(relativePath, regionName: null, region: null,
                    label: stem, folder));
            }
        }

        var ordered = entries
            .OrderBy(e => e.Folder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Logger.Info($"[level-editor] Asset catalog scanned '{rootAbsolutePath}': " +
                    $"{pngs.Count} PNG(s) → {ordered.Count} palette entries " +
                    $"({animGroups.Count} animation(s)).");
        return ordered;
    }

    /// <summary>The directory suffix that marks an animation-frames folder.</summary>
    public const string AnimDirectorySuffix = ".anim";

    /// <summary>The nearest ancestor directory of <paramref name="pngAbsolutePath"/> (inside the
    /// scan root) whose name ends in <see cref="AnimDirectorySuffix"/>, or null.</summary>
    private static string? FindAnimDirectory(string rootAbsolutePath, string pngAbsolutePath)
    {
        var dir = Path.GetDirectoryName(pngAbsolutePath);
        while (dir != null && dir.Length >= rootAbsolutePath.Length)
        {
            if (Path.GetFileName(dir).EndsWith(AnimDirectorySuffix, StringComparison.OrdinalIgnoreCase))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string ComposeRelativePath(string rootAbsolutePath, string keyRoot, string absolutePath)
    {
        var relativeToRoot = Path.GetRelativePath(rootAbsolutePath, absolutePath).Replace('\\', '/');
        return string.IsNullOrEmpty(keyRoot) ? relativeToRoot : keyRoot + "/" + relativeToRoot;
    }

    private static string FolderOf(string rootAbsolutePath, string absolutePath)
    {
        var relativeToRoot = Path.GetRelativePath(rootAbsolutePath, absolutePath).Replace('\\', '/');
        return relativeToRoot.Contains('/')
            ? relativeToRoot.Substring(0, relativeToRoot.IndexOf('/'))
            : string.Empty;
    }

    /// <summary>Natural-numeric file-name comparison (<c>frame2</c> before <c>frame10</c>) so
    /// animation frames order by their number regardless of zero-padding.</summary>
    internal static int CompareNaturalNumeric(string a, string b)
    {
        var na = Regex.Replace(a, @"\d+", m => m.Value.PadLeft(10, '0'));
        var nb = Regex.Replace(b, @"\d+", m => m.Value.PadLeft(10, '0'));
        return string.Compare(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Tileset auto-grid (the `(NxM)` filename convention) ----

    private static readonly Regex GridMarker = new(@"\((\d+)\s*[xX]\s*(\d+)\)", RegexOptions.Compiled);

    /// <summary>The auto-grid safety cap: a sheet that would slice into more cells than this is
    /// left whole (loud) — a palette with thousands of cards is unusable, not helpful.</summary>
    public const int MaxAutoGridCells = 256;

    /// <summary>
    /// Slices a <c>(NxM)</c>-named sheet into row-major <c>rRRcCC</c> cell regions. Sheet
    /// dimensions come from the PNG IHDR header (<see cref="TryReadPngDimensions"/> — a fixed-offset
    /// 24-byte read, never a decode). False (whole-PNG fallback, loud where it matters) when the
    /// name has no marker, the header is unreadable, the sheet doesn't divide evenly, or the cell
    /// count exceeds <see cref="MaxAutoGridCells"/>.
    /// </summary>
    private static bool TryAutoGridRegions(string absolutePath, string stem,
        out List<(string Name, Microsoft.Xna.Framework.Rectangle Rect)> cells)
    {
        cells = new List<(string, Microsoft.Xna.Framework.Rectangle)>();
        var marker = GridMarker.Match(stem);
        if (!marker.Success) return false;

        var cellW = int.Parse(marker.Groups[1].Value);
        var cellH = int.Parse(marker.Groups[2].Value);
        if (cellW <= 0 || cellH <= 0) return false;

        if (!TryReadPngDimensions(absolutePath, out var width, out var height))
        {
            Logger.Warning($"[level-editor] '{absolutePath}' has a (NxM) grid marker but its PNG " +
                           "header is unreadable — using the whole PNG.");
            return false;
        }
        if (width % cellW != 0 || height % cellH != 0)
        {
            Logger.Warning($"[level-editor] '{absolutePath}' ({width}x{height}) does not divide " +
                           $"into {cellW}x{cellH} cells — using the whole PNG.");
            return false;
        }

        var cols = width / cellW;
        var rows = height / cellH;
        if (cols * rows > MaxAutoGridCells)
        {
            Logger.Warning($"[level-editor] '{absolutePath}' would slice into {cols * rows} cells " +
                           $"(cap {MaxAutoGridCells}) — using the whole PNG. Add a slices.json for " +
                           "the regions you actually want.");
            return false;
        }

        for (var r = 0; r < rows; r++)
        for (var c = 0; c < cols; c++)
            cells.Add(($"r{r:00}c{c:00}",
                new Microsoft.Xna.Framework.Rectangle(c * cellW, r * cellH, cellW, cellH)));
        return true;
    }

    /// <summary>Reads a PNG's pixel dimensions from its fixed-offset IHDR chunk (signature 8 bytes
    /// + length 4 + type 4 + width 4 + height 4, both big-endian) — 24 bytes, no decode.</summary>
    internal static bool TryReadPngDimensions(string absolutePath, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            using var stream = File.OpenRead(absolutePath);
            Span<byte> header = stackalloc byte[24];
            if (stream.Read(header) != header.Length) return false;
            // PNG signature, then the first chunk must be IHDR.
            if (header[0] != 0x89 || header[1] != (byte)'P' || header[2] != (byte)'N' || header[3] != (byte)'G')
                return false;
            if (header[12] != (byte)'I' || header[13] != (byte)'H' || header[14] != (byte)'D' || header[15] != (byte)'R')
                return false;
            width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
            return width > 0 && height > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The sidecar regions of <paramref name="pngAbsolutePath"/>, or null (no sidecar, or
    /// a malformed one — logged loud, whole-PNG fallback).</summary>
    private static List<SliceRegionDto>? TryReadSidecar(string pngAbsolutePath)
    {
        var sidecarPath = pngAbsolutePath + SliceSidecarSuffix;
        if (!File.Exists(sidecarPath)) return null;

        try
        {
            var dto = JsonSerializer.Deserialize<SliceSidecarDto>(File.ReadAllText(sidecarPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var regions = dto?.Regions?.Where(r => !string.IsNullOrEmpty(r.Name)).ToList();
            if (regions is not { Count: > 0 })
            {
                Logger.Warning($"[level-editor] Slice sidecar '{sidecarPath}' has no named regions — " +
                               "using the whole PNG.");
                return null;
            }
            return regions;
        }
        catch (Exception ex)
        {
            Logger.Warning($"[level-editor] Slice sidecar '{sidecarPath}' is malformed " +
                           $"({ex.Message}) — using the whole PNG.");
            return null;
        }
    }

    private sealed class SliceSidecarDto
    {
        [JsonPropertyName("regions")] public List<SliceRegionDto>? Regions { get; set; }
    }

    private sealed class SliceRegionDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("x")] public int X { get; set; }
        [JsonPropertyName("y")] public int Y { get; set; }
        [JsonPropertyName("w")] public int W { get; set; }
        [JsonPropertyName("h")] public int H { get; set; }
    }
}
