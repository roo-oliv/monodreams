using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Examples.Component.Runner;
using MonoDreams.Message;
using MonoDreams.State;
using Logger = MonoDreams.State.Logger;

namespace MonoDreams.Examples.System.Runner;

public class RunnerCollisionHandlerSystem : ISystem<GameState>
{
    public bool IsEnabled { get; set; } = true;

    private readonly World _world;

    public RunnerCollisionHandlerSystem(World world)
    {
        _world = world;
        _world.Subscribe<CollisionMessage>(OnCollision);
    }

    [Subscribe]
    private void OnCollision(in CollisionMessage message)
    {
        switch (message.Type)
        {
            case CollisionType.Collectible:
                HandleCollectible(message);
                break;
            case CollisionType.Damage:
                HandleObstacleHit(message);
                break;
        }
    }

    private void HandleCollectible(in CollisionMessage message)
    {
        // Remove the collectible first to prevent duplicate collisions
        if (message.CollidingEntity.IsAlive)
        {
            message.CollidingEntity.Dispose();
        }

        // Increment score
        var playerEntity = message.BaseEntity;
        if (playerEntity.IsAlive && playerEntity.Has<RunnerState>())
        {
            var runnerState = playerEntity.Get<RunnerState>();
            runnerState.Score++;
            Logger.Info($"Charm collected! Score: {runnerState.Score}");
        }
    }

    private void HandleObstacleHit(in CollisionMessage message)
    {
        // Remove the obstacle to prevent repeated collision messages
        if (message.CollidingEntity.IsAlive)
        {
            message.CollidingEntity.Dispose();
        }

        var playerEntity = message.BaseEntity;
        if (playerEntity.IsAlive && playerEntity.Has<RunnerState>())
        {
            var runnerState = playerEntity.Get<RunnerState>();
            if (!runnerState.IsGameOver)
            {
                runnerState.IsGameOver = true;
                Logger.Info("Hit obstacle! Game over.");
            }
        }
    }

    public void Update(GameState state) { }
    public void Dispose() { GC.SuppressFinalize(this); }
}
