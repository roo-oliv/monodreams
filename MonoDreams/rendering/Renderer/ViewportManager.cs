using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MonoDreams.Renderer;

/// <summary>
/// Calculates the optimal viewport and rendering destination rectangle
/// to maintain a virtual aspect ratio within the actual screen bounds,
/// adding letterboxing or pillarboxing as needed.
/// </summary>
public class ViewportManager
{
    // Add configurable virtual resolution
    public void SetVirtualResolution(int width, int height)
    {
        VirtualWidth = width;
        VirtualHeight = height;
        MarkDirty();
    }
    
    // Add preset resolution configurations
    public static readonly (int width, int height)[] PresetResolutions = 
    {
        (1920, 1080), // HD
        (1280, 720),  // HD Ready
        (2560, 1440), // QHD
        (3840, 2160), // UHD/4K
        (800, 600),   // Default/Retro
        (1600, 900),  // Mid-range
    };
    
    // Add scaling mode options
    public enum ScalingMode
    {
        PixelPerfect,    // Integer scaling only
        Smooth,          // Allow fractional scaling
        KeepAspectRatio  // Current behavior (letterbox/pillarbox)
    }
    
    public ScalingMode CurrentScalingMode { get; set; } = ScalingMode.KeepAspectRatio;
    private readonly Game _game;
    private Viewport _currentViewport;
    private Rectangle _destinationRectangle;
    private float _scaleX = 1.0f;
    private float _scaleY = 1.0f;
    private bool _dirty = true; // Flag to recalculate when screen size changes

    // Reserved chrome margins (the editor shell's viewport inset), in physical screen pixels.
    // When any is non-zero the aspect-fit game viewport is computed inside the remaining
    // sub-rectangle instead of the full window. All zero (the default) reproduces the
    // historical full-window letterbox byte-identically.
    private int _insetLeft, _insetTop, _insetRight, _insetBottom;

    private int _integerScale = 1;
    private Rectangle _pixelPerfectDestinationRectangle;

    public int IntegerScale
    {
        get
        {
            if (_dirty) Recalculate();
            return _integerScale;
        }
    }

    // Recalculates lazily like DestinationRectangle, so a read after a resize/inset change is
    // never stale (it used to be a plain auto-property, honest only after another getter ran).
    public Rectangle PixelPerfectDestinationRectangle
    {
        get
        {
            if (_dirty) Recalculate();
            return _pixelPerfectDestinationRectangle;
        }
    }

    public ViewportManager(Game game, int virtualWidth = 800, int virtualHeight = 600)
    {
        _game = game;
        VirtualWidth = virtualWidth;
        VirtualHeight = virtualHeight;

        // Initialize screen size (should be updated if window resized)
        ScreenWidth = 800;
        ScreenHeight = 600;
        
        // Hook into window resize event if possible
        // _game.Window.ClientSizeChanged += (s, e) => MarkDirty(); // Example
    }
    
    public int VirtualHeight { get; private set; }
    public int VirtualWidth { get; private set; }

    // Should be updated externally if the game window resizes
    private int _screenWidth;
    public int ScreenWidth
    {
        get => _screenWidth;
        set { if (_screenWidth != value) { _screenWidth = value; MarkDirty(); } }
    }

    private int _screenHeight;
    public int ScreenHeight
    {
        get => _screenHeight;
        set { if (_screenHeight != value) { _screenHeight = value; MarkDirty(); } }
    }

    /// <summary>
    /// Device pixels per window LOGICAL point (Flutter's devicePixelRatio) — 1 unless the host
    /// enabled a device-resolution backbuffer behind a scaled window (macOS Retina under the
    /// editor run flag; see the level-editor module's <c>EditorHiDpi</c>). When it is >1,
    /// <see cref="ScreenWidth"/>/<see cref="ScreenHeight"/> are DEVICE pixels while OS mouse
    /// coordinates stay logical, so <c>CursorInputSystem</c> multiplies the raw mouse position by
    /// this ratio — keeping the invariant that <c>ScreenPosition</c>, chrome layout/hit-tests, and
    /// the backbuffer all share one space (device pixels). Chrome layout (e.g.
    /// <c>EditorChromeLayout</c>) multiplies its point-based metrics by this ratio so on-screen
    /// physical sizes stay constant while gaining pixel density.
    /// </summary>
    public float DevicePixelRatio { get; set; } = 1f;

    /// <summary>Whether a viewport inset (reserved chrome margins) is currently active.</summary>
    public bool HasViewportInset => _insetLeft != 0 || _insetTop != 0 || _insetRight != 0 || _insetBottom != 0;

