using System.Collections.Generic;
using System.Linq;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.State;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.System.Draw;

/// <summary>
/// Unified rendering system that handles all draw types: sprites, text, nine-patch, and mesh.
/// This system is game-agnostic — it only renders what's in DrawComponent without any
/// specialized component handling.
/// <para>
/// One instance renders a single pass: every entity whose <see cref="DrawComponent.Target"/>
/// equals <paramref name="source"/>, through the optional <paramref name="camera"/> view
/// transform (null ⇒ screen-space, no transform), into <paramref name="destination"/>.
/// Compose multiple instances for multiple views — e.g. the world (Main) through the main
/// camera, plus UI and HUD screen-space passes, plus a minimap (Main through a second camera
/// into its own target), splitscreen, CCTV, or portal textures. The instances are independent
/// render passes; <see cref="FinalDrawSystem"/> arranges their targets onto the screen.
/// </para>
/// </summary>
public class MasterRenderSystem(
    SpriteBatch spriteBatch,
    GraphicsDevice graphicsDevice,
    World world,
    RenderTargetID source,
    RenderTarget2D destination,
    MonoDreams.Component.Camera? camera = null,
    SamplerState? spriteSampler = null,
    SamplerState? textSampler = null) : ISystem<GameState>
{
    private BasicEffect? _basicEffect;

    // A 1×1 white pixel used to stroke text underlines in the text branch (tinted with the text
    // color). Created lazily on the graphics device, disposed with the system.
    private Texture2D? _pixel;

    // The draw set for this pass, built once and reused for the system's lifetime.
    // AsSet() registers subscriptions the World keeps alive forever — building a fresh
    // set every frame leaks an EntitySet + its subscriptions per frame, so memory climbs
    // even on a static scene. See premise "Per-target draw sets are built once, not per frame".
    private EntitySet? _drawSet;

    private EntitySet DrawSet => _drawSet ??= BuildDrawSet();

    private EntitySet BuildDrawSet()
    {
        var queryBuilder = world.GetEntities()
            .With((in DrawComponent e) => e.Target == source);

        // The world (Main) respects CullingSystem visibility; screen-space passes always render.
        if (source == RenderTargetID.Main)
            queryBuilder = queryBuilder.With<VisibleComponent>();

        return queryBuilder.AsSet();
    }

    // Pixel art wants nearest-neighbour for sprites (and meshes); downscaled bitmap fonts
    // read better with linear filtering — so each draw type gets its own sampler. Both are
    // overridable per game; the defaults suit this engine's pixel-art content.
    private SamplerState SpriteSamplerState => spriteSampler ?? SamplerState.PointClamp;
    private SamplerState TextSamplerState => textSampler ?? SamplerState.LinearClamp;

    private enum BatchType { None, Sprite, Text, Mesh }

    private static BatchType GetBatchType(DrawElementType type) => type switch
    {
        DrawElementType.Mesh => BatchType.Mesh,
        DrawElementType.Text => BatchType.Text,
        _ => BatchType.Sprite,
    };

    // The mesh ortho projection maps this destination's pixel space to NDC. Derived from the
    // destination size (not the camera) so screen-space passes need no camera; the camera's
    // virtual resolution must match the destination size (it centers the view there).
    private Matrix Projection() =>
        Matrix.CreateOrthographicOffCenter(0, destination.Width, destination.Height, 0, 0, 1);

    private Texture2D Pixel()
    {
        if (_pixel == null)
        {
            _pixel = new Texture2D(graphicsDevice, 1, 1);
            _pixel.SetData(new[] { Color.White });
        }
        return _pixel;
    }

    private void EnsureBasicEffect()
    {
        _basicEffect ??= new BasicEffect(graphicsDevice)
        {
            VertexColorEnabled = true,
            View = Matrix.Identity,
            Projection = Projection(),
        };
    }

    private void BeginSpriteBatch(Matrix transformMatrix, SamplerState samplerState)
    {
        spriteBatch.Begin(
            sortMode: SpriteSortMode.Deferred, // We pre-sort, so no need for FrontToBack
            blendState: BlendState.AlphaBlend,
            samplerState: samplerState,
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

        graphicsDevice.SetRenderTarget(destination);
        graphicsDevice.Clear(Color.Transparent);

        var transformMatrix = camera?.GetViewTransformationMatrix() ?? Matrix.Identity;

        // Sort ALL entities by LayerDepth (stable sort preserves order for same depth)
        var entities = DrawSet.GetEntities().ToArray();
        var sortedEntities = entities
            .Select((entity, index) => (entity, index, dc: entity.Get<DrawComponent>()))
            .Where(x => x.dc.Type != DrawElementType.Mesh || x.dc.HasValidMesh)
            .OrderBy(x => x.dc.LayerDepth)
            .ThenBy(x => x.index) // Stable sort
            .ToList();

        if (sortedEntities.Count > 0)
            RenderInterleaved(sortedEntities, transformMatrix);
    }

    private void RenderInterleaved(
        List<(Entity entity, int index, DrawComponent dc)> sortedEntities,
        Matrix transformMatrix)
    {
        BatchType currentBatch = BatchType.None;
        // The running quad count + flush decision for the *current* SpriteBatch Begin/End run, shared with
        // the SpriteBatchFlushTests so the renderer and its tests exercise the same flush logic. Reset on
        // every Begin (context switch or flush); used to keep each run below the Reach 16-bit-index budget.
        var batchRun = new SpriteBatchFlush.BatchRun();

        foreach (var (_, _, dc) in sortedEntities)
        {
            var requiredBatch = GetBatchType(dc.Type);

            // Batch type changed - switch context. Sprite and Text both use the SpriteBatch
            // but with different samplers, so switching between them flushes + reopens it.
            // Painter's order is preserved because the list is already depth-sorted.
            if (requiredBatch != currentBatch)
            {
                // End previous batch
                if (currentBatch is BatchType.Sprite or BatchType.Text)
                    EndSpriteBatch();

                // Start new batch
                if (requiredBatch == BatchType.Sprite)
                {
                    BeginSpriteBatch(transformMatrix, SpriteSamplerState);
                    batchRun.Reset();
                }
                else if (requiredBatch == BatchType.Text)
                {
                    BeginSpriteBatch(transformMatrix, TextSamplerState);
                    batchRun.Reset();
                }
                else if (requiredBatch == BatchType.Mesh)
                    ResetGraphicsStateForMeshRendering();

                currentBatch = requiredBatch;
            }

            // Draw the element. Sprite/Text both accumulate into the current Begin/End run via the same
            // BatchRun the tests drive: ConsumeBefore decides whether the run must flush (End + reopen
            // with the same sampler) before this element to stay below the Reach 16-bit-index cap, and
            // folds the element's quad count into the run. Called once per sprite/text element — including
            // the first after a context switch (its count is 0, so it never flushes).
            if (requiredBatch == BatchType.Mesh)
                DrawSingleMesh(dc, transformMatrix);
            else
            {
                if (batchRun.ConsumeBefore(dc))
                {
                    EndSpriteBatch();
                    BeginSpriteBatch(
                        transformMatrix,
                        currentBatch == BatchType.Text ? TextSamplerState : SpriteSamplerState);
                }
                DrawElement(dc); // Sprite or Text
            }
        }

        // End final batch if it was a SpriteBatch one
        if (currentBatch is BatchType.Sprite or BatchType.Text)
            EndSpriteBatch();
    }

    private void DrawSingleMesh(DrawComponent dc, Matrix transformMatrix)
    {
        if (!dc.HasValidMesh) return;

        _basicEffect!.Projection = Projection();
        _basicEffect.World = (dc.WorldMatrix ?? Matrix.Identity) * transformMatrix;

        // Prefer 16-bit indices so meshes paint on the Reach profile (WebGL/BlazorGL), which
        // rejects 32-bit indices. Procedural meshes are tiny, so this is the path taken; only a
        // mesh exceeding the 16-bit vertex ceiling falls back to 32-bit indices (HiDef only).
        // See DrawComponent.Get16BitIndices() and the "Mesh indices render 16-bit" premise.
        var indices16 = dc.Get16BitIndices();
        var primitiveCount = dc.GetPrimitiveCount();

        foreach (var pass in _basicEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            if (indices16 != null)
                graphicsDevice.DrawUserIndexedPrimitives(
                    dc.PrimitiveType, dc.Vertices, 0, dc.Vertices!.Length, indices16, 0, primitiveCount);
            else
                graphicsDevice.DrawUserIndexedPrimitives(
                    dc.PrimitiveType, dc.Vertices, 0, dc.Vertices!.Length, dc.Indices, 0, primitiveCount);
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

                // Lay out multi-line text ourselves: one DrawString per '\n'-separated line,
                // advancing by the (scaled) font line height × LineSpacing. This gives a
                // configurable, scale-correct leading instead of relying on the font backend's
                // internal newline advance (which ignores the draw scale and overlaps lines).
                var lineSpacing = element.LineSpacing > 0 ? element.LineSpacing : DynamicTextComponent.DefaultLineSpacing;
                var lineAdvance = element.Font.LineHeight * element.Scale.Y * lineSpacing;
                var lines = element.Text.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var linePos = element.Position + new Vector2(0f, i * lineAdvance);
                    spriteBatch.DrawString(
                        element.Font,
                        lines[i],
                        linePos,
                        element.Color,
                        element.Rotation,
                        element.Origin,
                        element.Scale,
                        SpriteEffects.None,
                        element.LayerDepth);

                    // Underline: a thin filled bar (1×1 pixel scaled) under the line, in the text's
                    // own color, spanning the rendered line width, at the line's bottom. Scales with
                    // the text. The text color is opaque, so this honors the opaque-fill rule.
                    if (element.Underline && lines[i].Length > 0)
                    {
                        var lineWidth = element.Font.MeasureString(lines[i]).Width * element.Scale.X;
                        if (lineWidth > 0f)
                        {
                            var thickness = MathHelper.Max(1f, element.Font.LineHeight * element.Scale.Y * 0.06f);
                            var underlineY = linePos.Y + element.Font.LineHeight * element.Scale.Y - thickness;
                            spriteBatch.Draw(
                                Pixel(),
                                new Vector2(linePos.X, underlineY),
                                null,
                                element.Color,
                                element.Rotation,
                                Vector2.Zero,
                                new Vector2(lineWidth, thickness),
                                SpriteEffects.None,
                                element.LayerDepth);
                        }
                    }
                }
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
        _pixel?.Dispose();
        _drawSet?.Dispose();
    }

    public bool IsEnabled { get; set; } = true;
}
