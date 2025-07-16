using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Draw;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Draw;

public sealed class MasterRenderSystem : ISystem<GameState>
{
    private readonly SpriteBatch _spriteBatch;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly MonoDreams.Component.Camera _camera; // Assuming you have a Camera class
    private readonly IReadOnlyDictionary<RenderTargetID, RenderTarget2D> _renderTargets;
    private readonly World _world;
    private readonly List<DrawElement> _drawList = new(1024); // Pre-allocate

    public bool IsEnabled { get; set; } = true;

    public MasterRenderSystem(
        SpriteBatch spriteBatch,
        GraphicsDevice graphicsDevice,
        MonoDreams.Component.Camera camera,
        IReadOnlyDictionary<RenderTargetID, RenderTarget2D> renderTargets, // Pass in your RTs
        World world)
    {
        _spriteBatch = spriteBatch;
        _graphicsDevice = graphicsDevice;
        _camera = camera;
        _renderTargets = renderTargets;
        _world = world;
    }
    
    private static readonly Comparison<DrawElement> DrawElementComparer = (a, b) =>
    {
        // Compare RenderTargetID values directly without boxing
        var targetCompare = ((int)a.Target).CompareTo((int)b.Target);
        if (targetCompare != 0) return targetCompare;
        return a.LayerDepth.CompareTo(b.LayerDepth);
    };


    public void Update(GameState state)
    {
        // 1. Collect all DrawElements
        _drawList.Clear();
        var drawablesSet = _world.GetEntities().With<DrawComponent>().AsSet();
        foreach (ref readonly var entity in drawablesSet.GetEntities())
        {
            _drawList.AddRange(entity.Get<DrawComponent>().Drawables);
        }

        // 2. Sort the list
        // Sort primarily by RenderTarget, secondarily by LayerDepth.
        // Add tertiary sort by Texture for potential batching improvements if needed.
        _drawList.Sort(DrawElementComparer);

        // 3. Iterate and Draw, handling RenderTarget changes
        RenderTargetID? currentTargetId = null; // Use nullable to track initial state

        foreach (var element in _drawList)
        {
            // Check if RenderTarget needs to change
            if (element.Target != currentTargetId)
            {
                // End previous batch if one was active
                if (currentTargetId.HasValue)
                {
                    _spriteBatch.End();
                }

                // Set new RenderTarget
                currentTargetId = element.Target;
                _renderTargets.TryGetValue(currentTargetId.Value, out RenderTarget2D targetRt);
                _graphicsDevice.SetRenderTarget(targetRt); // Pass null for the back buffer if needed

                // Clear the new target *if* it's not the default back buffer
                // Only clear if you haven't cleared it elsewhere (e.g., at the very start of the frame)
                // Often UI/HUD targets are cleared, Main might be cleared or just drawn over. Depends on your setup.
                if (targetRt != null)
                {
                    _graphicsDevice.Clear(Color.Transparent); // Or appropriate background color
                }

                // Adjust sort mode based on LayerDepth comparison in Sort:
                // If using a.LayerDepth.CompareTo(b.LayerDepth) -> FrontToBack
                // If using b.LayerDepth.CompareTo(a.LayerDepth) -> BackToFront
                SpriteSortMode sortMode = SpriteSortMode.FrontToBack;
                Matrix transformMatrix = Matrix.Identity;

                // Apply camera transform ONLY to the main game render target
                if (currentTargetId == RenderTargetID.Main)
                {
                    transformMatrix = _camera.GetViewTransformationMatrix();
                }
                
                _spriteBatch.Begin(
                    sortMode: sortMode,
                    blendState: BlendState.AlphaBlend, // Common defaults
                    samplerState: SamplerState.PointClamp, // Or PointWrap, LinearClamp, etc.
                    depthStencilState: DepthStencilState.None,
                    rasterizerState: RasterizerState.CullNone,
                    effect: null,
                    transformMatrix: transformMatrix);
            }

            // 4. Perform the Draw Call based on Element Type
             switch (element.Type)
             {
                 case DrawElementType.Sprite:
                     if (element.Texture != null)
                     {
                         // Simple sprite draw - assumes element.Position is top-left
                         // and element.Size determines destination rect directly.
                         // Origin/Rotation/Scale would need to be included if used.
                         _spriteBatch.Draw(
                             element.Texture,
                             new Rectangle((int)element.Position.X, (int)element.Position.Y, (int)element.Size.X, (int)element.Size.Y),
                             element.SourceRectangle,
                             element.Color,
                             0f, // element.Rotation
                             Vector2.Zero, // element.Origin
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
                     }
                     break;

                 case DrawElementType.Text:
                     if (element.Font != null && element.Text != null)
                     {
                        // Origin/Rotation/Scale/Effects can be added if stored in DrawElement
                         _spriteBatch.DrawString(
                             element.Font,
                             element.Text,
                             element.Position,
                             element.Color,
                             0f, // Rotation
                             Vector2.Zero, // Origin (for alignment)
                             0.5f, // Scale
                             SpriteEffects.None, // Effects
                             element.LayerDepth);
                     }
                     break;

                case DrawElementType.NinePatch:
                     // If SpritePrepSystem generated 9 individual Sprite DrawElements, this case isn't needed.
                     // If DrawElement holds NinePatchData, implement the 9 draw calls here.
                     // Recommendation: Let SpritePrepSystem create the 9 sprite elements for simplicity here.
                     break;
             }
        }

        // 5. End the final batch if one was active
        if (currentTargetId.HasValue)
        {
            _spriteBatch.End();
        }

         // Note: Drawing the RenderTargets TO the screen/backbuffer is usually done
         // in a *separate* system AFTER this one, like the 'FinalDrawSystem' mentioned.
    }

    public void Dispose() { /* Nothing specific to dispose here unless holding unmanaged resources */ }
}