using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;

namespace MonoDreams.Renderer;

/// <summary>
/// Owns the game's TWO coordinate spaces and the mapping between them and the window.
///
/// <para><b>Authoring (layout) space</b> — <see cref="LayoutWidth"/>×<see cref="LayoutHeight"/> — is
/// where every game number lives: entity/UI coordinates, HUD and overlay boxes, and the point
/// <see cref="MapMouse"/> hands back. <b>Render (virtual) space</b> —
/// <see cref="VirtualWidth"/>×<see cref="VirtualHeight"/> — is the pixel size of the per-pass render
/// targets and of the back buffer. <see cref="RenderScale"/> is the single ratio between them, and it
/// is applied in exactly ONE place: the per-pass render cameras this class hands out
/// (<see cref="CreateCamera"/> for world passes, <see cref="LayoutCamera"/> for screen-space ones).
/// Moving a game from 720p to 1080p is therefore a render-resolution change only — no game
/// coordinate, no UI number and no coordinate-carrying test moves.</para>
///
/// <para>The two spaces default to being EQUAL (<see cref="RenderScale"/> = 1), which is the
/// single-space game: every matrix, rectangle and mouse mapping is byte-identical to a
/// ViewportManager that had never heard of layout space. Opting in means passing a layout size that
/// differs from the virtual one — the aspect ratios must match, so the scale stays uniform.</para>
///
/// <para>On top of that it computes the optimal viewport and destination rectangle that keeps the
/// virtual aspect ratio inside the actual window bounds, adding letterboxing or pillarboxing as
/// needed; <see cref="MapMouse"/> inverts exactly that rectangle, which is what makes pointer
/// mapping robust to window resize, letterboxing and the editor's viewport inset for free.</para>
/// </summary>
public class ViewportManager
{
    /// <summary>
    /// Sets the RENDER resolution (the render targets and back buffer) and, optionally, the
    /// AUTHORING resolution. Passing no layout size keeps the two spaces equal — the single-space
    /// game, where this is just "change the resolution". Passing one opts into the two-space model:
    /// the render resolution moves while every authored coordinate stays put.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A non-positive dimension.</exception>
    /// <exception cref="ArgumentException">Layout and virtual aspect ratios differ (the scale would
    /// not be uniform).</exception>
    public void SetResolution(int virtualWidth, int virtualHeight, int? layoutWidth = null, int? layoutHeight = null)
    {
        ApplyResolution(virtualWidth, virtualHeight, layoutWidth ?? virtualWidth, layoutHeight ?? virtualHeight);
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

    /// <param name="game">The owning game (kept for parity with the rest of the renderer plumbing).</param>
    /// <param name="virtualWidth">RENDER resolution width — the render targets' and back buffer's pixel width.</param>
    /// <param name="virtualHeight">RENDER resolution height.</param>
    /// <param name="layoutWidth">AUTHORING width; 0 (the default) means "same as the render width" —
    /// the single-space game, byte-identical to the pre-two-space behaviour.</param>
    /// <param name="layoutHeight">AUTHORING height; 0 means "same as the render height".</param>
    public ViewportManager(Game game, int virtualWidth = 800, int virtualHeight = 600,
        int layoutWidth = 0, int layoutHeight = 0)
    {
        _game = game;
        ApplyResolution(virtualWidth, virtualHeight,
            layoutWidth == 0 ? virtualWidth : layoutWidth,
            layoutHeight == 0 ? virtualHeight : layoutHeight);

        // Initialize screen size (should be updated if window resized)
        ScreenWidth = 800;
        ScreenHeight = 600;

        // Hook into window resize event if possible
        // _game.Window.ClientSizeChanged += (s, e) => MarkDirty(); // Example
    }

    /// <summary>RENDER space height: the pixel height of the per-pass render targets and back buffer.</summary>
    public int VirtualHeight { get; private set; }

    /// <inheritdoc cref="VirtualHeight"/>
    public int VirtualWidth { get; private set; }

    /// <summary>
    /// AUTHORING space width — the coordinate space every game number is written in (entity and UI
    /// positions, HUD/overlay boxes, and what <see cref="MapMouse"/> returns). Equal to
    /// <see cref="VirtualWidth"/> in a single-space game.
    /// </summary>
    public int LayoutWidth { get; private set; }

    /// <inheritdoc cref="LayoutWidth"/>
    public int LayoutHeight { get; private set; }

    /// <summary>
    /// Render pixels per authoring unit (<see cref="VirtualWidth"/> ÷ <see cref="LayoutWidth"/>) —
    /// 1 in a single-space game. Never apply it by hand to a coordinate: the cameras this class
    /// hands out are the one place it is applied. It IS the right factor for sizing a
    /// render target that must hold a layout-sized region at full render fidelity (e.g. a scroll
    /// viewport target: <c>(int)(layoutSize * RenderScale)</c>).
    /// </summary>
    public float RenderScale { get; private set; } = 1f;

    // The shared screen-space camera, created on demand and dropped whenever the resolution changes
    // (Camera's virtual resolution and render scale are immutable by contract).
    private Camera? _layoutCamera;

    /// <summary>
    /// The camera for SCREEN-SPACE passes (UI, HUD, Scroll, and any other non-world target): a
    /// camera parked at the centre of authoring space with zoom 1, so a UI entity authored at layout
    /// point <c>p</c> lands on render pixel <c>p × RenderScale</c>. Pass it to
    /// <c>MasterRenderSystem</c> instead of <c>null</c> for those passes. In a single-space game its
    /// view matrix is exactly <see cref="Matrix.Identity"/>, so passing it changes nothing — which is
    /// what makes the two-space model opt-in with zero behaviour change. Shared and read-mostly:
    /// treat it as immutable (do not move or zoom it).
    /// </summary>
    public Camera LayoutCamera => _layoutCamera ??= CreateLayoutCamera(VirtualWidth, VirtualHeight);

    /// <summary>
    /// A screen-space camera for a pass whose destination is NOT the full render resolution — a
    /// scroll viewport, a picture-in-picture panel, any sub-target whose content is authored in
    /// layout units and sized <c>layoutSize × <see cref="RenderScale"/></c>. Same contract as
    /// <see cref="LayoutCamera"/> (authoring point <c>p</c> → render pixel <c>p × RenderScale</c>,
    /// identity in a single-space game); it exists because a pass's camera virtual resolution must
    /// equal its destination size.
    /// </summary>
    public Camera CreateLayoutCamera(int destinationWidth, int destinationHeight) =>
        new(destinationWidth, destinationHeight, RenderScale)
        {
            // Derived from the render size so the mapping is exact even when the layout size ×
            // RenderScale rounds: (p − centre) × scale + destination/2 collapses to p × scale.
            Position = new Vector2(
                destinationWidth / (2f * RenderScale),
                destinationHeight / (2f * RenderScale)),
        };

    /// <summary>
    /// Creates a WORLD camera for a render pass whose destination is a full render-resolution target:
    /// virtual resolution = the render resolution (the "camera virtual resolution == destination size"
    /// contract) and <see cref="Camera.RenderScale"/> = <see cref="RenderScale"/>, so its
    /// <see cref="Camera.Zoom"/> stays an authoring-space number. Every game/host that builds a camera
    /// should build it here rather than with <c>new Camera(...)</c>, so the scale keeps living in one
    /// place.
    /// </summary>
    public Camera CreateCamera() => new(VirtualWidth, VirtualHeight, RenderScale);

    private void ApplyResolution(int virtualWidth, int virtualHeight, int layoutWidth, int layoutHeight)
    {
        if (virtualWidth <= 0 || virtualHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(virtualWidth),
                $"Render resolution must be positive (got {virtualWidth}x{virtualHeight}).");
        if (layoutWidth <= 0 || layoutHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(layoutWidth),
                $"Authoring resolution must be positive (got {layoutWidth}x{layoutHeight}).");

        var scaleX = virtualWidth / (float)layoutWidth;
        var scaleY = virtualHeight / (float)layoutHeight;
        // A non-uniform ratio would mean two scales — exactly the "the scale lives in one place"
        // invariant this class exists to hold. Refuse it loudly instead of silently distorting.
        // The tolerance is RELATIVE (0.2%) so that rounding a fractional scale to whole render
        // pixels (1280×1.333 → 1707×960) still passes, while a real aspect mismatch (4:3 authored
        // into 16:9, ~33% apart) always throws.
        if (Math.Abs(scaleX - scaleY) > 0.002f * Math.Max(scaleX, scaleY))
            throw new ArgumentException(
                $"Authoring resolution {layoutWidth}x{layoutHeight} and render resolution " +
                $"{virtualWidth}x{virtualHeight} must share an aspect ratio (scale {scaleX} vs {scaleY}).",
                nameof(layoutWidth));

        VirtualWidth = virtualWidth;
        VirtualHeight = virtualHeight;
        LayoutWidth = layoutWidth;
        LayoutHeight = layoutHeight;
        RenderScale = scaleX;
        _layoutCamera = null; // rebuilt on next read against the new resolution
    }

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
    /// <see cref="MapMouse"/> inverts that same rectangle — so compositing
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
    /// Maps physical screen coordinates (e.g. the raw mouse position) into AUTHORING coordinates —
    /// <c>(0,0)</c> to <see cref="LayoutWidth"/>×<see cref="LayoutHeight"/> — by inverting the
    /// present <see cref="DestinationRectangle"/>. Because that rectangle is recomputed from the
    /// current window size and viewport inset, the inversion is robust to window resize,
    /// letter/pillarboxing and the editor's chrome margins for free. Returns <c>null</c> when the
    /// position falls outside the aspect-fit viewport (the bars or the chrome margins), which callers
    /// read as "the pointer is not over the game".
    ///
    /// <para>The result is in authoring space, NOT render space: a render-resolution move leaves
    /// every mapped coordinate (and every test asserting on one) unchanged. Feed it straight to
    /// <c>Camera.VirtualScreenToWorld</c> for world picking.</para>
    /// </summary>
    public Vector2? MapMouse(Vector2 screenPosition)
    {
        if (_dirty) Recalculate();

        // Check if mouse is inside the viewport bounds
        if (!_currentViewport.Bounds.Contains(screenPosition))
        {
            return null;
        }

        float layoutX = (screenPosition.X - _currentViewport.X) / _scaleX;
        float layoutY = (screenPosition.Y - _currentViewport.Y) / _scaleY;

        return new Vector2(layoutX, layoutY);
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

        // Store the destination rectangle and scaling factors. The factors are screen pixels per
        // AUTHORING unit, because they exist only to be inverted by MapMouse — which hands callers
        // back a layout-space point. In a single-space game layout == virtual, so they are the
        // historical values.
        _destinationRectangle = _currentViewport.Bounds; // This is where we draw the final RT
        _scaleX = (float)_destinationRectangle.Width / LayoutWidth;
        _scaleY = (float)_destinationRectangle.Height / LayoutHeight;

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