using System.Collections.Generic;
using DefaultEcs;

namespace MonoDreams.Dialogue;

public class DialogueStateComponent
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
