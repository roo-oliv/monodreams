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

    // Previous-frame key state for manual edge detection. Pressed() is level-triggered
    // and engine JustPressed requires buffer > 0, so we track edges ourselves.
    public bool InteractHeld;
    public bool UpHeld;
    public bool DownHeld;

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
