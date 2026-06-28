using DefaultEcs;

namespace MonoDreams.Examples.Message;

public readonly struct DialogueEndMessage
{
    public readonly Entity DialogueEntity;
    
    public DialogueEndMessage(Entity dialogueEntity)
    {
        DialogueEntity = dialogueEntity;
    }
}
