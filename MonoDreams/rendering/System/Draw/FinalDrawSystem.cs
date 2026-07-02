using System;
using System.Collections.Generic;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.System.Draw;

/// <summary>
/// One layer in the final composite: a render target drawn to the back buffer at a
/// viewport-resolved destination rectangle with a viewport-resolved sampler. Layers are
/// drawn in list order (later = on top). Use the <see cref="Main"/> / <see cref="UI"/> /
/// <see cref="HUD"/> factories for the standard full-frame layers and <see cref="Overlay"/>
/// for a sub-rectangle layer such as a minimap or CCTV view.
/// </summary>
public sealed record RenderLayer(
    RenderTarget2D Target,
    Func<ViewportManager, Rectangle> Destination,
    Func<ViewportManager, SamplerState> Sampler,
    Func<RenderTarget2D> TargetProvider = null)
{
    /// <summary>
    /// Native-resolution chrome layer (the editor shell): the provided target is composited 1:1
    /// over the whole window (no aspect-fit, no scaling — its pixels ARE screen pixels), point
    /// sampled, above whatever layers precede it in the list. The target comes from a provider
    /// because a native-resolution target is recreated on window resize (a fixed reference would
    /// go stale); a <c>null</c> provider result skips the layer entirely — how the chrome
    /// contributes nothing outside Edit mode.
    /// </summary>
    public static RenderLayer Native(Func<RenderTarget2D> targetProvider) => new(
        null,
        vm => new Rectangle(0, 0, vm.ScreenWidth, vm.ScreenHeight),
        _ => SamplerState.PointClamp,
        targetProvider);

    /// World layer: integer-scaled in pixel-perfect mode, else aspect-fit; linear filtered.
    public static RenderLayer Main(RenderTarget2D target) => new(
        target,
        vm => vm.CurrentScalingMode == ViewportManager.ScalingMode.PixelPerfect
            ? vm.PixelPerfectDestinationRectangle
            : vm.DestinationRectangle,
        _ => SamplerState.LinearClamp);

    /// Screen-space UI layer: aspect-fit; linear in Smooth mode, point otherwise.
    public static RenderLayer UI(RenderTarget2D target) => new(
        target,
        vm => vm.DestinationRectangle,
        vm => vm.CurrentScalingMode == ViewportManager.ScalingMode.Smooth
            ? SamplerState.LinearClamp
            : SamplerState.PointClamp);

    /// Screen-space HUD layer: aspect-fit into the letterboxed viewport (the same
    /// <see cref="ViewportManager.DestinationRectangle"/> as Main/UI), point filtered. HUD content —
    /// including the cursor — is authored in virtual coordinates and positioned via
    /// <see cref="ViewportManager.ScaleMouseToVirtualCoordinates"/>, which inverts the aspect-fit
    /// transform; drawing the layer to the SAME rectangle is what keeps the cursor locked to the
    /// mouse and the HUD undistorted. Stretching it to the whole screen instead scales HUD content
    /// non-uniformly on a non-virtual aspect ratio and desyncs the cursor from the pointer (they
    /// meet only at the screen centre and drift apart toward the edges, by the letterbox amount).
    public static RenderLayer HUD(RenderTarget2D target) => new(
        target,
        vm => vm.DestinationRectangle,
        _ => SamplerState.PointClamp);

    /// Sub-rectangle layer (minimap / CCTV / picture-in-picture). The bounds are in HUD
    /// virtual coordinates (0..VirtualWidth, 0..VirtualHeight) and mapped to the screen the
    /// same way the HUD layer is, so the layer aligns with HUD chrome drawn at those bounds.
    public static RenderLayer Overlay(RenderTarget2D target, Rectangle virtualBounds, SamplerState? sampler = null) => new(
        target,
        vm => MapVirtualToScreen(virtualBounds, vm),
        _ => sampler ?? SamplerState.LinearClamp);

    private static Rectangle MapVirtualToScreen(Rectangle bounds, ViewportManager vm)
    {
        // Map HUD-virtual coordinates into the letterboxed viewport (the same aspect-fit
        // DestinationRectangle the HUD layer draws to), so an overlay aligns with HUD chrome drawn
        // at those coordinates. At a matching aspect ratio DestinationRectangle fills the screen, so
        // this reduces to the full-screen mapping.
        var dest = vm.DestinationRectangle;
        var sx = dest.Width / (float)vm.VirtualWidth;
        var sy = dest.Height / (float)vm.VirtualHeight;
        return new Rectangle(
            dest.X + (int)(bounds.X * sx), dest.Y + (int)(bounds.Y * sy),
            (int)(bounds.Width * sx), (int)(bounds.Height * sy));
    }
}

