using DefaultEcs;

namespace MonoDreams.Examples.Message;

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
