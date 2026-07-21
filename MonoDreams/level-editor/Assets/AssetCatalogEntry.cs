#nullable enable
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.Assets;

/// <summary>
/// One placeable art asset the editor's palette offers (island-authoring plan §2): either a whole
/// PNG from the asset drop folder, or one named region of a sprite sheet sliced by a
/// <c>*.slices.json</c> sidecar. Pure data — the catalog scan produces these; the palette lists
/// them; <see cref="SpritePropFactory"/> turns one into the standard renderable entity stack.
/// </summary>
public sealed class AssetCatalogEntry
{
    /// <param name="relativePath">Content-root-relative PNG path, forward slashes
    /// (e.g. <c>"Island/props/tree01.png"</c>).</param>
    /// <param name="regionName">The sliced region's name, or null for a whole-PNG entry.</param>
    /// <param name="region">The region's source rectangle in the sheet, or null for a whole-PNG
    /// entry (the source is the full texture, known only once the texture loads).</param>
    /// <param name="label">The palette label (file stem, plus the region name for slices).</param>
    /// <param name="folder">The grouping folder under the scan root (e.g. <c>"props"</c>;
    /// empty for loose files at the root).</param>
    public AssetCatalogEntry(string relativePath, string? regionName, Rectangle? region,
        string label, string folder)
    {
        RelativePath = relativePath.Replace('\\', '/');
        RegionName = regionName;
        Region = region;
        Label = label;
        Folder = folder;
    }

    /// <summary>Content-root-relative PNG path, forward slashes.</summary>
    public string RelativePath { get; }

    /// <summary>The sliced region's name, or null for a whole-PNG entry.</summary>
    public string? RegionName { get; }

    /// <summary>The region's source rectangle in the sheet, or null (whole texture).</summary>
    public Rectangle? Region { get; }

    /// <summary>The palette label.</summary>
    public string Label { get; }

    /// <summary>The grouping folder under the scan root ("" for root-level files).</summary>
    public string Folder { get; }

    /// <summary>The stable identifier — the <see cref="AssetKey"/> without the scheme prefix
    /// (e.g. <c>"Island/props/sheet.png#trunk"</c>). This is what the headless
    /// <c>palette:&lt;id&gt;</c> op names.</summary>
    public string Id => RegionName == null
        ? RelativePath
        : RelativePath + FileAssetKey.RegionSeparator + RegionName;

    /// <summary>The <c>file:</c> AssetKey a placed entity serializes
    /// (see <see cref="FileAssetKey"/>).</summary>
    public string AssetKey => FileAssetKey.Compose(RelativePath, RegionName);
}
