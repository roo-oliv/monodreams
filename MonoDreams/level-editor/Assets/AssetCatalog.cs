#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
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
/// <para><b>The scan reads only the directory listing and the sidecar JSONs</b> — it never opens a
/// PNG. Textures are loaded lazily, on first use, by <see cref="FileAssetTextureLoader"/>.</para>
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
                        "starts empty. See Content/Island/MANIFEST.md for what to download.");
            return entries;
        }

        var pngs = Directory
            .EnumerateFiles(rootAbsolutePath, "*.png", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase) // deterministic across platforms
            .ToList();

        foreach (var absolutePath in pngs)
        {
            var relativeToRoot = Path.GetRelativePath(rootAbsolutePath, absolutePath).Replace('\\', '/');
            var relativePath = string.IsNullOrEmpty(keyRoot) ? relativeToRoot : keyRoot + "/" + relativeToRoot;
            var stem = Path.GetFileNameWithoutExtension(absolutePath);
            var folder = relativeToRoot.Contains('/')
                ? relativeToRoot.Substring(0, relativeToRoot.IndexOf('/'))
                : string.Empty;

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
                    $"{pngs.Count} PNG(s) → {ordered.Count} palette entries.");
        return ordered;
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
