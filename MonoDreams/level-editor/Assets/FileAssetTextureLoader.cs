#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.LevelEditor.UI;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Assets;

/// <summary>
/// Loads the <c>Texture2D</c> behind a <c>file:</c> AssetKey (see <see cref="FileAssetKey"/>) —
/// lazily, memoized per PNG path, through <c>TitleContainer.OpenStream</c> +
/// <c>Texture2D.FromStream</c> (island-authoring plan §2.1; the same content-stream seam the
/// scene reader uses). The catalog scan never touches this: textures load on first use (arming a
/// palette item, placing, or rehydrating a loaded scene).
///
/// <para><b>No file? Try the built content, THEN complain.</b> When the PNG cannot be opened or
/// decoded, <see cref="Load"/> falls back to the MGCB content key for the same path with the
/// extension dropped (<c>Art/Terrain/Adve.png</c> → <c>Art/Terrain/Adve</c>) — which is what lets
/// <c>file:</c> keys work where there is no filesystem to read: on the web the drop folder is not
/// there, but the built <c>.xnb</c> is. Only when BOTH miss does it log a <c>Logger.Warning</c> and
/// return the shared solid-magenta placeholder (generated once), so a scene referencing a pack the
/// checkout has not downloaded shows unmistakable magenta boxes instead of silently dropping
/// sprites. The failed path is recorded in <see cref="MissingPaths"/>.</para>
///
/// <para>The stream/decode/placeholder functions are injectable so the loader is unit-testable
/// without a <c>GraphicsDevice</c> (the production ctor wires TitleContainer +
/// <c>Texture2D.FromStream</c> + a generated 32×32 magenta texture).</para>
/// </summary>
public sealed class FileAssetTextureLoader
{
    /// <summary>The placeholder color for a missing asset file (also its conventional name).</summary>
    public static readonly Color PlaceholderColor = EditorTheme.PlaceholderMagenta;

    /// <summary>The generated placeholder's square size in pixels — big enough to be unmissable.</summary>
    public const int PlaceholderSize = 32;

    private readonly Func<string, Stream?> _openStream;
    private readonly Func<Stream, Texture2D?> _decode;
    private readonly Func<Texture2D?> _createPlaceholder;
    private readonly Func<string, Texture2D?>? _resolveContentKey;

    private readonly Dictionary<string, Texture2D?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _missing = new();
    private Texture2D? _placeholder;
    private bool _placeholderCreated;

    /// <summary>Production ctor: opens <c>contentRoot/relativePath</c> — through
    /// <c>TitleContainer.OpenStream</c> for a content-relative root, or direct file IO for an
    /// ABSOLUTE root (TitleContainer refuses rooted paths; an absolute root is the editor's
    /// source-content-tree loader, desktop-only by construction) — decodes with
    /// <c>Texture2D.FromStream</c>, and generates the magenta placeholder on
    /// <paramref name="graphicsDevice"/>. <paramref name="resolveContentKey"/> (optional) serves
    /// NON-<c>file:</c> keys (MGCB content keys) so mixed-key consumers — a prefab thumbnail
    /// whose sprite uses a content key — resolve through this one loader.</summary>
    public FileAssetTextureLoader(GraphicsDevice graphicsDevice, string contentRoot,
        Func<string, Texture2D?>? resolveContentKey = null)
        : this(
            relativePath => OpenContentStream(contentRoot, relativePath),
            stream => Texture2D.FromStream(graphicsDevice, stream),
            () => CreateMagentaPlaceholder(graphicsDevice),
            resolveContentKey)
    {
    }

    /// <summary>Test seam: inject the stream/decode/placeholder functions (no GraphicsDevice).
    /// <paramref name="openStream"/> takes the content-root-relative path and may return null or
    /// throw for a missing file; <paramref name="decode"/> turns the stream into a texture;
    /// <paramref name="createPlaceholder"/> builds the shared missing-asset placeholder
    /// (invoked at most once).</summary>
    public FileAssetTextureLoader(
        Func<string, Stream?> openStream,
        Func<Stream, Texture2D?> decode,
        Func<Texture2D?> createPlaceholder,
        Func<string, Texture2D?>? resolveContentKey = null)
    {
        _openStream = openStream ?? throw new ArgumentNullException(nameof(openStream));
        _decode = decode ?? throw new ArgumentNullException(nameof(decode));
        _createPlaceholder = createPlaceholder ?? throw new ArgumentNullException(nameof(createPlaceholder));
        _resolveContentKey = resolveContentKey;
    }

    /// <summary>The production stream opener: <c>TitleContainer</c> for bundled (relative) content;
    /// direct file IO when the combined path is rooted — <c>TitleContainer.OpenStream</c> throws on
    /// absolute paths by contract.</summary>
    internal static Stream? OpenContentStream(string contentRoot, string relativePath)
    {
        var path = Path.Combine(contentRoot, relativePath);
        return Path.IsPathRooted(path) ? File.OpenRead(path) : TitleContainer.OpenStream(path);
    }

    /// <summary>How many times a PNG was actually decoded (test observability for the lazy /
    /// memoized contract: N loads of the same path = 1 decode).</summary>
    public int DecodeCount { get; private set; }

    /// <summary>The content-root-relative paths that failed to load (each recorded once) — these
    /// are showing the magenta placeholder.</summary>
    public IReadOnlyList<string> MissingPaths => _missing;

