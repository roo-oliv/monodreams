using System;
using DefaultEcs;
using DefaultEcs.System;
using DefaultEcs.Threading;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.System;

// public sealed class FollowingCameraSystem : AEntitySetSystem<GameState>
// {
//     private readonly Camera _camera;
//     private Vector2 _lastTarget;
//     private float _followTime;
//     private const float LerpDuration = 1.8f;
//
//     public FollowingCameraSystem(Camera camera, World world, IParallelRunner runner)
//         : base(world.GetEntities().With<PlayerInput>().AsSet(), runner)
//     {
//         _camera = camera;
//         _lastTarget = camera.Position;
//         _followTime = 0;
//     }
//
//     protected override void Update(GameState state, in Entity entity)
//     {
//         ref var position = ref entity.Get<Position>();
//         // var target = position.Current + new Vector2((float)position.CurrentOrientation * 300, 0);
//         var target = new Vector2(position.Current.X + (float)position.CurrentOrientation * 300, position.Current.Y);
//         if (target != _lastTarget)
//         {
//             _lastTarget = target;
//             _followTime = 0;
//         }
//         _followTime += state.Time;
//         _camera.Position = _followTime < LerpDuration ? Vector2.Lerp(_camera.Position, target, _followTime / LerpDuration) : target;
//         if (Math.Abs(target.X - _camera.Position.X) > 500)
//         {
//             _camera.Position = new Vector2(target.X - (float)position.CurrentOrientation * 500, _camera.Position.Y);
//         }
//         if (Math.Abs(target.Y - _camera.Position.Y) > 300)
//         {
//             var sign = target.Y > _camera.Position.Y ? 1 : -1;
//             _camera.Position = new Vector2(_camera.Position.X, target.Y - sign * 300);
//         }
//
//         _camera.Position = new Vector2(1900, 1250);  // debug with fixed camera
//     }
// }