using System;
using System.Collections.Generic;
using System.Text.Json;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Input;
using MonoDreams.Platform;
using MonoDreams.State;

namespace MonoDreams.System.Input;

public sealed class InputReplaySystem : ISystem<GameState>
{
    private readonly Game _game;
    private readonly Dictionary<string, AInputState> _actionMap;
    private readonly List<InputReplayCommand> _commands;
    private readonly HashSet<string> _pressedActions = new();

    private int _cursor;

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// When set, the system does <b>not</b> auto-exit the game when its command queue drains — another
    /// driver owns the session lifetime. The editor-op channel sets this (via the editor screen) so the
    /// keyboard replay running out does not kill an editor-op run before its ops + assertions complete;
    /// the editor-op driver then requests exit once its own queue drains. Default <c>null</c> →
    /// historical behaviour (auto-exit on drain) is unchanged.
    /// </summary>
    public Func<bool> SuppressAutoExit { get; set; }

    private InputReplaySystem(Game game, Dictionary<string, AInputState> actionMap, InputReplayPlan plan)
    {
        _game = game;
        _actionMap = actionMap;
        _commands = plan.Commands;

        Logger.Info($"InputReplaySystem loaded: \"{plan.Description}\" ({_commands.Count} commands)");
    }

    public static InputReplaySystem TryLoad(string debugDirectory, Dictionary<string, AInputState> actionMap, Game game)
    {
        var filePath = PlatformServices.Current.CombinePath(debugDirectory, "input_replay.json");
        if (!PlatformServices.Current.FileExists(filePath))
        {
            Logger.Debug($"No input_replay.json found at {filePath}. Replay disabled.");
            return null;
        }

        try
        {
            var json = PlatformServices.Current.ReadAllText(filePath);
            var plan = JsonSerializer.Deserialize<InputReplayPlan>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (plan?.Commands == null || plan.Commands.Count == 0)
            {
                Logger.Warning("input_replay.json has no commands. Replay disabled.");
                return null;
            }

            return new InputReplaySystem(game, actionMap, plan);
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load input_replay.json: {ex.Message}");
            return null;
        }
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        // Advance cursor: execute all commands whose time has arrived
        while (_cursor < _commands.Count && _commands[_cursor].Time <= state.TotalTime)
        {
            var cmd = _commands[_cursor];

            if (cmd.Type == "press")
            {
                _pressedActions.Add(cmd.Action);
                Logger.Info($"Replay: press '{cmd.Action}' at GT {state.TotalTime:F2}");
            }
            else if (cmd.Type == "release")
            {
                _pressedActions.Remove(cmd.Action);
                Logger.Info($"Replay: release '{cmd.Action}' at GT {state.TotalTime:F2}");
            }

            _cursor++;
        }

        // Update all registered input states every frame
        foreach (var (name, inputState) in _actionMap)
        {
            inputState.Update(_pressedActions.Contains(name), state);
        }

        // When all commands consumed and no actions pressed, exit — unless another driver (e.g. the
        // editor-op channel) is holding the session open and owns the exit.
        if (_cursor >= _commands.Count && _pressedActions.Count == 0)
        {
            if (SuppressAutoExit?.Invoke() == true) return;
            Logger.Info("Replay complete. Exiting game.");
            _game.Exit();
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
