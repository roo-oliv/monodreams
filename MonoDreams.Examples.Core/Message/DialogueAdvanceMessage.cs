using DefaultEcs;

namespace MonoDreams.Examples.Message;

public readonly struct DialogueAdvanceMessage
{
    public readonly Entity DialogueEntity;
    
    public DialogueAdvanceMessage(Entity dialogueEntity)
    {
        DialogueEntity = dialogueEntity;
    }
}
