#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Assets;

/// <summary>
/// The <b>per-asset layer-band marks</b> (island-authoring §3.3, FW3): a small
/// <c>asset-bands.json</c> config living in the asset drop folder (next to the PNGs it describes)
/// mapping a catalog entry's <see cref="AssetCatalogEntry.Id"/> to a band <b>name</b>, so an asset
/// can be <b>permanently</b> marked as (say) Ground and always place onto that band regardless of
/// the palette's global band selector. This is <b>dev-authoring metadata</b> — it lives with the
/// gitignored placeholder packs, is desktop-editor-only (never web), and never touches a scene
/// file: a placed entity still serializes the actual band it landed on (unchanged); this config
/// only changes the <i>default</i> band used when arming/placing an asset.
///
/// <para><b>Resolution rule (premise):</b> placing an armed asset uses <b>its marked band if set,
/// else the global band selector</b>. The mark survives an editor restart (that is the
/// "permanent") because it is persisted here and reloaded on the next scan.</para>
///
/// <para><b>Persistence.</b> Written and read through <see cref="CanonicalJson"/> (the same
/// byte-stable, ordinal-key-sorted policy scenes use), so the file diffs cleanly. Like
/// <see cref="AssetCatalog"/>'s own directory scan, the read/write uses host <c>System.IO</c>
/// directly (not the portable <c>IPlatformServices</c> seam): this metadata is a desktop-editor
/// authoring concern rooted at the same drop folder the catalog enumerates, never a shipped/web
/// artifact. A directly-constructed config (no root — a unit test) keeps the marks in memory and
/// its <see cref="Save"/> is a loud no-op, mirroring <see cref="AssetCatalog.CanRescan"/>.</para>
/// </summary>
public sealed class AssetBandConfig
{
    /// <summary>The config file name, dropped at the asset-folder root.</summary>
    public const string FileName = "asset-bands.json";

    private readonly string? _filePath;
    private readonly Dictionary<string, string> _bands;

    /// <summary>In-memory config (no scan root): marks are kept but <see cref="Save"/> is a no-op.</summary>
    public AssetBandConfig() : this(null, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
    {
    }

    private AssetBandConfig(string? filePath, Dictionary<string, string> bands)
    {
        _filePath = filePath;
        _bands = bands;
    }

    /// <summary>Whether this config is backed by a file and can <see cref="Save"/> (a real drop
    /// folder), versus in-memory only (a directly-constructed test instance).</summary>
    public bool CanPersist => _filePath != null;

    /// <summary>The marked entry ids (for tests / observability).</summary>
    public IReadOnlyCollection<string> MarkedIds => _bands.Keys;

    /// <summary>
    /// Loads the config from <c>rootAbsolutePath/asset-bands.json</c> (missing/malformed → an empty,
    /// persistable config rooted there — a fresh drop folder has no marks yet). A null root yields an
    /// in-memory config (<see cref="CanPersist"/> false).
    /// </summary>
    public static AssetBandConfig Load(string? rootAbsolutePath)
    {
        if (string.IsNullOrEmpty(rootAbsolutePath))
            return new AssetBandConfig();

        var path = Path.Combine(rootAbsolutePath!, FileName);
        var bands = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(path))
        {
            try
            {
                var dto = CanonicalJson.Deserialize<BandConfigDto>(File.ReadAllText(path));
                if (dto?.Bands != null)
                    foreach (var kv in dto.Bands)
                        if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                            bands[kv.Key] = kv.Value;
            }
            catch (Exception ex)
            {
                Logger.Warning($"[level-editor] Asset-band config '{path}' is malformed " +
                               $"({ex.Message}) — starting with no per-asset band marks.");
            }
        }

        return new AssetBandConfig(path, bands);
    }

    /// <summary>The band name marked for <paramref name="entryId"/>, or null (unmarked → the caller
    /// falls back to the global band selector).</summary>
    public bool TryGetBand(string entryId, out string bandName) => _bands.TryGetValue(entryId, out bandName!);

    /// <summary>Marks <paramref name="entryId"/> with <paramref name="bandName"/> and persists.</summary>
    public void SetBand(string entryId, string bandName)
    {
        _bands[entryId] = bandName;
        Save();
    }

    /// <summary>Clears any mark on <paramref name="entryId"/> (back to the global selector) and
    /// persists. A no-op (still persists nothing changed) when the id was unmarked.</summary>
    public void ClearBand(string entryId)
    {
        if (_bands.Remove(entryId)) Save();
    }

    /// <summary>Writes the marks to disk through the canonical (byte-stable) JSON policy. A loud
    /// no-op for an in-memory config.</summary>
    public void Save()
    {
        if (_filePath == null)
        {
            Logger.Warning("[level-editor] Asset-band config save skipped: this config has no drop-folder root.");
            return;
        }

        try
        {
            File.WriteAllText(_filePath, CanonicalJson.Serialize(new BandConfigDto { Bands = _bands }));
        }
        catch (Exception ex)
        {
            Logger.Warning($"[level-editor] Failed to write asset-band config '{_filePath}': {ex.Message}.");
        }
    }

    private sealed class BandConfigDto
    {
        [JsonPropertyName("bands")] public Dictionary<string, string> Bands { get; set; } = new();
    }
}
