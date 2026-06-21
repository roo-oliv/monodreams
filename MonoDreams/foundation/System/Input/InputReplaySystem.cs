using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Input;
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

    private InputReplaySystem(Game game, Dictionary<string, AInputState> actionMap, InputReplayPlan plan)
    {
        _game = game;
        _actionMap = actionMap;
        _commands = plan.Commands;

        Logger.Info($"InputReplaySystem loaded: \"{plan.Description}\" ({_commands.Count} commands)");
    }

    public static InputReplaySystem TryLoad(string debugDirectory, Dictionary<string, AInputState> actionMap, Game game)
    {
        var filePath = Path.Combine(debugDirectory, "input_replay.json");
        if (!File.Exists(filePath))
        {
            Logger.Debug($"No input_replay.json found at {filePath}. Replay disabled.");
            return null;
        }

        try
        {
            var json = File.ReadAllText(filePath);
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

        // When all commands consumed and no actions pressed, exit
        if (_cursor >= _commands.Count && _pressedActions.Count == 0)
        {
            Logger.Info("Replay complete. Exiting game.");
            _game.Exit();
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
