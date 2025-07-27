using Flecs.NET.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Examples.Component.Draw;
using static MonoDreams.Examples.System.SystemPhase;

namespace MonoDreams.Examples.System.Draw;

public static class MasterRenderSystem
{
    public static void Register(
        World world,
        SpriteBatch spriteBatch,
        GraphicsDevice graphicsDevice,
        MonoDreams.Component.Camera camera,
        IReadOnlyDictionary<RenderTargetID, RenderTarget2D> renderTargets)
    {
        // Create a system that runs once per frame to handle rendering
        world.System()
            .Kind(RenderPhase)
            .Kind(DrawPhase)
            .Run(() =>
            {
                foreach (var renderTarget in renderTargets)
                {
                    var drawList = new List<DrawComponent>();
                    
                    // Query for entities to draw on this render target
                    using var query = world.QueryBuilder<DrawComponent>().Build();
                    query.Each((ref DrawComponent draw) =>
                    {
                        if (draw.Target == renderTarget.Key)
                        {
                            drawList.Add(draw);
                        }
                    });
                    
                    if (drawList.Count == 0) continue;

                    graphicsDevice.SetRenderTarget(renderTarget.Value);
                    graphicsDevice.Clear(Color.Transparent);

                    var transformMatrix = Matrix.Identity;
                    if (renderTarget.Key == RenderTargetID.Main)
                    {
                        transformMatrix = camera.GetViewTransformationMatrix();
                    }
                        
                    spriteBatch.Begin(
                        sortMode: SpriteSortMode.FrontToBack,
                        blendState: BlendState.AlphaBlend,
                        samplerState: SamplerState.PointClamp,
                        depthStencilState: DepthStencilState.None,
                        rasterizerState: RasterizerState.CullNone,
                        effect: null,
                        transformMatrix: transformMatrix);

                    foreach (var drawComponent in drawList)
                    {
                        DrawElement(spriteBatch, drawComponent);
                    }
                    
                    spriteBatch.End();
                }
            });
    }
    
    private static void DrawElement(SpriteBatch spriteBatch, DrawComponent element)
    {
        switch (element.Type)
        {
            case DrawElementType.Sprite:
                if (element.Texture == null) return;
                
                spriteBatch.Draw(
                    element.Texture,
                    new Rectangle((int)element.Position.X, (int)element.Position.Y, (int)element.Size.X, (int)element.Size.Y),
                    element.SourceRectangle,
                    element.Color,
                    0f,
                    Vector2.Zero,
                    SpriteEffects.None,
                    element.LayerDepth);
                break;

            case DrawElementType.Text:
                spriteBatch.DrawString(
                    element.Font,
                    element.Text,
                    element.Position,
                    element.Color,
                    0f,
                    Vector2.Zero,
                    0.5f,
                    SpriteEffects.None,
                    element.LayerDepth);
                break;
        }
    }
}