/// <summary>
/// Composites the per-pass render targets onto the back buffer in order. The screen owns the
/// layer list, so it decides which targets exist, their stacking order, and where each lands —
/// supporting overlays (minimaps, CCTV) and tiled layouts (splitscreen) without changing this
/// system. Each <see cref="MasterRenderSystem"/> instance fills one target; this draws them.
/// </summary>
public sealed class FinalDrawSystem : ISystem<GameState>
{
    private readonly SpriteBatch _spriteBatch;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ViewportManager _viewportManager;
    private readonly IReadOnlyList<RenderLayer> _layers;

    // 1×1 white pixel used to paint the in-viewport background fill, created lazily.
    private Texture2D? _pixel;

    /// Background color painted inside the aspect-fit viewport (the game's backdrop). Settable so
    /// the game shell can pick a project-wide palette (e.g., dark navy demo theme).
    public static Color ClearColor = new(245, 235, 220); // Warm, cozy lofi default

    /// Color of the letter/pillarbox bars — the margins outside the aspect-fit viewport when the
    /// screen aspect ratio differs from the virtual one. Black by default (the conventional
    /// "bars around a centered game" look); the bars are distinct from <see cref="ClearColor"/>.
    public static Color LetterboxColor = Color.Black;

    public bool IsEnabled { get; set; } = true;

    public FinalDrawSystem(
        SpriteBatch spriteBatch,
        GraphicsDevice graphicsDevice,
        ViewportManager viewportManager,
        IReadOnlyList<RenderLayer> layers)
    {
        _spriteBatch = spriteBatch;
        _graphicsDevice = graphicsDevice;
        _viewportManager = viewportManager;
        _layers = layers;
    }

    public void Update(GameState state)
    {
        // Draw to the back buffer. Clear the WHOLE surface to the letterbox color so the
        // margins outside the aspect-fit viewport read as bars, then paint the game backdrop
        // (ClearColor) only inside the aspect-fit viewport. When the screen aspect matches the
        // virtual one, the viewport fills the screen and only ClearColor shows — identical to
        // before; only a mismatched aspect reveals the bars.
        _graphicsDevice.SetRenderTarget(null);
        _graphicsDevice.Clear(LetterboxColor);

        var viewport = _viewportManager.DestinationRectangle;
        _spriteBatch.Begin(
            SpriteSortMode.Immediate,
            BlendState.Opaque,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        _spriteBatch.Draw(Pixel(), viewport, ClearColor);
        _spriteBatch.End();

        foreach (var layer in _layers)
        {
            // A provider-backed layer (RenderLayer.Native) resolves its target per frame — the
            // target may be recreated on resize or absent (null = skip; e.g. chrome outside Edit).
            var target = layer.TargetProvider != null ? layer.TargetProvider() : layer.Target;
            if (target == null) continue;

            _spriteBatch.Begin(
                SpriteSortMode.Immediate,
                BlendState.AlphaBlend,
                layer.Sampler(_viewportManager),
                DepthStencilState.None,
                RasterizerState.CullNone);

            _spriteBatch.Draw(target, layer.Destination(_viewportManager), Color.White);
            _spriteBatch.End();
        }
    }

    private Texture2D Pixel()
    {
        if (_pixel == null)
        {
            _pixel = new Texture2D(_graphicsDevice, 1, 1);
            _pixel.SetData(new[] { Color.White });
        }
        return _pixel;
    }

    public void Dispose() => _pixel?.Dispose();
}
