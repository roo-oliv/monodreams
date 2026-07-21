using Microsoft.Xna.Framework;

namespace MonoDreams.State;

/// <summary>
/// Engine-wide run state. <see cref="RunMode.Play"/> is the normal game loop;
/// <see cref="RunMode.Edit"/> is the in-game level-editor mode (the editor is a
/// game mode, not a separate app — see <c>docs/CORE_TENETS.md</c> tenet
/// "The editor is part of the game").
///
/// The run mode only changes behaviour for systems explicitly wrapped in a
/// <see cref="MonoDreams.System.GatedSystem"/>; ungated systems are run by the
/// pipeline regardless of the mode. The default is <see cref="Play"/>, so a
/// screen that never sets <see cref="GameState.RunMode"/> behaves exactly as
/// before this model existed.
/// </summary>
public enum RunMode
{
    /// <summary>Normal game loop — every gated system runs per its policy's Play column.</summary>
    Play,
    /// <summary>In-game editor mode — gated systems run per their policy's Edit column.</summary>
    Edit,
}

public class GameState
{
    public (GameTime current, GameTime last) GameTime { get; private set; }
    public float Time => (float) GameTime.current.ElapsedGameTime.TotalSeconds;
    public float LastTime => (float) GameTime.last.ElapsedGameTime.TotalSeconds;
    public float TotalTime => (float) GameTime.current.TotalGameTime.TotalSeconds;

    /// <summary>
    /// The engine-wide run mode. Defaults to <see cref="RunMode.Play"/> so existing
    /// screens are unaffected; a <see cref="MonoDreams.System.GatedSystem"/> reads this
    /// to decide whether to run its wrapped child for the current frame. Mutating it
    /// (Play↔Edit) enters/exits editing without a screen swap, so editor state is preserved.
    /// </summary>
    public RunMode RunMode { get; set; } = RunMode.Play;

    public GameState(GameTime gameTime)
    {
        GameTime = (gameTime, gameTime);
    }

    public void Update(GameTime gameTime)
    {
        GameTime = (gameTime, GameTime.current);
    }
}