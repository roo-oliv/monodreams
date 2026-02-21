using System;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Runner;
using MonoDreams.Examples.Runner;
using MonoDreams.Message;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Runner;

public class RunnerSpawnerSystem : ISystem<GameState>
{
    public bool IsEnabled { get; set; } = true;

    private readonly World _world;
    private readonly EntitySet _spawnPointSet;
    private readonly Random _random = new();
    private float _spawnTimer;
    private float _totalTime;

    public RunnerSpawnerSystem(World world)
    {
        _world = world;
        _spawnPointSet = world.GetEntities().With<SpawnPoint>().With<Transform>().AsSet();
    }

    public void Update(GameState state)
    {
        // Find runner state to check game over
        var runners = _world.GetEntities().With<RunnerState>().AsEnumerable();
        foreach (var runner in runners)
        {
            if (runner.Get<RunnerState>().IsGameOver) return;
        }

        _totalTime += state.Time;
        _spawnTimer += state.Time;

        // Oscillate spawn point Y
        foreach (ref readonly var spawnEntity in _spawnPointSet.GetEntities())
        {
            ref var transform = ref spawnEntity.Get<Transform>();
            var newY = RunnerConstants.SpawnPointBaseY +
                RunnerConstants.SpawnPointAmplitude * MathF.Sin(RunnerConstants.SpawnPointFrequency * _totalTime);
            transform.SetPositionY(newY);
        }

        // Unified spawn timer
        if (_spawnTimer >= RunnerConstants.SpawnInterval)
        {
            _spawnTimer = 0;

            // Get spawn position from spawn point entity
            Vector2 spawnPosition = new(RunnerConstants.SpawnPointX, RunnerConstants.SpawnPointBaseY);
            foreach (ref readonly var spawnEntity in _spawnPointSet.GetEntities())
            {
                spawnPosition = spawnEntity.Get<Transform>().Position;
                break;
            }

            // Randomly choose charm or obstacle
            var entityType = _random.NextDouble() < RunnerConstants.ObstacleSpawnChance ? "Obstacle" : "Charm";
            _world.Publish(new EntitySpawnRequest(
                entityType, "", spawnPosition,
                Vector2.Zero, Vector2.Zero, Vector2.Zero, default, null));
        }
    }

    public void Dispose()
    {
        _spawnPointSet.Dispose();
        GC.SuppressFinalize(this);
    }
}
