using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Draw;

public sealed class FinalDrawSystem : ISystem<GameState>
 {
     private readonly SpriteBatch _spriteBatch;
     private readonly GraphicsDevice _graphicsDevice;
     private readonly ViewportManager _viewportManager;
     private readonly IReadOnlyDictionary<RenderTargetID, RenderTarget2D> _renderTargets;
     // Define the order and how to draw your RTs (drawn later = on top)
     private readonly RenderTargetID[] _renderTargetsToDraw = [
         RenderTargetID.Main,  // Back (game world with camera)
         RenderTargetID.UI,    // Middle
         RenderTargetID.HUD,   // Front (cursor, always on top)
     ];
     private readonly MonoDreams.Component.Camera _camera;
     private static readonly Color ClearColor = new(245, 235, 220); // Warm, cozy lofi background

     public bool IsEnabled { get; set; } = true;


     public FinalDrawSystem(SpriteBatch spriteBatch, GraphicsDevice graphicsDevice, ViewportManager viewportManager, MonoDreams.Component.Camera camera, IReadOnlyDictionary<RenderTargetID, RenderTarget2D> renderTargets)
     {
         _spriteBatch = spriteBatch;
         _graphicsDevice = graphicsDevice;
         _viewportManager = viewportManager;
         _camera = camera;
         _renderTargets = renderTargets;
     }

     public void Update(GameState state)
     {
        // Ensure we are drawing to the back buffer
        _graphicsDevice.SetRenderTarget(null);
        // Clear the entire screen (including letter/pillarbox areas)
        _graphicsDevice.Clear(ClearColor);

        // Draw each render target with appropriate sampler and destination
        foreach (var targetId in _renderTargetsToDraw)
        {
            if (!_renderTargets.TryGetValue(targetId, out var renderTarget) || renderTarget == null)
                continue;

            Rectangle destinationRect;
            SamplerState samplerState;

            switch (targetId)
            {
                case RenderTargetID.Main:
                    // Use integer-scaled destination for pixel-perfect mode
                    destinationRect = _viewportManager.CurrentScalingMode == ViewportManager.ScalingMode.PixelPerfect
                        ? _viewportManager.PixelPerfectDestinationRectangle
                        : _viewportManager.DestinationRectangle;
                    // LinearClamp for smooth filtering matching Blender's Linear interpolation
                    samplerState = SamplerState.LinearClamp;
                    break;

                case RenderTargetID.UI:
                    destinationRect = _viewportManager.DestinationRectangle;
                    // LinearClamp for smooth text in Smooth mode, PointClamp otherwise
                    samplerState = _viewportManager.CurrentScalingMode == ViewportManager.ScalingMode.Smooth
                        ? SamplerState.LinearClamp
                        : SamplerState.PointClamp;
                    break;

                case RenderTargetID.HUD:
                    destinationRect = new Rectangle(0, 0, _viewportManager.ScreenWidth, _viewportManager.ScreenHeight);
                    samplerState = SamplerState.PointClamp;
                    break;

                default:
                    destinationRect = _viewportManager.DestinationRectangle;
                    samplerState = SamplerState.PointClamp;
                    break;
            }

            _spriteBatch.Begin(
                SpriteSortMode.Immediate,
                BlendState.AlphaBlend,
                samplerState,
                DepthStencilState.None,
                RasterizerState.CullNone);

            _spriteBatch.Draw(renderTarget, destinationRect, Color.White);
            _spriteBatch.End();
        }
     }
     public void Dispose() { }
 }