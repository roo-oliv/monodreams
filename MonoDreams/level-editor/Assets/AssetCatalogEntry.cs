#nullable enable
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoDreams.LevelEditor.Assets;

/// <summary>
/// One placeable art asset the editor's palette offers (island-authoring plan §2): a whole PNG
/// from the asset drop folder, one named region of a sprite sheet sliced by a
/// <c>*.slices.json</c> sidecar (or the <c>(NxM)</c> filename auto-grid), or a <b>frame
/// sequence</b> (numbered sibling PNGs collapsed into one animated entry — see
/// <see cref="SequenceFrames"/>). Pure data — the catalog scan produces these; the palette lists
/// them; <see cref="SpritePropFactory"/> turns one into the standard renderable entity stack.
/// </summary>
public sealed class AssetCatalogEntry
{
    /// <param name="relativePath">Content-root-relative PNG path, forward slashes
    /// (e.g. <c>"Island/props/tree01.png"</c>). For a frame sequence: the FIRST frame's PNG (the
    /// entry's thumbnail/ghost/frame-0 texture).</param>
    /// <param name="regionName">The sliced region's name, or null for a whole-PNG entry.</param>
    /// <param name="region">The region's source rectangle in the sheet, or null for a whole-PNG
    /// entry (the source is the full texture, known only once the texture loads).</param>
    /// <param name="label">The palette label (file stem, plus the region name for slices).</param>
    /// <param name="folder">The grouping folder under the scan root (e.g. <c>"props"</c>;
    /// empty for loose files at the root).</param>
    /// <param name="sequenceFrames">For an animated frame-sequence entry: every frame's
    /// content-root-relative PNG path in play order (first == <paramref name="relativePath"/>);
    /// null for a plain static entry.</param>
    public AssetCatalogEntry(string relativePath, string? regionName, Rectangle? region,
        string label, string folder, IReadOnlyList<string>? sequenceFrames = null)
    {
        RelativePath = relativePath.Replace('\\', '/');
        RegionName = regionName;
        Region = region;
        Label = label;
        Folder = folder;
        SequenceFrames = sequenceFrames;
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

    /// <summary>For an animated frame-sequence entry (numbered sibling PNGs like
    /// <c>Chest Open 01.png … Chest Open 10.png</c> collapsed by the scan): every frame's
    /// content-root-relative path in play order — the first IS <see cref="RelativePath"/>.
    /// Null for a plain static entry. Placement builds a <c>SpriteAnimationComponent</c> whose
    /// frame asset keys are these paths' <c>file:</c> keys.</summary>
    public IReadOnlyList<string>? SequenceFrames { get; }

    /// <summary>Whether this entry is an animated frame sequence (two or more frames).</summary>
    public bool IsSequence => SequenceFrames is { Count: > 1 };

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
