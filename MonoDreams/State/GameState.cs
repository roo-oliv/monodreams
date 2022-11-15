using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace MonoDreams.State
{
    public class GameState
    {
        public float Time;
        public float LastTime;
        public KeyboardState KeyboardState;

        public GameState(float time, float lastTime, KeyboardState keyboardState)
        {
            Time = time;
            LastTime = lastTime;
            KeyboardState = keyboardState;
        }
    }
}