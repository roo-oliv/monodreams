using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Examples.System.Draw;

/// <summary>
/// Unified rendering system that handles all draw types: sprites, text, nine-patch, and mesh.
/// This system is game-agnostic - it only renders what's in DrawComponent without any
/// specialized component handling.
/// </summary>
[With(typeof(DrawComponent))]
public class MasterRenderSystem(
    SpriteBatch spriteBatch,
    GraphicsDevice graphicsDevice,
    MonoDreams.Component.Camera camera,
    IReadOnlyDictionary<RenderTargetID, RenderTarget2D> renderTargets,
    World world) : ISystem<GameState>
{
    private BasicEffect? _basicEffect;

    private enum BatchType { None, Sprite, Mesh }

    private static BatchType GetBatchType(DrawElementType type) =>
        type == DrawElementType.Mesh ? BatchType.Mesh : BatchType.Sprite;

    private void EnsureBasicEffect()
    {
        _basicEffect ??= new BasicEffect(graphicsDevice)
        {
            VertexColorEnabled = true,
            View = Matrix.Identity,
            Projection = Matrix.CreateOrthographicOffCenter(0, camera.VirtualWidth, camera.VirtualHeight, 0, 0, 1)
        };
    }

    private void BeginSpriteBatch(Matrix transformMatrix)
    {
        spriteBatch.Begin(
            sortMode: SpriteSortMode.Deferred, // We pre-sort, so no need for FrontToBack
            blendState: BlendState.AlphaBlend,
            samplerState: SamplerState.PointClamp,
            depthStencilState: DepthStencilState.None,
            rasterizerState: RasterizerState.CullNone,
            effect: null,
            transformMatrix: transformMatrix);
    }

    private void EndSpriteBatch() => spriteBatch.End();

    private void ResetGraphicsStateForMeshRendering()
    {
        // Reset states after SpriteBatch.End() before mesh rendering
        graphicsDevice.BlendState = BlendState.AlphaBlend;
        graphicsDevice.DepthStencilState = DepthStencilState.None;
        graphicsDevice.RasterizerState = RasterizerState.CullNone;
        graphicsDevice.SamplerStates[0] = SamplerState.PointClamp;
    }

    public void Update(GameState state)
    {
        EnsureBasicEffect();

        foreach (var renderTarget in renderTargets)
        {
            var drawList = world.GetEntities()
                .With((in DrawComponent e) => e.Target == renderTarget.Key)
                .AsSet();

            graphicsDevice.SetRenderTarget(renderTarget.Value);
            graphicsDevice.Clear(Color.Transparent);

            var transformMatrix = Matrix.Identity;
            if (renderTarget.Key == RenderTargetID.Main)
            {
                transformMatrix = camera.GetViewTransformationMatrix();
            }

            // Sort ALL entities by LayerDepth (stable sort preserves order for same depth)
            var entities = drawList.GetEntities().ToArray();
            var sortedEntities = entities
                .Select((entity, index) => (entity, index, dc: entity.Get<DrawComponent>()))
                .Where(x => x.dc.Type != DrawElementType.Mesh || x.dc.HasValidMesh)
                .OrderBy(x => x.dc.LayerDepth)
                .ThenBy(x => x.index) // Stable sort
                .ToList();

            if (sortedEntities.Count > 0)
            {
                RenderInterleaved(sortedEntities, transformMatrix);
            }
        }
    }

    private void RenderInterleaved(
        List<(Entity entity, int index, DrawComponent dc)> sortedEntities,
        Matrix transformMatrix)
    {
        BatchType currentBatch = BatchType.None;

        foreach (var (_, _, dc) in sortedEntities)
        {
            var requiredBatch = GetBatchType(dc.Type);

            // Batch type changed - switch context
            if (requiredBatch != currentBatch)
            {
                // End previous batch
                if (currentBatch == BatchType.Sprite)
                    EndSpriteBatch();

                // Start new batch
                if (requiredBatch == BatchType.Sprite)
                    BeginSpriteBatch(transformMatrix);
                else if (requiredBatch == BatchType.Mesh)
                    ResetGraphicsStateForMeshRendering();

                currentBatch = requiredBatch;
            }

            // Draw the element
            if (requiredBatch == BatchType.Sprite)
                DrawElement(dc);
            else
                DrawSingleMesh(dc, transformMatrix);
        }

        // End final batch if sprites
        if (currentBatch == BatchType.Sprite)
            EndSpriteBatch();
    }

    private void DrawSingleMesh(DrawComponent dc, Matrix transformMatrix)
    {
        if (!dc.HasValidMesh) return;

        _basicEffect!.Projection = Matrix.CreateOrthographicOffCenter(
            0, camera.VirtualWidth, camera.VirtualHeight, 0, 0, 1);
        _basicEffect.World = (dc.WorldMatrix ?? Matrix.Identity) * transformMatrix;

        foreach (var pass in _basicEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            graphicsDevice.DrawUserIndexedPrimitives(
                dc.PrimitiveType,
                dc.Vertices,
                0,
                dc.Vertices!.Length,
                dc.Indices,
                0,
                dc.GetPrimitiveCount());
        }
    }

    private void DrawElement(DrawComponent element)
    {
        switch (element.Type)
        {
            case DrawElementType.Sprite:
                if (element.Texture == null) return;

                // Calculate scale from source to destination size for sub-pixel precision
                Vector2 scale;
                if (element.SourceRectangle.HasValue && element.SourceRectangle.Value.Width > 0 && element.SourceRectangle.Value.Height > 0)
                {
                    scale = new Vector2(
                        element.Size.X / element.SourceRectangle.Value.Width,
                        element.Size.Y / element.SourceRectangle.Value.Height);
                }
                else
                {
                    scale = element.Scale;
                }

                spriteBatch.Draw(
                    element.Texture,
                    element.Position,  // Vector2 preserves sub-pixel precision
                    element.SourceRectangle,
                    element.Color,
                    element.Rotation,
                    element.Origin,
                    scale,
                    SpriteEffects.None,
                    element.LayerDepth);
                break;

            case DrawElementType.Text:
                if (element.Font == null || element.Text == null) return;

                spriteBatch.DrawString(
                    element.Font,
                    element.Text,
                    element.Position,
                    element.Color,
                    element.Rotation,
                    element.Origin,
                    element.Scale,
                    SpriteEffects.None,
                    element.LayerDepth);
                break;

            case DrawElementType.NinePatch:
                // NinePatch handled by SpritePrepSystem creating 9 sprite elements
                break;

            case DrawElementType.Mesh:
                // Handled separately in DrawSingleMesh via RenderInterleaved
                break;
        }
    }

    public void Dispose()
    {
        _basicEffect?.Dispose();
    }

    public bool IsEnabled { get; set; } = true;
}