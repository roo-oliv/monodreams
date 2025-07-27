using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Renderer;
using static MonoDreams.Examples.System.SystemPhase;

namespace MonoDreams.Examples.System.Draw;

public static class FinalDrawSystem
{
    // Define the order and how to draw your RTs
    private static readonly RenderTargetID[] RenderTargetsToDraw = [
        RenderTargetID.Main,
        RenderTargetID.UI,
        RenderTargetID.HUD,
    ];

    public static void Register(
        World world,
        SpriteBatch spriteBatch,
        GraphicsDevice graphicsDevice,
        ViewportManager viewportManager,
        IReadOnlyDictionary<RenderTargetID, RenderTarget2D> renderTargets)
    {
        world.System()
            .Kind(RenderPhase)
            .Kind(DrawPhase)
            .Run(() =>
            {
                // Ensure we are drawing to the back buffer
                graphicsDevice.SetRenderTarget(null);
                // Clear the entire screen (including letter/pillarbox areas)
                graphicsDevice.Clear(Color.Black);

                // No scale matrix needed here, we draw using destination rectangle for scaling.
                spriteBatch.Begin(
                    SpriteSortMode.Immediate, // Draw RTs in specific order
                    BlendState.AlphaBlend,    // Use AlphaBlend for UI overlays
                    SamplerState.PointClamp,  // PointClamp for pixel art, LinearClamp otherwise
                    DepthStencilState.None,
                    RasterizerState.CullNone);

                // Iterate through render targets in the desired draw order
                foreach (var targetId in RenderTargetsToDraw)
                {
                    if (!renderTargets.TryGetValue(targetId, out var renderTarget) || renderTarget == null) continue;

                    var destinationRect = targetId == RenderTargetID.Main ?
                        viewportManager.DestinationRectangle
                        : new Rectangle(0, 0, viewportManager.ScreenWidth, viewportManager.ScreenHeight);

                    spriteBatch.Draw(renderTarget, destinationRect, Color.White);
                }

                spriteBatch.End();
            });
    }
}