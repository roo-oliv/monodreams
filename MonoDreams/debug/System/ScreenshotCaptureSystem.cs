#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Platform;
using MonoDreams.State;

namespace MonoDreams.System.Debug;

/// <summary>How a captured frame reaches the disk.</summary>
public enum CaptureFormat
{
    /// <summary>
    /// One PNG per capture, encoded off the main thread. Small files an agent or a human can open, and
    /// the only sensible choice for the occasional verification shot.
    ///
    /// <para><b>It cannot keep up with the game.</b> Encoding a 1280x720 PNG costs roughly 50ms — far
    /// more than a 16.6ms frame — and under MonoGame's fixed timestep the host answers a long draw by
    /// running about four simulation updates per draw. So a capture at every frame drags the observed
    /// capture rate down to ~15-26 fps and the "video" it produces is of a game running in slow
    /// motion. That is what <see cref="Raw"/> exists for.</para>
    /// </summary>
    Png,

    /// <summary>
    /// One uncompressed RGBA8888 blob per frame, written SYNCHRONOUSLY, with no encode and no
    /// per-pixel conversion — the backbuffer's <c>Color[]</c> is reinterpreted as bytes with
    /// <c>MemoryMarshal.AsBytes</c> and memcpy'd out. Sustains ~59.8 fps at 1280x720, which is
    /// ~220 MB/s.
    ///
    /// <para><b>It is a firehose.</b> 1280x720x4 = 3.5 MiB a frame, so a 20-second take is about
    /// 4.2 GB. Capture to a scratch directory (<c>MONODREAMS_DEBUG_DIR</c>), assemble, delete — and
    /// cap the run with <c>MONODREAMS_SCREENSHOT_MAX_FRAMES</c>. See the "Frame capture" section of
    /// <c>MonoDreams/debug/docs/overview.md</c> for the env contract and the disk-cost table.</para>
    /// </summary>
    Raw,
}

/// <summary>
/// Dumps composited frames to the debug directory. Registered ONLY when the environment asks for it —
/// see <see cref="FromEnvironment"/> — because constructing it has side effects (it mkdirs and logs),
/// so a shipped run that captures nothing should not be building one.
///
/// <para>Must run after the final composite (<c>FinalDrawSystem</c>): the backbuffer is what it
/// reads.</para>
/// </summary>
public sealed class ScreenshotCaptureSystem : ISystem<GameState>, IDisposable
{
    /// <summary>How often the byte total is reported in <see cref="CaptureFormat.Raw"/> — the mode is
    /// capable of filling a disk inside a minute, so it says so as it goes.</summary>
    private const int RawReportEveryFrames = 120;

    private readonly GraphicsDevice _graphicsDevice;
    private readonly float _captureIntervalSeconds;
    private readonly string _outputDirectory;
    private readonly CaptureFormat _format;
    private readonly int _maxFrames;

    private float _timeSinceLastCapture;
    private int _counter;
    private bool _pendingSave;
    private long _bytesWritten;
    private bool _stopped;
    /// <summary>Grown to the backbuffer on first use — empty rather than null so the resize test is a
    /// length comparison and nothing has to ask whether the buffer exists yet.</summary>
    private Color[] _pixelBuffer = [];
    private Texture2D? _stagingTexture;

    /// <summary>Reused destination for the raw memcpy, so a 60 fps capture allocates nothing per
    /// frame.</summary>
    private byte[] _rawBytes = [];

    public bool IsEnabled { get; set; }

    /// <summary>Which format this instance writes — the caller's cue that the annotation and the
    /// interval mean different things in each.</summary>
    public CaptureFormat Format => _format;

