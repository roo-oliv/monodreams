#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component.Draw;
using MonoDreams.Platform;
using MonoDreams.State;
using MonoDreams.System.Draw;

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
/// reads — and in <see cref="CaptureTarget"/> mode, the named render target must be UNBOUND, which
/// the composite is also what guarantees.</para>
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
    private readonly RenderTargetID? _captureTarget;

    /// <summary>The render target last announced for <see cref="_captureTarget"/> by
    /// <c>MasterRenderSystem.RenderedTargetSink</c>, or null until a pass for that id has run since
    /// the previous capture. Written by <see cref="OnTargetRendered"/> (latch) and by the read path
    /// (cleared after every read, and dropped by <see cref="ResolveSourceTarget"/> when the screen has
    /// disposed it); touched only on the main thread. It can outlive the frame that published it —
    /// an interval capture reads it many frames later — which is exactly why both sides treat a
    /// disposed target as no target.</summary>
    private RenderTarget2D? _resolvedTarget;

    /// <summary>Whether the "no pass rendered that target" warning has already been logged — the
    /// condition is per-frame, so an unwarned run would repeat it 60 times a second.</summary>
    private bool _missingTargetWarned;

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

    /// <summary>
    /// The render target this instance reads, or <c>null</c> for the window backbuffer (the default
    /// and the historical behaviour). A named target is captured at ITS OWN fixed resolution, so the
    /// file geometry is independent of the window size, of a resize mid-run, and of letter/pillarboxing
    /// — which is what makes a pixel coordinate an agent noted in one run mean the same thing in the
    /// next, on another machine. It also isolates a single layer (just <c>UI</c>) for sharper
    /// assertions.
    /// </summary>
    public RenderTargetID? CaptureTarget => _captureTarget;

    public ScreenshotCaptureSystem(GraphicsDevice graphicsDevice, float captureIntervalSeconds,
        string outputDirectory, CaptureFormat format = CaptureFormat.Png, int maxFrames = 0,
        RenderTargetID? captureTarget = null)
    {
        _graphicsDevice = graphicsDevice;
        _captureIntervalSeconds = captureIntervalSeconds;
        _outputDirectory = outputDirectory;
        _format = format;
        _maxFrames = maxFrames;
        _captureTarget = captureTarget;

        // Target capture is the ONLY reason to observe the render passes, so the socket is plugged
        // exactly when one was asked for — an unset target leaves the sink untouched and the render
        // path pays nothing but the null check it already had.
        if (_captureTarget != null)
            MasterRenderSystem.RenderedTargetSink += OnTargetRendered;

        PlatformServices.Current.CreateDirectory(outputDirectory);
        Logger.Info($"ScreenshotCaptureSystem initialized. Format: {format}, " +
                    $"interval: {(captureIntervalSeconds <= 0f ? "every frame" : captureIntervalSeconds + "s")}, " +
                    $"source: {DescribeSource(captureTarget)}, " +
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
    ///   <item><c>MONODREAMS_SCREENSHOT_TARGET=window|Main|UI|HUD|Scroll|Editor</c> — what to read.
    ///   <c>window</c> (the default) is the backbuffer, i.e. today's behaviour byte for byte; a
    ///   <see cref="RenderTargetID"/> name reads that pass's fixed-resolution target instead, so the
    ///   file geometry stops following the window.</item>
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

        // What to read: the window backbuffer (default, unchanged) or one named render target. An
        // unreadable value refuses the whole capture rather than quietly falling back to the window —
        // a run that captures the wrong surface produces evidence that LOOKS right and is not
        // comparable with anything.
        if (!TryParseCaptureTarget(
                PlatformServices.Current.GetEnvironmentVariable("MONODREAMS_SCREENSHOT_TARGET"),
                out var captureTarget))
            return null;

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

        return new ScreenshotCaptureSystem(graphicsDevice, interval, outputDirectory, format, maxFrames,
            captureTarget)
        {
            IsEnabled = true,
        };
    }

    /// <summary>
    /// Parses <c>MONODREAMS_SCREENSHOT_TARGET</c>: unset/blank/<c>window</c> ⇒ the backbuffer
    /// (<c>null</c> target, today's behaviour), a <see cref="RenderTargetID"/> NAME (case-insensitive)
    /// ⇒ that target. Returns false — the whole capture is refused — for anything else, after logging
    /// what the valid values are. The match is against the enum's NAMES only, deliberately not
    /// <c>Enum.TryParse</c>: that would additionally accept <c>"0"</c> as <c>Main</c> and read
    /// <c>"Main,UI"</c> as a flags-style OR that lands on some unrelated member, and an env protocol
    /// whose values are numeric aliases is one nobody can read back out of a log.
    /// </summary>
    private static bool TryParseCaptureTarget(string? requested, out RenderTargetID? target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(requested)) return true;

        var trimmed = requested.Trim();
        if (string.Equals(trimmed, "window", StringComparison.OrdinalIgnoreCase)) return true;

        foreach (var name in Enum.GetNames<RenderTargetID>())
        {
            if (!string.Equals(trimmed, name, StringComparison.OrdinalIgnoreCase)) continue;
            target = Enum.Parse<RenderTargetID>(name);
            return true;
        }

        Logger.Error($"MONODREAMS_SCREENSHOT_TARGET='{requested}' is not a capture source — capturing " +
                     $"nothing. Valid values: window, {string.Join(", ", Enum.GetNames<RenderTargetID>())}.");
        return false;
    }

    /// <summary>The human-readable source name used in the init log line — the only place a run
    /// records what its files are pictures OF.</summary>
    private static string DescribeSource(RenderTargetID? captureTarget) =>
        captureTarget == null ? "window backbuffer" : $"{captureTarget} render target";

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

        // Null = the requested render target has not been rendered (yet): skip this capture without
        // consuming a counter, and try again next tick. Window mode never returns null.
        if (Grab() is not { } frame) return;
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
    /// Synchronously captures the current source — the backbuffer, or <see cref="CaptureTarget"/>
    /// when one was named — to a PNG in the output directory and returns whether the frame is
    /// non-blank (contains more than one distinct colour). Unlike <see cref="Update"/>, the file is
    /// fully written before this method returns, so a headless run can capture a verifiable frame on
    /// a chosen game frame and exit immediately afterwards without dropping the save. Returns false
    /// (having written nothing) when a named target has not been rendered.
    /// </summary>
    public bool CaptureNow(float gameTime)
    {
        if (Grab() is not { } frame) return false;
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

    // ── The source: window backbuffer, or one named render target ────────────────────────────────

    /// <summary>
    /// Records the render target a pass just drew into, when it is the one this instance captures.
    /// Installed on <c>MasterRenderSystem.RenderedTargetSink</c> only when a target was named.
    ///
    /// <para>The FIRST publisher since the last read wins, because a screen may run several passes
    /// for one id — the camera demo renders <c>Main</c> twice, once for the world and once for the
    /// minimap — and the first is the primary pass every screen lists first in its composite. The
    /// slot is cleared after each read so the next capture re-resolves from that frame's passes.</para>
    ///
    /// <para><b>A latched target that has since been DISPOSED is not a publisher, it is a corpse, and
    /// it loses to the next one.</b> Between two reads — every frame of the 0.5s a PNG interval waits,
    /// for instance — a screen switch or a window resize tears the latched target down and builds a
    /// new one; a plain <c>??=</c> would refuse every replacement and pin the capture to the dead
    /// object for the rest of the run. This check is what keeps "the slot is re-resolved from the
    /// passes that ran" true across a target's lifetime, not just across a frame.</para>
    /// </summary>
    private void OnTargetRendered(RenderTargetID id, RenderTarget2D target)
    {
        if (id != _captureTarget) return;
        if (_resolvedTarget is null or { IsDisposed: true }) _resolvedTarget = target;
    }

    /// <summary>
    /// The target the next read should use, or <c>null</c> when there is none to read this tick —
    /// having logged why, at most once per gap.
    ///
    /// <para>Dropping a disposed latch is the second half of the invalidation protocol
    /// <see cref="OnTargetRendered"/> starts: a target torn down AFTER the last publish of the frame
    /// (a screen switch, or the resize that makes the editor chrome rebuild its target) would
    /// otherwise be read dead. Clearing the slot rather than merely refusing it is what lets the next
    /// pass latch a fresh one — the read path and the publish path must agree that a disposed target
    /// is no target at all, or the capture stalls permanently.</para>
    /// </summary>
    internal RenderTarget2D? ResolveSourceTarget()
    {
        if (_resolvedTarget is { IsDisposed: true })
        {
            _resolvedTarget = null;
            // Debug, not Warning: this is a normal, self-healing event (one skipped capture at most) —
            // the next pass that draws the id republishes and the capture resumes.
            Logger.Debug($"Frame capture: the {_captureTarget} render target was torn down (a screen " +
                         "switch or a resize) — re-resolving from the next pass that draws it.");
            return null;
        }

        if (_resolvedTarget == null && !_missingTargetWarned)
        {
            _missingTargetWarned = true;
            Logger.Warning($"Frame capture: no render pass has drawn the {_captureTarget} target " +
                           "— capturing nothing until one does. Either the current screen has no " +
                           $"{_captureTarget} pass, or the capture runs before it.");
        }

        return _resolvedTarget;
    }

    /// <summary>
    /// Reads the current source into <see cref="_pixelBuffer"/> (grown/shrunk to fit) and returns its
    /// geometry, or <c>null</c> when a named target has not been rendered since the last read — in
    /// which case nothing is captured this tick and the caller tries again on the next one.
    ///
    /// <para>Reading a render target rather than the backbuffer is what makes the file's geometry the
    /// TARGET's fixed resolution: unaffected by window size, by a resize mid-run, and by
    /// letter/pillarboxing. It is safe for exactly the reason the composite itself is — the target is
    /// unbound by then (<c>FinalDrawSystem</c> sets the device back to the backbuffer and samples the
    /// targets as textures), and this must run after that composite for the same reason.</para>
    /// </summary>
    private (int width, int height)? ReadSourcePixels()
    {
        int width, height;
        RenderTarget2D? target = null;

        if (_captureTarget != null)
        {
            // Null = no pass has published a live target for this id (a screen whose pipeline has no
            // pass for it never will), or the latched one was torn down and the slot has just been
            // dropped so the next pass can refill it. Either way: nothing to read this tick.
            target = ResolveSourceTarget();
            if (target == null) return null;
            width = target.Width;
            height = target.Height;
        }
        else
        {
            var pp = _graphicsDevice.PresentationParameters;
            width = pp.BackBufferWidth;
            height = pp.BackBufferHeight;
        }

        var pixels = width * height;
        if (_pixelBuffer.Length != pixels) _pixelBuffer = new Color[pixels];

        if (target != null)
        {
            // A target the device cannot hand back as RGBA (an exotic surface format, or one still
            // bound because the capture was registered before the composite) would otherwise throw
            // out of Draw once per frame and take an unattended run down. Stop the capture instead,
            // loudly — the same valve the raw write-failure path uses.
            try
            {
                target.GetData(_pixelBuffer);
            }
            catch (Exception ex)
            {
                _stopped = true;
                Logger.Error($"Frame capture stopped: the {_captureTarget} render target could not be " +
                             $"read back ({ex.GetType().Name}: {ex.Message}). A target that is still " +
                             "bound (capture registered before the composite) or not in the Color " +
                             "surface format is the usual cause.");
                return null;
            }
        }
        else _graphicsDevice.GetBackBufferData(_pixelBuffer);

        // Re-resolve from next frame's passes; a warning is per-gap, not per-run.
        _resolvedTarget = null;
        _missingTargetWarned = false;
        return (width, height);
    }

    // ── Raw ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the source verbatim, on the main thread, in the time a memcpy takes.
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
        // Null = the requested render target has not been rendered (yet): no blob, no counter, and the
        // next frame tries again. Window mode never returns null.
        var geometry = ReadSourcePixels();
        if (geometry == null) return;
        var (width, height) = geometry.Value;
        var pixels = width * height;

        if (_rawBytes.Length != pixels * 4) _rawBytes = new byte[pixels * 4];

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
    /// Reads the source (backbuffer, or <see cref="CaptureTarget"/> when one was named), encodes it to
    /// PNG, and computes a cheap blank/non-blank metric. Must run on the main thread after
    /// <c>FinalDrawSystem</c> has composited the frame (the read-back and SaveAsPng both need the
    /// graphics context, and a named target must be unbound). Returns null when a named target has
    /// not been rendered — the caller skips the capture.
    /// </summary>
    private (byte[] png, bool nonBlank, int distinct)? Grab()
    {
        var geometry = ReadSourcePixels();
        if (geometry == null) return null;
        var (width, height) = geometry.Value;

        // The staging texture follows the source geometry — recreated when it changes (a window
        // resize, or a first capture after raw mode, which never builds one).
        if (_stagingTexture == null || _stagingTexture.Width != width || _stagingTexture.Height != height)
        {
            _stagingTexture?.Dispose();
            _stagingTexture = new Texture2D(_graphicsDevice, width, height);
        }
        var staging = _stagingTexture;

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
        // Unplug from the render socket: a disposed capture must not keep a screen's targets — or
        // itself — reachable from a static delegate. Symmetric with the constructor's subscribe, so a
        // window-mode instance never touched the sink and never touches it here.
        if (_captureTarget != null)
            MasterRenderSystem.RenderedTargetSink -= OnTargetRendered;
        _resolvedTarget = null;
        _stagingTexture?.Dispose();
        GC.SuppressFinalize(this);
    }
}
