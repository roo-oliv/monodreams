using System;
using System.IO;
using System.Threading.Tasks;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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

        Directory.CreateDirectory(outputDirectory);
        Logger.Info($"ScreenshotCaptureSystem initialized. Interval: {captureIntervalSeconds}s, output: {outputDirectory}");
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        _timeSinceLastCapture += state.Time;

        if (_timeSinceLastCapture < _captureIntervalSeconds) return;
        if (_pendingSave) return;

        _timeSinceLastCapture = 0f;

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

        // Read backbuffer data (must be on main thread, after FinalDrawSystem)
        _graphicsDevice.GetBackBufferData(_pixelBuffer);
        _stagingTexture.SetData(_pixelBuffer);

        // Encode to PNG on main thread (SaveAsPng needs the graphics context)
        var pngData = new MemoryStream();
        _stagingTexture.SaveAsPng(pngData, width, height);
        var pngBytes = pngData.ToArray();
        pngData.Dispose();

        var counter = _counter++;
        var gameTime = state.TotalTime;
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var filename = $"screenshot_{counter:D6}_gt{gameTime:F2}_{timestamp}.png";
        var filePath = Path.Combine(_outputDirectory, filename);

        _pendingSave = true;
        Task.Run(() =>
        {
            try
            {
                File.WriteAllBytes(filePath, pngBytes);
                Logger.Debug($"Screenshot saved: {filename}");
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

    public void Dispose()
    {
        _stagingTexture?.Dispose();
        GC.SuppressFinalize(this);
    }
}