    /// <summary>The shared missing-asset placeholder (null until first needed; in tests the
    /// injected factory decides what it is).</summary>
    public Texture2D? Placeholder => _placeholder;

    /// <summary>
    /// Resolves the texture for a <c>file:</c> AssetKey (a <c>#region</c> suffix is stripped —
    /// regions share their sheet's texture; the region rectangle lives on the sprite's
    /// <c>Source</c>). Returns the memoized texture, or the magenta placeholder (with a loud
    /// warning, once per path) when the file is missing or undecodable. A non-<c>file:</c> key is
    /// a composition error and also returns the placeholder, loudly.
    /// </summary>
    public Texture2D? Load(string assetKey)
    {
        if (!FileAssetKey.TryParse(assetKey, out var relativePath, out _))
        {
            // An MGCB content key: serve it through the injected content resolver when one is
            // wired (memoized like file loads), else the loud placeholder as before.
            if (_resolveContentKey != null)
            {
                if (_cache.TryGetValue(assetKey, out var cachedContent)) return cachedContent;
                Texture2D? resolved = null;
                try
                {
                    resolved = _resolveContentKey(assetKey);
                }
                catch (Exception ex)
                {
                    Logger.Debug($"[level-editor] Content key '{assetKey}' failed to load: {ex.Message}");
                }
                if (resolved == null)
                {
                    Logger.Warning($"[level-editor] Content key '{assetKey}' did not resolve — " +
                                   "returning the magenta placeholder.");
                    resolved = GetPlaceholder();
                }
                _cache[assetKey] = resolved;
                return resolved;
            }

            Logger.Warning($"[level-editor] '{assetKey}' is not a file: asset key — " +
                           "returning the magenta placeholder.");
            return GetPlaceholder();
        }

        if (_cache.TryGetValue(relativePath!, out var cached)) return cached;

        Texture2D? texture = null;
        try
        {
            using var stream = _openStream(relativePath!);
            if (stream != null)
            {
                texture = _decode(stream);
                DecodeCount++;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"[level-editor] Opening asset file '{relativePath}' failed: {ex.Message}");
        }

        // No readable file: fall back to the MGCB CONTENT key for the same path (extension dropped —
        // "Art/Terrain/Adve.png" → "Art/Terrain/Adve"). This is what makes `file:` keys work on
        // platforms with no filesystem: on the web the PNGs are not there to open, but their built
        // .xnb is, so the SAME scene, sprite sheet and animation keys resolve — the drop folder stays
        // the fast desktop authoring loop, and shipping is "add it to Content.mgcb", not "rewrite
        // every key". Only the placeholder is left for a key that is in neither place.
        if (texture == null && _resolveContentKey != null)
        {
            var contentKey = StripExtension(relativePath!);
            try
            {
                texture = _resolveContentKey(contentKey);
            }
            catch (Exception ex)
            {
                Logger.Debug($"[level-editor] Content fallback '{contentKey}' failed: {ex.Message}");
            }
            if (texture != null)
                Logger.Debug($"[level-editor] '{relativePath}' served from MGCB content '{contentKey}'.");
        }

        if (texture == null)
        {
            Logger.Warning($"[level-editor] Missing asset file '{relativePath}' — showing the " +
                           "magenta placeholder. Restore the file under the asset drop folder " +
                           "and press Refresh (or graduate the key to MGCB content).");
            _missing.Add(relativePath!);
            texture = GetPlaceholder();
        }

        _cache[relativePath!] = texture;
        return texture;
    }

    /// <summary>
    /// Drops the memoized textures and the missing-file record (island-authoring Slice 4 refresh):
    /// the next <see cref="Load"/> of any key re-opens + re-decodes its PNG, so a changed or
    /// newly-dropped file is picked up without an editor restart. The shared magenta placeholder is
    /// kept (it is scene-independent), and <see cref="DecodeCount"/> stays cumulative. The cached
    /// textures are <b>not</b> disposed — already-placed props still reference them through their
    /// <c>SpriteInfoComponent.SpriteSheet</c>; the next load decodes a fresh texture instead.
    /// </summary>
    public void Invalidate()
    {
        _cache.Clear();
        _missing.Clear();
    }

    /// <summary>"Art/Terrain/Adve.png" → "Art/Terrain/Adve" — an asset path's MGCB content key (MGCB
    /// strips the extension when it builds). Paths without one pass through.</summary>
    internal static string StripExtension(string relativePath)
    {
        var dot = relativePath.LastIndexOf('.');
        var slash = relativePath.LastIndexOfAny(new[] { '/', '\\' });
        return dot > slash && dot >= 0 ? relativePath[..dot] : relativePath;
    }

    private Texture2D? GetPlaceholder()
    {
        if (!_placeholderCreated)
        {
            _placeholder = _createPlaceholder();
            _placeholderCreated = true;
        }
        return _placeholder;
    }

    private static Texture2D CreateMagentaPlaceholder(GraphicsDevice device)
    {
        var texture = new Texture2D(device, PlaceholderSize, PlaceholderSize);
        var pixels = new Color[PlaceholderSize * PlaceholderSize];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = PlaceholderColor;
        texture.SetData(pixels);
        return texture;
    }
}
