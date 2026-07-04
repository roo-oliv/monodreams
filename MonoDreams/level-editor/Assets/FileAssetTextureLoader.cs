#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.State;

namespace MonoDreams.LevelEditor.Assets;

/// <summary>
/// Loads the <c>Texture2D</c> behind a <c>file:</c> AssetKey (see <see cref="FileAssetKey"/>) —
/// lazily, memoized per PNG path, through <c>TitleContainer.OpenStream</c> +
/// <c>Texture2D.FromStream</c> (island-authoring plan §2.1; the same content-stream seam the
/// Blender parser uses). The catalog scan never touches this: textures load on first use (arming a
/// palette item, placing, or rehydrating a loaded scene).
///
/// <para><b>Missing file = loud warning + a visible magenta placeholder, never invisible.</b>
/// When the PNG cannot be opened or decoded, <see cref="Load"/> logs a <c>Logger.Warning</c> and
/// returns a shared solid-magenta placeholder texture (generated once), so a scene referencing a
/// pack the checkout has not downloaded shows unmistakable magenta boxes instead of silently
/// dropping sprites. The failed path is recorded in <see cref="MissingPaths"/>.</para>
///
/// <para>The stream/decode/placeholder functions are injectable so the loader is unit-testable
/// without a <c>GraphicsDevice</c> (the production ctor wires TitleContainer +
/// <c>Texture2D.FromStream</c> + a generated 32×32 magenta texture).</para>
/// </summary>
public sealed class FileAssetTextureLoader
{
    /// <summary>The placeholder color for a missing asset file (also its conventional name).</summary>
    public static readonly Color PlaceholderColor = Color.Magenta;

    /// <summary>The generated placeholder's square size in pixels — big enough to be unmissable.</summary>
    public const int PlaceholderSize = 32;

    private readonly Func<string, Stream?> _openStream;
    private readonly Func<Stream, Texture2D?> _decode;
    private readonly Func<Texture2D?> _createPlaceholder;

    private readonly Dictionary<string, Texture2D?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _missing = new();
    private Texture2D? _placeholder;
    private bool _placeholderCreated;

    /// <summary>Production ctor: opens <c>contentRoot/relativePath</c> through
    /// <c>TitleContainer.OpenStream</c>, decodes with <c>Texture2D.FromStream</c>, and generates
    /// the magenta placeholder on <paramref name="graphicsDevice"/>.</summary>
    public FileAssetTextureLoader(GraphicsDevice graphicsDevice, string contentRoot)
        : this(
            relativePath => TitleContainer.OpenStream(Path.Combine(contentRoot, relativePath)),
            stream => Texture2D.FromStream(graphicsDevice, stream),
            () => CreateMagentaPlaceholder(graphicsDevice))
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
        Func<Texture2D?> createPlaceholder)
    {
        _openStream = openStream ?? throw new ArgumentNullException(nameof(openStream));
        _decode = decode ?? throw new ArgumentNullException(nameof(decode));
        _createPlaceholder = createPlaceholder ?? throw new ArgumentNullException(nameof(createPlaceholder));
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

        if (texture == null)
        {
            Logger.Warning($"[level-editor] Missing asset file '{relativePath}' — showing the " +
                           "magenta placeholder. Drop the pack into Content/Island/ " +
                           "(see MANIFEST.md) and restart the editor.");
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
