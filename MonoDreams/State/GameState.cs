using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace MonoDreams.State
{
    public class GameState
    {
        public float Time;
        public KeyboardState KeyboardState;

        public GameState(float time, KeyboardState keyboardState)
        {
            Time = time;
            KeyboardState = keyboardState;
        }
    }
}