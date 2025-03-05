using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Message;
using MonoDreams.State;

namespace MonoDreams.System;

public sealed class CollisionDrawSystem<TCollisionMessage> : ISystem<GameState>
    where TCollisionMessage : ICollisionMessage
{
    private readonly Texture2D _square;
    private readonly World _world;
    private readonly List<TCollisionMessage> _collisions;
    private readonly List<Entity> _hints;
    private readonly RenderTarget2D _renderTarget;

    public CollisionDrawSystem(Texture2D square, World world, RenderTarget2D renderTarget)
    {
        world.Subscribe(this);
        _square = square;
        _world = world;
        _collisions = new List<TCollisionMessage>();
        _hints = new List<Entity>();
        _renderTarget = renderTarget;
    }
        
    [Subscribe]
    private void On(in TCollisionMessage message) => _collisions.Add(message);

    public void Update(GameState state)
    {
        foreach (var hint in _hints)
        {
            hint.Dispose();
        }
        _hints.Clear();
        if (_collisions.Count == 0)
        {
            return;
        }
        foreach (var collision in _collisions)
        {
            var collidingEntity = collision.CollidingEntity;
            var location = collidingEntity.Get<Position>().Current;
            var hint = _world.CreateEntity();
            hint.Set(new Position(location));
            hint.Set(new DrawInfo(spriteSheet: _square, source: new Rectangle(0, 0, 1, 1), color: Color.DarkSlateGray, renderTarget: _renderTarget));
            _hints.Add(hint);
        }

        _collisions.Clear();
    }

    public bool IsEnabled { get; set; } = true;

    public void Dispose()
    {
        _collisions.Clear();
        _hints.Clear();
    }
}