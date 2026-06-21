using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Physics;
using MonoDreams.Examples.Component.Runner;
using MonoDreams.Examples.Input;
using MonoDreams.Examples.Runner;
using MonoDreams.State;
using MonoGame.Extended.BitmapFonts;

namespace MonoDreams.Examples.System.Runner;

[With(typeof(RunnerState), typeof(TransformComponent))]
public class GameOverSystem(World world, Game game, BitmapFont font) : AEntitySetSystem<GameState>(world)
{
    private Entity _gameOverTextEntity;
    private bool _gameOverTextCreated;

    protected override void Update(GameState state, in Entity entity)
    {
        var runnerState = entity.Get<RunnerState>();
        ref var transform = ref entity.Get<TransformComponent>();

        // Check fall death
        if (!runnerState.IsGameOver && transform.Position.Y > RunnerConstants.FallDeathY)
        {
            runnerState.IsGameOver = true;
            Logger.Info("Fell off treadmill! Game over.");
        }

        // Check left boundary
        if (!runnerState.IsGameOver && transform.Position.X < RunnerConstants.LeftBoundary)
        {
            runnerState.IsGameOver = true;
            Logger.Info("Fell off left edge! Game over.");
        }

        if (!runnerState.IsGameOver) return;

        // Create game over text once
        if (!_gameOverTextCreated)
        {
            CreateGameOverText();
            _gameOverTextCreated = true;
            // Stop the player
            if (entity.Has<VelocityComponent>())
            {
                var velocity = entity.Get<VelocityComponent>();
                velocity.Current = Vector2.Zero;
            }
        }

        if (InputState.Jump.JustPressed() || InputState.Right.JustPressed() || InputState.Interact.JustPressed())
        {
            RestartRunner(entity);
        }
    }

    private void CreateGameOverText()
    {
        // We'll create a game over text entity on the HUD
        _gameOverTextEntity = World.CreateEntity();
        _gameOverTextEntity.Set(new EntityInfoComponent("Interface"));
        _gameOverTextEntity.Set(new TransformComponent(new Vector2(80, 60)));
        _gameOverTextEntity.Set(new DynamicTextComponent
        {
            Target = RenderTargetID.HUD,
            LayerDepth = 1.0f,
            TextContent = "GAME OVER - Press Jump to Restart",
            Font = font,
            Color = RunnerConstants.GameOverColor,
            Scale = RunnerConstants.ScoreTextScale,
            IsRevealed = true,
            VisibleCharacterCount = int.MaxValue
        });
        _gameOverTextEntity.Set(new VisibleComponent());
    }

    private void RestartRunner(in Entity playerEntity)
    {
        // Reset player state
        var runnerState = playerEntity.Get<RunnerState>();
        runnerState.IsGameOver = false;
        runnerState.Score = 0;
        runnerState.IsGrounded = false;
        runnerState.GameTime = 0;

        // Reset player position
        ref var transform = ref playerEntity.Get<TransformComponent>();
        transform.Position = RunnerConstants.PlayerStartPosition;
        transform.LastPosition = RunnerConstants.PlayerStartPosition;

        // Reset velocity
        if (playerEntity.Has<VelocityComponent>())
        {
            var velocity = playerEntity.Get<VelocityComponent>();
            velocity.Current = Vector2.Zero;
            velocity.Last = Vector2.Zero;
        }

        // Remove game over text
        if (_gameOverTextEntity.IsAlive)
        {
            _gameOverTextEntity.Dispose();
        }
        _gameOverTextCreated = false;

        // Reset spawn point position
        var spawnPoints = World.GetEntities().With<SpawnPoint>().With<TransformComponent>().AsEnumerable();
        foreach (var sp in spawnPoints)
        {
            ref var spTransform = ref sp.Get<TransformComponent>();
            spTransform.SetPositionY(RunnerConstants.SpawnPointBaseY);
        }

        // Clean up all collectibles and obstacles
        var collectibles = World.GetEntities().With<EntityInfoComponent>().AsEnumerable();
        var toDispose = new global::System.Collections.Generic.List<Entity>();
        foreach (var e in collectibles)
        {
            var info = e.Get<EntityInfoComponent>();
            if (info.Type is "Collectible" or "Obstacle")
            {
                toDispose.Add(e);
            }
        }
        foreach (var e in toDispose) e.Dispose();

        Logger.Info("Runner restarted.");
    }
}
