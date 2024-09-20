using Microsoft.Xna.Framework.Graphics;

namespace MonoDreams.Component;

public class DialogueController
{
    public Speech[] Speeches;
    public int CurrentSpeechIndex;
    public bool Standby;
    
    public DialogueController(Speech[] speeches)
    {
        Speeches = speeches;
        CurrentSpeechIndex = 0;
        Standby = false;
    }
}

public class Speech
{
    public string Text;
    public string Character;
    public Texture2D Portrait;
    public bool PortraitOnLeft;
}