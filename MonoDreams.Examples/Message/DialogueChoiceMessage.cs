using DefaultEcs;

namespace MonoDreams.Examples.Message;

public readonly struct DialogueChoiceMessage
{
    public readonly Entity DialogueEntity;
    public readonly int ChoiceIndex;
    
    public DialogueChoiceMessage(Entity dialogueEntity, int choiceIndex)
    {
        DialogueEntity = dialogueEntity;
        ChoiceIndex = choiceIndex;
    }
}
