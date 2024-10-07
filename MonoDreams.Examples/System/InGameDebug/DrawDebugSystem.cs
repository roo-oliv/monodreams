using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component.Debug;
using MonoDreams.Renderer;
using MonoDreams.State;

namespace MonoDreams.Examples.System.InGameDebug;

public class DrawDebugSystem(World world, SpriteBatch batch, ResolutionIndependentRenderer renderer) : AComponentSystem<GameState, DebugUI>(world)
{
    protected override void Update(GameState state, ref DebugUI debugUI)
    {
        batch.Begin(
            SpriteSortMode.Deferred,
            BlendState.AlphaBlend,
            SamplerState.PointClamp,
            DepthStencilState.None,
            RasterizerState.CullNone);
        batch.Draw(debugUI.DebugRenderTarget, Vector2.Zero, new Rectangle(0, 0, renderer.ScreenWidth, renderer.ScreenHeight), Color.White);
        batch.End();
    }
}