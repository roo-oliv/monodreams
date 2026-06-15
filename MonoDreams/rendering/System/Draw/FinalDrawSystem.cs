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
    Func<ViewportManager, SamplerState> Sampler)
{
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

    /// Screen-space HUD layer: stretched to the whole screen; point filtered.
    public static RenderLayer HUD(RenderTarget2D target) => new(
        target,
        vm => new Rectangle(0, 0, vm.ScreenWidth, vm.ScreenHeight),
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
        var sx = vm.ScreenWidth / (float)vm.VirtualWidth;
        var sy = vm.ScreenHeight / (float)vm.VirtualHeight;
        return new Rectangle(
            (int)(bounds.X * sx), (int)(bounds.Y * sy),
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

    /// Background color used to clear the final back buffer. Settable so the
    /// game shell can pick a project-wide palette (e.g., dark navy demo theme).
    public static Color ClearColor = new(245, 235, 220); // Warm, cozy lofi default

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
        // Draw to the back buffer; clear the whole screen (including letter/pillarbox areas).
        _graphicsDevice.SetRenderTarget(null);
        _graphicsDevice.Clear(ClearColor);

        foreach (var layer in _layers)
        {
            if (layer.Target == null) continue;

            _spriteBatch.Begin(
                SpriteSortMode.Immediate,
                BlendState.AlphaBlend,
                layer.Sampler(_viewportManager),
                DepthStencilState.None,
                RasterizerState.CullNone);

            _spriteBatch.Draw(layer.Target, layer.Destination(_viewportManager), Color.White);
            _spriteBatch.End();
        }
    }

    public void Dispose() { }
}
