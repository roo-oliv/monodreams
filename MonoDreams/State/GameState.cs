using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace MonoDreams.State;

public class GameState
{
    public float Time;
    public float LastTime;
    public float TotalTime;
    public float LastTotalTime;
    public KeyboardState KeyboardState;
    public MouseState MouseState;

    public GameState(float time, float lastTime, float totalTime, float lastTotalTime, KeyboardState keyboardState,
        MouseState mouseState)
    {
        Time = time;
        LastTime = lastTime;
        TotalTime = totalTime;
        LastTotalTime = lastTotalTime;
        KeyboardState = keyboardState;
        MouseState = mouseState;
    }
}