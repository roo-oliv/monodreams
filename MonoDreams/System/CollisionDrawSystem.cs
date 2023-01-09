using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Component;
using MonoDreams.Message;
using MonoDreams.Renderer;
using MonoDreams.State;
using NotImplementedException = System.NotImplementedException;

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
            ref readonly var collidingEntity = ref collision.CollidingEntity;
            var rect = collidingEntity.Get<DrawInfo>().Destination;
            // var hint = _world.CreateEntity();
            // hint.Set(new Position(rect.Location.ToVector2()));
            // hint.Set(new DrawInfo
            // {
            //     Color = Color.DarkSlateGray,
            //     Destination = rect,
            // });
            // _hints.Add(hint);
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