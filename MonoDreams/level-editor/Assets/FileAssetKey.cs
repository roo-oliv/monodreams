#nullable enable
using System;

namespace MonoDreams.LevelEditor.Assets;

/// <summary>
/// The <c>file:</c> AssetKey scheme (island-authoring plan §2.1): a placed entity's
/// <c>SpriteInfoComponent.AssetKey</c> can reference a raw PNG in the asset drop folder instead of
/// an MGCB content key — <c>"file:Island/props/tree01.png"</c>, plus an optional
/// <c>#region</c> suffix naming a sliced sheet region (<c>"file:Island/props/sheet.png#trunk"</c>).
/// The path is content-root-relative with forward slashes, so the same key resolves on every host
/// through the content-stream seam (<c>TitleContainer.OpenStream</c>).
///
/// <para><b>The region suffix identifies the catalog entry, not the load.</b> Loading always opens
/// the base PNG (the suffix is stripped); the region's <c>Source</c> rectangle is serialized on the
/// <c>SpriteInfoComponent</c> itself, so a scene round-trips even when the sidecar file changes.</para>
///
/// <para><b>Graduation path (premise):</b> when art finalizes, assets move into MGCB content and
/// the key flips from <c>file:Island/props/tree01.png</c> to a content key
/// (<c>Island/props/tree01</c>) — a mechanical, greppable migration. <c>file:</c> keys are the
/// editor-first fast loop; content keys are the shipping (and web-ready) form.</para>
/// </summary>
public static class FileAssetKey
{
    /// <summary>The scheme prefix distinguishing a file asset from an MGCB content key.</summary>
    public const string Prefix = "file:";

    /// <summary>The separator between the relative path and an optional sliced-region name.</summary>
    public const char RegionSeparator = '#';

    /// <summary>Whether <paramref name="assetKey"/> uses the <c>file:</c> scheme.</summary>
    public static bool IsFileKey(string? assetKey) =>
        assetKey != null && assetKey.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Composes a <c>file:</c> key from a content-root-relative path (any directory
    /// separators are normalized to forward slashes) and an optional region name.</summary>
    public static string Compose(string relativePath, string? regionName = null)
    {
        if (string.IsNullOrEmpty(relativePath))
            throw new ArgumentException("A file asset key needs a relative path.", nameof(relativePath));
        var normalized = relativePath.Replace('\\', '/');
        return string.IsNullOrEmpty(regionName)
            ? Prefix + normalized
            : Prefix + normalized + RegionSeparator + regionName;
    }

    /// <summary>
    /// Splits a <c>file:</c> key into the content-root-relative path and the optional region name.
    /// Returns false (with null outs) for a non-<c>file:</c> key or an empty path.
    /// </summary>
    public static bool TryParse(string? assetKey, out string? relativePath, out string? regionName)
    {
        relativePath = null;
        regionName = null;
        if (!IsFileKey(assetKey)) return false;

        var body = assetKey!.Substring(Prefix.Length);
        var hash = body.IndexOf(RegionSeparator);
        if (hash >= 0)
        {
            regionName = hash + 1 < body.Length ? body.Substring(hash + 1) : null;
            body = body.Substring(0, hash);
        }

        if (string.IsNullOrEmpty(body)) return false;
        relativePath = body.Replace('\\', '/');
        return true;
    }
}
