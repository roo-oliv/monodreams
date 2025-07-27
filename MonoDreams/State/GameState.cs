using Microsoft.Xna.Framework;

namespace MonoDreams.State;

public class GameState(GameTime gameTime)
{
    public (GameTime current, GameTime last) GameTime { get; private set; } = (gameTime, gameTime);
    public float Time => (float) GameTime.current.ElapsedGameTime.TotalSeconds;
    public float LastTime => (float) GameTime.last.ElapsedGameTime.TotalSeconds;
    public float TotalTime => (float) GameTime.current.TotalGameTime.TotalSeconds;

    public void Update(GameTime gameTime)
    {
        GameTime = (gameTime, GameTime.current);
    }
}