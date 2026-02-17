using DefaultEcs;

namespace MonoDreams.Examples.Component.Dialogue;

public class DialogueState
{
    public bool IsActive;
    public bool WasTriggered;
    public Entity BoxEntity;
    public Entity TextEntity;
    public Entity IndicatorEntity;

    // Yarn dialogue state
    public DialoguePhase CurrentPhase;
    public string? CurrentSpeaker;
    public bool WaitingForInput;
    public bool InputConsumed;

    // Options state
    public List<string> CurrentOptions = [];
    public List<int> CurrentOptionIDs = [];
    public int SelectedOptionIndex;
    public List<Entity> OptionEntities = [];
}

public enum DialoguePhase
{
    None,
    Line,
    Options,
    Complete,
}
