using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Message;
using MonoDreams.State;

namespace MonoDreams.System;

public sealed class CollisionDrawSystem : ISystem<GameState>
{
    private readonly Texture2D _square;
    private readonly World _world;
    private readonly List<CollisionMessage> _collisions;
    private readonly List<Entity> _hints;

    public CollisionDrawSystem(Texture2D square, World world)
    {
        world.Subscribe(this);
        _square = square;
        _world = world;
        _collisions = new List<CollisionMessage>();
        _hints = new List<Entity>();
    }
        
    [Subscribe]
    private void On(in CollisionMessage message) => _collisions.Add(message);

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
            var location = collidingEntity.Get<Position>().CurrentLocation;
            var hint = _world.CreateEntity();
            hint.Set(new Position(location));
            hint.Set(new DrawInfo(spriteSheet: _square, source: new Rectangle(0, 0, 1, 1), color: Color.DarkSlateGray));
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