    /// <summary>
    /// Reserves chrome margins around the game viewport (the editor shell's inset), in physical
    /// screen pixels. The aspect-fit <see cref="DestinationRectangle"/> (and the pixel-perfect
    /// rectangle) are then computed inside the remaining centered sub-rectangle, and
    /// <see cref="ScaleMouseToVirtualCoordinates"/> inverts that same rectangle — so compositing
    /// and mouse mapping always agree: a click inside the inset viewport maps to the correct
    /// virtual point with no extra math, and a click in the margins maps to <c>null</c> (chrome
    /// consumes it in screen space). All-zero margins are byte-identical to the historical
    /// full-window letterbox. Setting the same values again is a no-op (no recalculation).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Any negative margin.</exception>
    public void SetViewportInset(int left, int top, int right, int bottom)
    {
        if (left < 0 || top < 0 || right < 0 || bottom < 0)
            throw new ArgumentOutOfRangeException(nameof(left),
                $"Viewport inset margins must be non-negative (got {left},{top},{right},{bottom}).");
        if (_insetLeft == left && _insetTop == top && _insetRight == right && _insetBottom == bottom)
            return;
        _insetLeft = left;
        _insetTop = top;
        _insetRight = right;
        _insetBottom = bottom;
        MarkDirty();
    }

    /// <summary>Clears the viewport inset — back to the full-window letterbox.</summary>
    public void ClearViewportInset() => SetViewportInset(0, 0, 0, 0);

    /// <summary>
    /// Gets the calculated viewport that maintains the virtual aspect ratio (letter/pillarboxed).
    /// </summary>
    public Viewport Viewport
    {
        get
        {
            if (_dirty) Recalculate();
            return _currentViewport;
        }
    }

    /// <summary>
    /// Gets the destination rectangle on the screen where the virtual-resolution content should be drawn.
    /// </summary>
    public Rectangle DestinationRectangle
    {
        get
        {
            if (_dirty) Recalculate();
            return _destinationRectangle;
        }
    }

    private void MarkDirty()
    {
        _dirty = true;
    }

    /// <summary>
    /// Converts physical screen coordinates (e.g., raw mouse position) into virtual coordinates
    /// within the virtual resolution space (0,0 to VirtualWidth, VirtualHeight).
    /// Returns null if the screen position is outside the letter/pillarbox viewport.
    /// </summary>
    public Vector2? ScaleMouseToVirtualCoordinates(Vector2 screenPosition)
    {
        if (_dirty) Recalculate();

        // Check if mouse is inside the viewport bounds
        if (!_currentViewport.Bounds.Contains(screenPosition))
        {
            return null;
        }
    
        float virtualX = (screenPosition.X - _currentViewport.X) / _scaleX;
        float virtualY = (screenPosition.Y - _currentViewport.Y) / _scaleY;
    
        return new Vector2(virtualX, virtualY);
    }

    private void Recalculate()
    {
        var targetAspectRatio = VirtualWidth / (float) VirtualHeight;

        // The area available to the game viewport: the whole window minus the viewport-inset
        // margins (the editor shell's chrome). With a zero inset (the default) this IS the whole
        // window, so every computation below is byte-identical to the historical letterbox.
        int availX = _insetLeft;
        int availY = _insetTop;
        int availWidth = Math.Max(1, ScreenWidth - _insetLeft - _insetRight);
        int availHeight = Math.Max(1, ScreenHeight - _insetTop - _insetBottom);

        float screenWidth = availWidth;
        float screenHeight = availHeight;
        float screenAspectRatio = screenWidth / screenHeight;

        int destWidth;
        int destHeight;

        if (screenAspectRatio > targetAspectRatio) // Available area is wider than virtual (Letterbox)
        {
            destHeight = (int)screenHeight;
            destWidth = (int)(destHeight * targetAspectRatio + 0.5f);
        }
        else // Available area is taller than virtual (Pillarbox) or same aspect ratio
        {
            destWidth = (int)screenWidth;
            destHeight = (int)(destWidth / targetAspectRatio + 0.5f);
        }

        // set up the new viewport centered in the available area
        _currentViewport = new Viewport
        {
            X = availX + (int)((screenWidth / 2f) - (destWidth / 2f)),
            Y = availY + (int)((screenHeight / 2f) - (destHeight / 2f)),
            Width = destWidth,
            Height = destHeight,
            MinDepth = 0,
            MaxDepth = 1
        };

        // Store the destination rectangle and scaling factors
        _destinationRectangle = _currentViewport.Bounds; // This is where we draw the final RT
        _scaleX = (float)_destinationRectangle.Width / VirtualWidth;
        _scaleY = (float)_destinationRectangle.Height / VirtualHeight;

        // Calculate integer scale for pixel-perfect mode (within the same available area).
        // NOTE: assign the backing fields directly — reading the lazy properties here would
        // recurse (we are inside Recalculate; _dirty is still true).
        if (CurrentScalingMode == ScalingMode.PixelPerfect)
        {
            int scaleX = availWidth / VirtualWidth;
            int scaleY = availHeight / VirtualHeight;
            _integerScale = Math.Max(1, Math.Min(scaleX, scaleY));

            int ppWidth = VirtualWidth * _integerScale;
            int ppHeight = VirtualHeight * _integerScale;
            int ppX = availX + (availWidth - ppWidth) / 2;
            int ppY = availY + (availHeight - ppHeight) / 2;

            _pixelPerfectDestinationRectangle = new Rectangle(ppX, ppY, ppWidth, ppHeight);
        }
        else
        {
            _pixelPerfectDestinationRectangle = _destinationRectangle;
        }

        _dirty = false;

        // Note: We don't set the GraphicsDevice.Viewport here anymore.
        // The MasterRenderSystem sets it when targeting RenderTargets.
        // The FinalDrawSystem sets it to full screen when drawing to back buffer.
    }
}