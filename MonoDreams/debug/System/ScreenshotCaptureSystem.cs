using System;
using System.Collections.Generic;
using System.IO;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Platform;
using MonoDreams.State;

namespace MonoDreams.System.Debug;

public sealed class ScreenshotCaptureSystem : ISystem<GameState>, IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly float _captureIntervalSeconds;
    private readonly string _outputDirectory;

    private float _timeSinceLastCapture;
    private int _counter;
    private bool _pendingSave;
    private Color[] _pixelBuffer;
    private Texture2D _stagingTexture;

    public bool IsEnabled { get; set; }

    public ScreenshotCaptureSystem(GraphicsDevice graphicsDevice, float captureIntervalSeconds, string outputDirectory)
    {
        _graphicsDevice = graphicsDevice;
        _captureIntervalSeconds = captureIntervalSeconds;
        _outputDirectory = outputDirectory;

        PlatformServices.Current.CreateDirectory(outputDirectory);
        Logger.Info($"ScreenshotCaptureSystem initialized. Interval: {captureIntervalSeconds}s, output: {outputDirectory}");
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        _timeSinceLastCapture += state.Time;

        if (_timeSinceLastCapture < _captureIntervalSeconds) return;
        if (_pendingSave) return;

        _timeSinceLastCapture = 0f;

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

    private string MakeFilename(float gameTime)
    {
        var counter = _counter++;
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        return $"screenshot_{counter:D6}_gt{gameTime:F2}_{timestamp}.png";
    }

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
        if (_pixelBuffer == null || _pixelBuffer.Length != width * height)
        {
            _pixelBuffer = new Color[width * height];
            _stagingTexture?.Dispose();
            _stagingTexture = new Texture2D(_graphicsDevice, width, height);
        }

        _graphicsDevice.GetBackBufferData(_pixelBuffer);
        _stagingTexture.SetData(_pixelBuffer);

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
        _stagingTexture.SaveAsPng(pngData, width, height);
        var pngBytes = pngData.ToArray();
        pngData.Dispose();

        return (pngBytes, nonBlank, distinct.Count);
    }

    public void Dispose()
    {
        _stagingTexture?.Dispose();
        GC.SuppressFinalize(this);
    }
}
