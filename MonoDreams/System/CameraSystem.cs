using System;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System;

public sealed class CameraSystem : AEntitySetSystem<GameState>
{
    private readonly Camera _camera;
    private Entity _targetHint;
    private Entity _cameraHint;
    private Vector2 _lastTarget;
    private Vector2 _origin;
    private Vector2 _followTime;
    private readonly World _world;
    private const float LerpDuration = 0.4f;
    private const int MaxHorizontalDistance = 15;
    private const int MaxAboveDistance = 25;
    private const int MaxBelowDistance = 10;

    public CameraSystem(Camera camera, World world, IParallelRunner runner)
        : base(world.GetEntities().With<PlayerInput>().AsSet(), runner)
    {
        _world = world;
        _camera = camera;
        _lastTarget = camera.Position;
        _origin = camera.Position;
        _followTime = Vector2.Zero;
    }

    protected override void Update(GameState state, in Entity entity)
    {
        ref var position = ref entity.Get<Position>();
        var target = position.CurrentLocation + new Vector2(9 + (float)position.CurrentOrientation * 15, 0);
        // _targetHint.Dispose();
        // _targetHint = _world.CreateEntity();
        // _targetHint.Set(new Position(target));
        // _targetHint.Set(new DrawInfo
        // {
        //     Color = Color.Gold,
        //     Destination = new Rectangle((int)target.X - 1, (int)target.Y - 1, 2, 2),
        // });
        
        if (target != _lastTarget)
        {
            if (target.X != _lastTarget.X)
            {
                _followTime.X = 0;
                _origin.X = _camera.Position.X;
            }
            if (target.Y != _lastTarget.Y)
            {
                _followTime.Y = 0;
                _origin.Y = _camera.Position.Y;
            }
            _lastTarget = target;
        }
        _followTime += Vector2.One * state.Time;
        var duration = _followTime / LerpDuration;
        duration *= duration * (3f * Vector2.One - 2f * duration);
        // _camera.Position = _followTime < LerpDuration ? Vector2.Lerp(_origin, target, duration) : target;
        var cameraPositionX = _followTime.X < LerpDuration ? MathHelper.Lerp(_origin.X, target.X, duration.X) : target.X;
        var cameraPositionY = _followTime.Y < LerpDuration ? MathHelper.Lerp(_origin.Y, target.Y, duration.Y) : target.Y;
        _camera.Position = new Vector2(cameraPositionX, cameraPositionY);
        if (Math.Abs(target.X - _camera.Position.X) > MaxHorizontalDistance)
        {
            _camera.Position = new Vector2(target.X - (float)position.CurrentOrientation * MaxHorizontalDistance, _camera.Position.Y);
        }
        if (_camera.Position.Y - target.Y > MaxAboveDistance)
        {
            _camera.Position = new Vector2(_camera.Position.X, target.Y + MaxAboveDistance);
        }
        else if (target.Y - _camera.Position.Y > MaxBelowDistance)
        {
            _camera.Position = new Vector2(_camera.Position.X, target.Y - MaxBelowDistance);
        }
        
        // _cameraHint.Dispose();
        // _cameraHint = _world.CreateEntity();
        // _cameraHint.Set(new Position(_camera.Position));
        // _cameraHint.Set(new DrawInfo
        // {
        //     Color = Color.Red,
        //     Destination = new Rectangle((int)_camera.Position.X - 1, (int)_camera.Position.Y - 1, 2, 2),
        // });
    }
}