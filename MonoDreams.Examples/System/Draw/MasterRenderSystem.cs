using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;
using MonoGame.SplineFlower.Spline;
using MonoGame.SplineFlower.Spline.Types;

namespace MonoDreams.Examples.System.Draw;

[With(typeof(DrawComponent), typeof(Transform))]
public class MasterRenderSystem(
    SpriteBatch spriteBatch,
    GraphicsDevice graphicsDevice,
    MonoDreams.Component.Camera camera,
    IReadOnlyDictionary<RenderTargetID, RenderTarget2D> renderTargets,
    World world) : ISystem<GameState>
{
    public void Update(GameState state)
    {
        foreach (var renderTarget in renderTargets)
        {
            var drawList = world.GetEntities()
                .With((in DrawComponent e) => e.Target == renderTarget.Key)
                .AsSet();
            EntitySet? splineList = null;
            if (renderTarget.Key == RenderTargetID.Main)
            {
                splineList = world.GetEntities()
                    .With<HermiteSpline>()
                    .AsSet();
            }
            
            graphicsDevice.SetRenderTarget(renderTarget.Value);

            // Only clear if you haven't cleared it elsewhere (e.g., at the very start of the frame)
            // Often UI/HUD targets are cleared, Main might be cleared or just drawn over. Depends on your setup.
            graphicsDevice.Clear(Color.Transparent); // Or appropriate background color

            var transformMatrix = Matrix.Identity;
            // Apply camera transform ONLY to the main game render target
            // TODO: Check if this is the source of bugs rendering to other render targets other than Main
            if (renderTarget.Key == RenderTargetID.Main)
            {
                transformMatrix = camera.GetViewTransformationMatrix();
            }
                
            spriteBatch.Begin(
                sortMode: SpriteSortMode.FrontToBack,
                blendState: BlendState.AlphaBlend, // Common defaults
                samplerState: SamplerState.PointClamp, // Or PointWrap, LinearClamp, etc.
                depthStencilState: DepthStencilState.None,
                rasterizerState: RasterizerState.CullNone,
                effect: null,
                transformMatrix: transformMatrix);
            foreach (var entity in drawList.GetEntities()) DrawElement(entity.Get<DrawComponent>());
            if (splineList != null)
                foreach (var entity in splineList.GetEntities())
                {
                    var spline = entity.Get<HermiteSpline>();
                    spline.Draw(spriteBatch);
                    // spline.CreatePolygonStripe();
                    // spline.DrawPolygonStripe();
                }
            spriteBatch.End();
        }
    }
    
    private void DrawElement(DrawComponent element)
    {
        switch (element.Type)
        {
            case DrawElementType.Sprite:
                // Simple sprite draw - assumes element.Position is top-left
                // and element.Size determines destination rect directly.
                // Origin/Rotation/Scale would need to be included if used.
                if (element.Texture == null)
                {
                    // Log or handle missing texture case
                    return;
                }
                spriteBatch.Draw(
                    element.Texture,
                    new Rectangle((int)element.Position.X, (int)element.Position.Y, (int)element.Size.X, (int)element.Size.Y),
                    element.SourceRectangle,
                    element.Color,
                    element.Rotation,
                    element.Origin,
                    SpriteEffects.None, // element.Effects
                    element.LayerDepth);

                // Alternative using Position + Scale (if Size isn't pre-calculated):
                // _spriteBatch.Draw(
                //     element.Texture,
                //     element.Position,
                //     element.SourceRectangle,
                //     element.Color,
                //     element.Rotation,
                //     element.Origin,
                //     element.Scale, // Use Scale field from DrawElement
                //     element.Effects,
                //     element.LayerDepth);
                break;

            case DrawElementType.Text:
                // Origin/Rotation/Scale/Effects can be added if stored in DrawComponent
                spriteBatch.DrawString(
                    element.Font,
                    element.Text,
                    element.Position,
                    element.Color,
                    0f, // Rotation
                    Vector2.Zero, // Origin (for alignment)
                    0.5f, // Scale
                    SpriteEffects.None, // Effects
                    element.LayerDepth);
                break;

            case DrawElementType.NinePatch:
            default:
                // If SpritePrepSystem generated 9 individual Sprite DrawElements, this case isn't needed.
                // If DrawElement holds NinePatchData, implement the 9 draw calls here.
                // Recommendation: Let SpritePrepSystem create the 9 sprite elements for simplicity here.
                break;
        }
    }

    public void Dispose() { /* Nothing specific to dispose here unless holding unmanaged resources */ }

    public bool IsEnabled { get; set; }
}