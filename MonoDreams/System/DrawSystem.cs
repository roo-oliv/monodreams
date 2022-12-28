using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System;

public sealed class DrawSystem : AComponentSystem<GameState, DrawInfo>
{
    private readonly SpriteBatch _batch;
    private readonly Texture2D _square;

    public DrawSystem(SpriteBatch batch, Texture2D square, World world)
        : base(world)
    {
        _batch = batch;
        _square = square;
    }

    protected override void PreUpdate(GameState state) => _batch.Begin();

    protected override void Update(GameState state, ref DrawInfo component) =>
        _batch.Draw(_square, component.Destination, component.Color);

    protected override void PostUpdate(GameState state) => _batch.End();
}