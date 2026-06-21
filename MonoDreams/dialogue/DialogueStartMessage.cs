using DefaultEcs;

namespace MonoDreams.Dialogue;

public readonly struct DialogueStartMessage
{
    public readonly Entity DialogueEntity;
    public readonly string StartNode;
    
    public DialogueStartMessage(Entity dialogueEntity, string startNode)
    {
        DialogueEntity = dialogueEntity;
        StartNode = startNode;
    }
}