    public ScreenshotCaptureSystem(GraphicsDevice graphicsDevice, float captureIntervalSeconds,
        string outputDirectory, CaptureFormat format = CaptureFormat.Png, int maxFrames = 0)
    {
        _graphicsDevice = graphicsDevice;
        _captureIntervalSeconds = captureIntervalSeconds;
        _outputDirectory = outputDirectory;
        _format = format;
        _maxFrames = maxFrames;

        PlatformServices.Current.CreateDirectory(outputDirectory);
        Logger.Info($"ScreenshotCaptureSystem initialized. Format: {format}, " +
                    $"interval: {(captureIntervalSeconds <= 0f ? "every frame" : captureIntervalSeconds + "s")}, " +
                    $"output: {outputDirectory}" +
                    (maxFrames > 0 ? $", stopping after {maxFrames} frames" : ""));
        if (format == CaptureFormat.Raw)
            Logger.Warning("Raw frame capture is ~3.5 MiB per frame at 1280x720 (~220 MB/s at 60 fps). " +
                           "Capture to a scratch dir, assemble, then delete it — and cap the run with " +
                           "MONODREAMS_SCREENSHOT_MAX_FRAMES.");
    }

    /// <summary>
    /// Builds the capture system the environment asks for, or null when it asks for nothing.
    ///
    /// <list type="bullet">
    ///   <item><c>MONODREAMS_SCREENSHOT=1</c> (or <c>png</c>) — PNGs every 0.5s. The historical
    ///   behaviour, unchanged.</item>
    ///   <item><c>MONODREAMS_SCREENSHOT=raw</c> — raw RGBA every frame, for 60 fps video.</item>
    ///   <item><c>MONODREAMS_SCREENSHOT_INTERVAL=&lt;seconds&gt;</c> — overrides the interval of either
    ///   (0 = every frame).</item>
    ///   <item><c>MONODREAMS_SCREENSHOT_MAX_FRAMES=&lt;n&gt;</c> — stop after n frames. The safety
    ///   valve on raw mode: a forgotten capture fills a disk, and 600 frames is ten seconds.</item>
    /// </list>
    ///
    /// <para>Keeping the whole contract here rather than in each screen is deliberate: it is one
    /// environment protocol, and a second reader of it would be a second dialect of it.</para>
    /// </summary>
    public static ScreenshotCaptureSystem? FromEnvironment(GraphicsDevice graphicsDevice, string outputDirectory)
    {
        var requested = PlatformServices.Current.GetEnvironmentVariable("MONODREAMS_SCREENSHOT");
        if (string.IsNullOrWhiteSpace(requested)) return null;

        CaptureFormat format;
        switch (requested.Trim().ToLowerInvariant())
        {
            case "0":
            case "off":
                return null;
            case "1":
            case "png":
                format = CaptureFormat.Png;
                break;
            case "raw":
            case "rgba":
                format = CaptureFormat.Raw;
                break;
            default:
                Logger.Error($"MONODREAMS_SCREENSHOT='{requested}' is not a capture mode — capturing " +
                             "nothing. Valid modes: 1 (or png), raw.");
                return null;
        }

        // Raw defaults to EVERY frame: the whole point of the mode is a 60 fps take, and an interval
        // would silently make it a slideshow.
        var interval = format == CaptureFormat.Raw ? 0f : 0.5f;
        var overridden = PlatformServices.Current.GetEnvironmentVariable("MONODREAMS_SCREENSHOT_INTERVAL");
        if (!string.IsNullOrWhiteSpace(overridden)
            && float.TryParse(overridden, global::System.Globalization.NumberStyles.Float,
                global::System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            && parsed >= 0f)
            interval = parsed;

        var maxFrames = 0;
        var cap = PlatformServices.Current.GetEnvironmentVariable("MONODREAMS_SCREENSHOT_MAX_FRAMES");
        if (!string.IsNullOrWhiteSpace(cap) && int.TryParse(cap, out var parsedCap) && parsedCap > 0)
            maxFrames = parsedCap;

        return new ScreenshotCaptureSystem(graphicsDevice, interval, outputDirectory, format, maxFrames)
        {
            IsEnabled = true,
        };
    }

    public void Update(GameState state)
    {
        if (!IsEnabled || _stopped) return;

        _timeSinceLastCapture += state.Time;
        if (_timeSinceLastCapture < _captureIntervalSeconds) return;
        _timeSinceLastCapture = 0f;

        if (_maxFrames > 0 && _counter >= _maxFrames)
        {
            _stopped = true;
            Logger.Info($"Frame capture stopped at the {_maxFrames}-frame cap " +
                        $"({_bytesWritten / (1024.0 * 1024.0):0.#} MiB written).");
            return;
        }

        if (_format == CaptureFormat.Raw) { CaptureRaw(state.TotalTime); return; }

        if (_pendingSave) return;

        var frame = Grab();
        var filename = MakeFilename(state.TotalTime);
        var filePath = PlatformServices.Current.CombinePath(_outputDirectory, filename);

        _pendingSave = true;
        // Fire-and-forget: desktop runs this on a thread-pool thread; a single-threaded
        // host (WASM) runs it inline. Either way the save is best-effort, off the
        // deterministic CaptureNow path.
        PlatformServices.Current.RunBackground(() =>
        {
            try
            {
                PlatformServices.Current.WriteAllBytes(filePath, frame.png);
                Logger.Debug($"Screenshot saved: {filename} (nonBlank={frame.nonBlank}, distinctColors={frame.distinct})");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to save screenshot: {ex.Message}");
            }
            finally
            {
                _pendingSave = false;
            }
        });
    }

    /// <summary>
    /// Synchronously captures the current backbuffer to a PNG in the output directory
    /// and returns whether the frame is non-blank (contains more than one distinct
    /// colour). Unlike <see cref="Update"/>, the file is fully written before this
    /// method returns, so a headless run can capture a verifiable frame on a chosen
    /// game frame and exit immediately afterwards without dropping the save.
    /// </summary>
    public bool CaptureNow(float gameTime)
    {
        var frame = Grab();
        var filename = MakeFilename(gameTime);
        var filePath = PlatformServices.Current.CombinePath(_outputDirectory, filename);

        try
        {
            PlatformServices.Current.WriteAllBytes(filePath, frame.png);
            Logger.Info($"Screenshot saved: {filename} (nonBlank={frame.nonBlank}, distinctColors={frame.distinct})");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to save screenshot: {ex.Message}");
            return false;
        }

        return frame.nonBlank;
    }

    /// <summary>Optional filename annotation (e.g. camera position/zoom) — evaluated per capture
    /// and embedded in the name, so an agent reading the shots can map pixels to world space
    /// without reverse-engineering the view. Keep the returned string filename-safe.
    ///
    /// <para><b>PNG mode only.</b> Raw frame names carry the geometry and the timestamp a video
    /// assembler needs and nothing else — an annotation there would be a per-frame parsing hazard for
    /// no reader.</para></summary>
    public Func<string>? AnnotateFilename { get; set; }

    private string MakeFilename(float gameTime)
    {
        var counter = _counter++;
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var annotation = AnnotateFilename?.Invoke();
        var suffix = string.IsNullOrEmpty(annotation) ? "" : $"_{annotation}";
        return $"screenshot_{counter:D6}_gt{gameTime:F2}{suffix}_{timestamp}.png";
    }

    // ── Raw ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the backbuffer verbatim, on the main thread, in the time a memcpy takes.
    ///
    /// <para><b>Everything expensive that PNG mode does is skipped, and each omission was measured.</b>
    /// No <c>SaveAsPng</c> (the encode that caps PNG mode at ~26 fps); no staging <c>Texture2D</c> and
    /// therefore no <c>SetData</c> upload of 3.5 MiB per frame; no distinct-colour <c>HashSet</c> pass
    /// over 921,600 pixels. What is left is <c>GetBackBufferData</c>, one reinterpreting memcpy, and the
    /// write — and it is SYNCHRONOUS on purpose, because a background write cannot keep up with the
    /// producer and a queue of 3.5 MiB buffers is how a capture ends in an OOM rather than a video.</para>
    ///
    /// <para>The reinterpret is the load-bearing trick: <c>Color</c> is a packed RGBA byte quad, so
    /// <c>MemoryMarshal.AsBytes</c> over the pixel span IS the file's contents. A conversion loop over
    /// nearly a million pixels costs more than everything else here put together.</para>
    /// </summary>
    private void CaptureRaw(float gameTime)
    {
        var pp = _graphicsDevice.PresentationParameters;
        var width = pp.BackBufferWidth;
        var height = pp.BackBufferHeight;
        var pixels = width * height;

        if (_pixelBuffer.Length != pixels)
        {
            _pixelBuffer = new Color[pixels];
            _rawBytes = new byte[pixels * 4];
        }

        _graphicsDevice.GetBackBufferData(_pixelBuffer);
        MemoryMarshal.AsBytes(_pixelBuffer.AsSpan(0, pixels)).CopyTo(_rawBytes);

        // Geometry and timestamp in the NAME, so the directory is self-describing and needs no manifest
        // to fall out of step with it. Milliseconds as an integer rather than a formatted float: a
        // decimal separator is culture-dependent and would make the tool's parse machine-specific.
        var filename = $"raw_{_counter:D6}_{width}x{height}_{(int)MathF.Round(gameTime * 1000f):D8}.rgba";
        var filePath = PlatformServices.Current.CombinePath(_outputDirectory, filename);

        try
        {
            PlatformServices.Current.WriteAllBytes(filePath, _rawBytes);
        }
        catch (Exception ex)
        {
            _stopped = true;
            Logger.Error($"Raw frame capture stopped: {ex.Message} " +
                         $"({_counter} frames, {_bytesWritten / (1024.0 * 1024.0):0.#} MiB written). " +
                         "A full disk is the usual cause.");
            return;
        }

        _counter++;
        _bytesWritten += _rawBytes.Length;
        if (_counter % RawReportEveryFrames == 0)
            Logger.Info($"Raw capture: {_counter} frames, {_bytesWritten / (1024.0 * 1024.0):0.#} MiB " +
                        $"({width}x{height}).");
    }

    // ── PNG ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the backbuffer, encodes it to PNG, and computes a cheap blank/non-blank
    /// metric. Must run on the main thread after <c>FinalDrawSystem</c> has composited
    /// the frame (GetBackBufferData + SaveAsPng both need the graphics context).
    /// </summary>
    private (byte[] png, bool nonBlank, int distinct) Grab()
    {
        var pp = _graphicsDevice.PresentationParameters;
        int width = pp.BackBufferWidth;
        int height = pp.BackBufferHeight;

        // Lazily allocate or resize buffers
        if (_pixelBuffer.Length != width * height)
        {
            _pixelBuffer = new Color[width * height];
            _stagingTexture?.Dispose();
            _stagingTexture = new Texture2D(_graphicsDevice, width, height);
        }
        // Raw mode never builds one, so a process that switched modes (or a resize) needs it now.
        var staging = _stagingTexture ??= new Texture2D(_graphicsDevice, width, height);

        _graphicsDevice.GetBackBufferData(_pixelBuffer);
        staging.SetData(_pixelBuffer);

        // Cheap blank detection: a frame is non-blank if any pixel differs from the
        // first one. We also count distinct colours (capped) so the headless log
        // carries a coarse "how much was drawn" signal that a test can assert on.
        var nonBlank = false;
        var first = _pixelBuffer[0];
        var distinct = new HashSet<uint>();
        foreach (var c in _pixelBuffer)
        {
            if (c != first) nonBlank = true;
            if (distinct.Count < 256) distinct.Add(c.PackedValue);
        }

        var pngData = new MemoryStream();
        staging.SaveAsPng(pngData, width, height);
        var pngBytes = pngData.ToArray();
        pngData.Dispose();

        return (pngBytes, nonBlank, distinct.Count);
    }

    public void Dispose()
    {
        if (_format == CaptureFormat.Raw && _counter > 0)
            Logger.Info($"Raw capture finished: {_counter} frames, " +
                        $"{_bytesWritten / (1024.0 * 1024.0):0.#} MiB in {_outputDirectory}.");
        _stagingTexture?.Dispose();
        GC.SuppressFinalize(this);
    }
}
