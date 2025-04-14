using System;
using DefaultEcs.System;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System.Draw;

public class BeginDrawSystem(
    SpriteBatch batch,
    Camera camera) : ISystem<GameState>
{
    public void Update(GameState state)
    {
        batch.Begin(
            SpriteSortMode.BackToFront,
            BlendState.AlphaBlend,
            SamplerState.PointWrap,
            DepthStencilState.None,
            RasterizerState.CullNone, 
            null,
            camera.GetViewTransformationMatrix());
    }

    public bool IsEnabled { get; set; } = true;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}