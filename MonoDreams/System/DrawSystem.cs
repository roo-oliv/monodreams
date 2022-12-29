using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.System;

public sealed class DrawSystem : AComponentSystem<GameState, DrawInfo>
{
    private readonly SpriteBatch _batch;
    private readonly Texture2D _square;
    private readonly ResolutionIndependentRenderer _resolutionIndependentRenderer;
    private readonly Camera _camera;

    public DrawSystem(
        ResolutionIndependentRenderer resolutionIndependentRenderer,
        Camera camera,
        SpriteBatch batch,
        Texture2D square,
        World world
        ) : base(world)
    {
        _resolutionIndependentRenderer = resolutionIndependentRenderer;
        _camera = camera;
        _batch = batch;
        _square = square;
    }

    protected override void PreUpdate(GameState state)
    {
        _resolutionIndependentRenderer.BeginDraw();
        _batch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.LinearWrap,
            DepthStencilState.None,
            RasterizerState.CullNone, 
            null,
            _camera.GetViewTransformationMatrix());
    }

    protected override void Update(GameState state, ref DrawInfo component)
    {
        _batch.Draw(_square, component.Destination, component.Color);
    }

    protected override void PostUpdate(GameState state) => _batch.End();
}