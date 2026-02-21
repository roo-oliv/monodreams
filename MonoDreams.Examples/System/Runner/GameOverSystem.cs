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

[With(typeof(RunnerState), typeof(Transform))]
public class GameOverSystem(World world, Game game, BitmapFont font) : AEntitySetSystem<GameState>(world)
{
    private Entity _gameOverTextEntity;
    private bool _gameOverTextCreated;
    private bool _restartKeyHeld;

    protected override void Update(GameState state, in Entity entity)
    {
        var runnerState = entity.Get<RunnerState>();
        ref var transform = ref entity.Get<Transform>();

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
            if (entity.Has<Velocity>())
            {
                var velocity = entity.Get<Velocity>();
                velocity.Current = Vector2.Zero;
            }
        }

        // Press any key to restart (manual edge detection — Pressed() with guard)
        bool anyRestart = InputState.Jump.Pressed(state) || InputState.Right.Pressed(state) ||
                          InputState.Interact.Pressed(state);
        if (anyRestart && !_restartKeyHeld)
        {
            RestartRunner(entity);
        }
        _restartKeyHeld = anyRestart;
    }

    private void CreateGameOverText()
    {
        // We'll create a game over text entity on the HUD
        _gameOverTextEntity = World.CreateEntity();
        _gameOverTextEntity.Set(new EntityInfo("Interface"));
        _gameOverTextEntity.Set(new Transform(new Vector2(80, 60)));
        _gameOverTextEntity.Set(new DynamicText
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
        _gameOverTextEntity.Set(new Visible());
    }

    private void RestartRunner(in Entity playerEntity)
    {
        // Reset player state
        var runnerState = playerEntity.Get<RunnerState>();
        runnerState.IsGameOver = false;
        runnerState.Score = 0;
        runnerState.IsGrounded = false;
        runnerState.JumpHeld = false;
        runnerState.GameTime = 0;
        _restartKeyHeld = true; // prevent re-triggering on same frame

        // Reset player position
        ref var transform = ref playerEntity.Get<Transform>();
        transform.Position = RunnerConstants.PlayerStartPosition;
        transform.LastPosition = RunnerConstants.PlayerStartPosition;

        // Reset velocity
        if (playerEntity.Has<Velocity>())
        {
            var velocity = playerEntity.Get<Velocity>();
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
        var spawnPoints = World.GetEntities().With<SpawnPoint>().With<Transform>().AsEnumerable();
        foreach (var sp in spawnPoints)
        {
            ref var spTransform = ref sp.Get<Transform>();
            spTransform.SetPositionY(RunnerConstants.SpawnPointBaseY);
        }

        // Clean up all collectibles and obstacles
        var collectibles = World.GetEntities().With<EntityInfo>().AsEnumerable();
        var toDispose = new global::System.Collections.Generic.List<Entity>();
        foreach (var e in collectibles)
        {
            var info = e.Get<EntityInfo>();
            if (info.Type is "Collectible" or "Obstacle")
            {
                toDispose.Add(e);
            }
        }
        foreach (var e in toDispose) e.Dispose();

        Logger.Info("Runner restarted.");
    }
}
