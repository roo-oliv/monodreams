using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Message;
using MonoDreams.State;
using NotImplementedException = System.NotImplementedException;

namespace MonoDreams.System;

public sealed class CollisionDrawSystem : ISystem<GameState>
{
    private readonly SpriteBatch _batch;
    private readonly Texture2D _square;
    private readonly SpriteFont _font;
    private readonly List<CollisionMessage> _collisions;

    public CollisionDrawSystem(SpriteBatch batch, Texture2D square, SpriteFont font, World world)
    {
        world.Subscribe(this);
        _batch = batch;
        _square = square;
        _font = font;
        _collisions = new List<CollisionMessage>();
    }
        
    [Subscribe]
    private void On(in CollisionMessage message) => _collisions.Add(message);

    public void Update(GameState state)
    {
        if (_collisions.Count == 0)
        {
            return;
        }
        _batch.Begin();
        foreach (var collision in _collisions)
        {
            ref readonly var collidingEntity = ref collision.CollidingEntity;
            var rect = collidingEntity.Get<DrawInfo>().Destination;
            rect.Inflate(2, 2);

            _batch.Draw(_square, rect, Color.Gold);
        }

        _batch.End();
        _collisions.Clear();
    }

    public bool IsEnabled { get; set; } = true;

    public void Dispose()
    {
        _collisions.Clear();
    }
}