using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.Renderer;
using MonoDreams.State;
using MonoGame.Extended.BitmapFonts;
using MonoGame.SplineFlower.Spline.Types;

namespace MonoDreams.Examples.System.Draw;

[With(typeof(DrawComponent), typeof(Transform))]
public class MasterRenderSystem(
    SpriteBatch spriteBatch,
    GraphicsDevice graphicsDevice,
    MonoDreams.Component.Camera camera,
    IReadOnlyDictionary<RenderTargetID, RenderTarget2D> renderTargets,
    World world,
    ViewportManager viewportManager) : ISystem<GameState>
{
    private BasicEffect _basicEffect;
    
    public void Initialize()
    {
        _basicEffect = new BasicEffect(graphicsDevice)
        {
            VertexColorEnabled = true,
            View = Matrix.Identity,
            Projection = Matrix.CreateOrthographicOffCenter(0, camera.VirtualWidth, camera.VirtualHeight, 0, 0, 1)
        };
    }

    public void Update(GameState state)
    {
        if (_basicEffect == null)
            Initialize();
        else
        {
            // Always update the projection matrix to match the virtual dimensions
            _basicEffect.Projection = Matrix.CreateOrthographicOffCenter(0, camera.VirtualWidth, camera.VirtualHeight, 0, 0, 1);
        }
        
        foreach (var renderTarget in renderTargets)
        {
            var drawList = world.GetEntities()
                .With((in DrawComponent e) => e.Target == renderTarget.Key)
                .AsSet();

            // Get specialized draw components for control points and lines
            var controlPointsList = world.GetEntities()
                .With<ControlPointsDrawComponent>()
                .With((in ControlPointsDrawComponent e) => e.Target == renderTarget.Key)
                .AsSet();

            var controlLinesList = world.GetEntities()
                .With<ControlLinesDrawComponent>()
                .With((in ControlLinesDrawComponent e) => e.Target == renderTarget.Key)
                .AsSet();

            // Get specialized draw components for overtaking opportunities
            var overtakingOpportunitiesList = world.GetEntities()
                .With<OvertakingOpportunityDrawComponent>()
                .With((in OvertakingOpportunityDrawComponent e) => e.Target == renderTarget.Key)
                .AsSet();

            // Get specialized draw components for level boundaries
            var boundariesList = world.GetEntities()
                .With<BoundaryDrawComponent>()
                .With((in BoundaryDrawComponent e) => e.Target == renderTarget.Key)
                .AsSet();

            EntitySet? splineList = null;
            if (renderTarget.Key == RenderTargetID.Main)
            {
                splineList = world.GetEntities()
                    .With<CatMulRomSpline>()
                    .AsSet();
            }

            graphicsDevice.SetRenderTarget(renderTarget.Value);
            graphicsDevice.Clear(Color.Transparent);

            var transformMatrix = Matrix.Identity;
            if (renderTarget.Key == RenderTargetID.Main)
            {
                transformMatrix = camera.GetViewTransformationMatrix();
                _basicEffect.World = transformMatrix;
            }
            // Remove the UI scaling transform since render target is now in virtual coordinates
            // else if (renderTarget.Key == RenderTargetID.UI)
            // {
            //     transformMatrix = viewportManager.GetScaleMatrix();
            // }

            // Draw triangles first (they need different rendering setup)
            var triangleElements = drawList.GetEntities().ToArray()
                .Where(e => e.Get<DrawComponent>().Type == DrawElementType.Triangles)
                .ToList();

            if (triangleElements.Count != 0)
            {
                DrawTriangles(triangleElements);
            }

            // Draw control points triangles
            if (!controlPointsList.GetEntities().IsEmpty)
            {
                DrawControlPoints(controlPointsList.GetEntities().ToArray());
            }

            // Draw control lines triangles
            if (!controlLinesList.GetEntities().IsEmpty)
            {
                DrawControlLines(controlLinesList.GetEntities().ToArray());
            }

            // Draw overtaking opportunities
            if (!overtakingOpportunitiesList.GetEntities().IsEmpty)
            {
                DrawOvertakingOpportunities(overtakingOpportunitiesList.GetEntities().ToArray());
            }

            // Draw level boundaries
            if (!boundariesList.GetEntities().IsEmpty)
            {
                DrawBoundaries(boundariesList.GetEntities().ToArray());
            }
            
            // Draw sprites and other elements
            spriteBatch.Begin(
                sortMode: SpriteSortMode.FrontToBack,
                blendState: BlendState.AlphaBlend,
                samplerState: SamplerState.LinearClamp,
                depthStencilState: DepthStencilState.None,
                rasterizerState: RasterizerState.CullNone,
                effect: null,
                transformMatrix: transformMatrix);
            
            foreach (var entity in drawList.GetEntities())
            {
                var drawComponent = entity.Get<DrawComponent>();
                if (drawComponent.Type != DrawElementType.Triangles)
                {
                    DrawElement(drawComponent);
                }
            }
            
            // Temporarily keep spline drawing for comparison
            if (splineList != null)
            {
                foreach (var entity in splineList.GetEntities())
                {
                    var spline = entity.Get<CatMulRomSpline>();
                    // Comment out to remove original spline drawing
                    // spline.Draw(spriteBatch);
                }
            }
            
            spriteBatch.End();
        }
    }
    
    private void DrawTriangles(List<Entity> triangleEntities)
    {
        // Sort triangles by layer depth for proper rendering order
        var sortedTriangles = triangleEntities
            .Select(entity => entity.Get<DrawComponent>())
            .OrderBy(dc => dc.LayerDepth)
            .ToList();

        // Update the projection matrix for the current viewport state
        _basicEffect.Projection = Matrix.CreateOrthographicOffCenter(0, camera.VirtualWidth, camera.VirtualHeight, 0, 0, 1);

        foreach (var pass in _basicEffect.CurrentTechnique.Passes)
        {
            pass.Apply();

            foreach (var drawComponent in sortedTriangles)
            {
                if (!(drawComponent.Vertices?.Length > 0) || !(drawComponent.Indices?.Length > 0)) continue;
            
            // Use the original vertices without modifying Z coordinates for now
            graphicsDevice.DrawUserIndexedPrimitives(
                drawComponent.PrimitiveType,
                drawComponent.Vertices,
                0,
                drawComponent.Vertices.Length,
                drawComponent.Indices,
                0,
                drawComponent.Indices.Length / 3);
            }
        }
    }

    private void DrawControlPoints(Entity[] controlPointEntities)
    {
        // Update the projection matrix for the current viewport state
        _basicEffect.Projection = Matrix.CreateOrthographicOffCenter(0, camera.VirtualWidth, camera.VirtualHeight, 0, 0, 1);

        foreach (var pass in _basicEffect.CurrentTechnique.Passes)
        {
            pass.Apply();

            foreach (var entity in controlPointEntities)
            {
                var drawComponent = entity.Get<ControlPointsDrawComponent>();
                if (!(drawComponent.Vertices?.Length > 0) || !(drawComponent.Indices?.Length > 0)) continue;
                graphicsDevice.DrawUserIndexedPrimitives(
                    drawComponent.PrimitiveType,
                    drawComponent.Vertices,
                    0,
                    drawComponent.Vertices.Length,
                    drawComponent.Indices,
                    0,
                    drawComponent.Indices.Length / 3);
            }
        }
    }

    private void DrawControlLines(Entity[] controlLinesEntities)
    {
        // Update the projection matrix for the current viewport state
        _basicEffect.Projection = Matrix.CreateOrthographicOffCenter(0, camera.VirtualWidth, camera.VirtualHeight, 0, 0, 1);

        foreach (var pass in _basicEffect.CurrentTechnique.Passes)
        {
            pass.Apply();

            foreach (var entity in controlLinesEntities)
            {
                var drawComponent = entity.Get<ControlLinesDrawComponent>();
                if (!(drawComponent.Vertices?.Length > 0) || !(drawComponent.Indices?.Length > 0)) continue;
                graphicsDevice.DrawUserIndexedPrimitives(
                    drawComponent.PrimitiveType,
                    drawComponent.Vertices,
                    0,
                    drawComponent.Vertices.Length,
                    drawComponent.Indices,
                    0,
                    drawComponent.Indices.Length / 3);
            }
        }
    }

    private void DrawOvertakingOpportunities(Entity[] overtakingOpportunitiesEntities)
    {
        // Update the projection matrix for the current viewport state
        _basicEffect.Projection = Matrix.CreateOrthographicOffCenter(0, camera.VirtualWidth, camera.VirtualHeight, 0, 0, 1);

        foreach (var pass in _basicEffect.CurrentTechnique.Passes)
        {
            pass.Apply();

            foreach (var entity in overtakingOpportunitiesEntities)
            {
                var drawComponent = entity.Get<OvertakingOpportunityDrawComponent>();
                if (!(drawComponent.Vertices?.Length > 0) || !(drawComponent.Indices?.Length > 0)) continue;
                graphicsDevice.DrawUserIndexedPrimitives(
                    drawComponent.PrimitiveType,
                    drawComponent.Vertices,
                    0,
                    drawComponent.Vertices.Length,
                    drawComponent.Indices,
                    0,
                    drawComponent.Indices.Length / 3);
            }
        }
    }

    private void DrawBoundaries(Entity[] boundaryEntities)
    {
        // Update the projection matrix for the current viewport state
        _basicEffect.Projection = Matrix.CreateOrthographicOffCenter(0, camera.VirtualWidth, camera.VirtualHeight, 0, 0, 1);

        foreach (var pass in _basicEffect.CurrentTechnique.Passes)
        {
            pass.Apply();

            foreach (var entity in boundaryEntities)
            {
                var drawComponent = entity.Get<BoundaryDrawComponent>();
                if (!(drawComponent.Vertices?.Length > 0) || !(drawComponent.Indices?.Length > 0)) continue;
                graphicsDevice.DrawUserIndexedPrimitives(
                    drawComponent.PrimitiveType,
                    drawComponent.Vertices,
                    0,
                    drawComponent.Vertices.Length,
                    drawComponent.Indices,
                    0,
                    drawComponent.Indices.Length / 3);
            }
        }
    }
    
    private void DrawElement(DrawComponent element)
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
                    element.Rotation,
                    element.Origin,
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
                    element.Scale, // Use the scale from DrawComponent
                    SpriteEffects.None,
                    element.LayerDepth);
                break;

            case DrawElementType.Triangles:
                // Handled separately in DrawTriangles method
                break;

            case DrawElementType.NinePatch:
            default:
                break;
        }
    }

    public void Dispose() 
    { 
        _basicEffect?.Dispose();
    }

    public bool IsEnabled { get; set; }
}