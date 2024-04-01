using DefaultEcs;
using MonoDreams.Component;

namespace HeartfeltLending.Entities;

public class Dialogue
{
    public static Entity Create(World world, string dialogueFile)
    {
        var dialogue = world.CreateEntity();
        var speeches = DialogueLoader.Load(dialogueFile);
        dialogue.Set(new DialogueController(speeches));
        return dialogue;
    }
}

public static class DialogueLoader
{
    public static Speech[] Load(string dialogueFile)
    {
        Content
    }
}