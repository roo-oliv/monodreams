using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Draw;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.System;

public sealed class DrawSystem : AEntitySetSystem<GameState>
{
    private readonly SpriteBatch _batch;
    private readonly ResolutionIndependentRenderer _resolutionIndependentRenderer;
    private readonly Camera _camera;

    public DrawSystem(
        ResolutionIndependentRenderer resolutionIndependentRenderer,
        Camera camera,
        SpriteBatch batch,
        World world
        ) : base(world.GetEntities().With<DrawInfo>().AsSet())
    {
        _resolutionIndependentRenderer = resolutionIndependentRenderer;
        _camera = camera;
        _batch = batch;
    }

    protected override void PreUpdate(GameState state)
    {
        _resolutionIndependentRenderer.BeginDraw();
        _batch.Begin(
            SpriteSortMode.BackToFront,
            BlendState.AlphaBlend,
            SamplerState.PointWrap,
            DepthStencilState.None,
            RasterizerState.CullNone, 
            null,
            _camera.GetViewTransformationMatrix());
    }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var drawInfo = ref entity.Get<DrawInfo>();
        ref var position = ref entity.Get<Position>();

        if (entity.Has<Animation>())
        {
            ref var animation = ref entity.Get<Animation>();
        
            var totalDuration = animation.FrameDuration * animation.TotalFrames;
        
            animation.CurrentFrame = (int)((state.TotalTime % totalDuration) / animation.FrameDuration);
        
            var frameWidth = drawInfo.SpriteSheet.Width / animation.TotalFrames;
            drawInfo.Source = new Rectangle(frameWidth * animation.CurrentFrame, 0, frameWidth, drawInfo.SpriteSheet.Height);
        }
        
        float layerDepth = DrawLayerDepth.GetLayerDepth(drawInfo.Layer);
        var destination = new Rectangle(position.CurrentLocation.ToPoint(), drawInfo.Size);
        _batch.Draw(drawInfo.SpriteSheet, destination, drawInfo.Source, drawInfo.Color, 0, Vector2.Zero, SpriteEffects.None, layerDepth);
    }

    protected override void PostUpdate(GameState state) => _batch.End();
}