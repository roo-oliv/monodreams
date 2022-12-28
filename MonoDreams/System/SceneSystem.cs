using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Level;
using MonoDreams.State;

namespace MonoDreams.System
{
    public sealed class SceneSystem : ISystem<GameState>
    {
        private readonly Random _random;
        private readonly World _world;

        private int _ballCount;
        private int _brickCount;

        public SceneSystem(World world)
        {
            _random = new Random();
            _world = world;
            Level2.Load(_world);
        }

        public bool IsEnabled { get; set; } = true;

        public void Update(GameState state)
        {
        }

        public void Dispose() { }
    }
}