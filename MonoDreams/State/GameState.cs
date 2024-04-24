using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace MonoDreams.State;

public class GameState
{
    public (GameTime current, GameTime last) GameTime { get; private set; }
    public float Time => (float) GameTime.current.ElapsedGameTime.TotalSeconds;
    public float LastTime => (float) GameTime.last.ElapsedGameTime.TotalSeconds;
    public float TotalTime => (float) GameTime.current.TotalGameTime.TotalSeconds;

    public GameState(GameTime gameTime)
    {
        GameTime = (gameTime, gameTime);
    }
    
    public void Update(GameTime gameTime)
    {
        GameTime = (gameTime, GameTime.current);
    }
}