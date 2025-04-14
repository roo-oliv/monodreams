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
    private readonly Game _game;
    private Viewport _currentViewport;
    private Rectangle _destinationRectangle;
    private float _scaleX = 1.0f;
    private float _scaleY = 1.0f;
    private bool _dirty = true; // Flag to recalculate when screen size changes

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

        float screenWidth = ScreenWidth;
        float screenHeight = ScreenHeight;
        float screenAspectRatio = screenWidth / screenHeight;

        int destWidth;
        int destHeight;

        if (screenAspectRatio > targetAspectRatio) // Screen is wider than virtual (Letterbox)
        {
            destHeight = (int)screenHeight;
            destWidth = (int)(destHeight * targetAspectRatio + 0.5f);
        }
        else // Screen is taller than virtual (Pillarbox) or same aspect ratio
        {
            destWidth = (int)screenWidth;
            destHeight = (int)(destWidth / targetAspectRatio + 0.5f);
        }

        // set up the new viewport centered in the backbuffer
        _currentViewport = new Viewport
        {
            X = (int)((screenWidth / 2f) - (destWidth / 2f)),
            Y = (int)((screenHeight / 2f) - (destHeight / 2f)),
            Width = destWidth,
            Height = destHeight,
            MinDepth = 0,
            MaxDepth = 1
        };

        // Store the destination rectangle and scaling factors
        _destinationRectangle = _currentViewport.Bounds; // This is where we draw the final RT
        _scaleX = (float)_destinationRectangle.Width / VirtualWidth;
        _scaleY = (float)_destinationRectangle.Height / VirtualHeight;

        _dirty = false;

        // Note: We don't set the GraphicsDevice.Viewport here anymore.
        // The MasterRenderSystem sets it when targeting RenderTargets.
        // The FinalDrawSystem sets it to full screen when drawing to back buffer.
    }
}