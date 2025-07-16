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
     // Define the order and how to draw your RTs
     private readonly RenderTargetID[] _renderTargetsToDraw = [
         RenderTargetID.Main,
         RenderTargetID.UI,
         RenderTargetID.HUD,
     ];
     private readonly MonoDreams.Component.Camera _camera;

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
        _graphicsDevice.Clear(Color.Black);

        // No scale matrix needed here, we draw using destination rectangle for scaling.
        _spriteBatch.Begin(
            SpriteSortMode.Immediate, // Draw RTs in specific order
            BlendState.AlphaBlend,    // Use AlphaBlend for UI overlays
            SamplerState.PointClamp,  // PointClamp for pixel art, LinearClamp otherwise
            DepthStencilState.None,
            RasterizerState.CullNone);

        // Iterate through render targets in the desired draw order
        foreach (var targetId in _renderTargetsToDraw)
        {
            if (_renderTargets.TryGetValue(targetId, out var renderTarget) && renderTarget != null)
            {
                Rectangle destinationRect;
                
                // Apply letter/pillarboxing only to the main game view?
                if (targetId == RenderTargetID.Main)
                {
                    destinationRect = _viewportManager.DestinationRectangle;
                }
                else // Assume UI/HUD stretch or use full screen dimensions
                {
                    destinationRect = new Rectangle(0, 0, _viewportManager.ScreenWidth, _viewportManager.ScreenHeight);
                }
                
                _spriteBatch.Draw(renderTarget, destinationRect, Color.White);
            }
        }

        _spriteBatch.End();
     }
     public void Dispose() { }
 }