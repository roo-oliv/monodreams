using MonoDreams.YarnSpinner;
using Yarn;

namespace MonoDreams.Examples.Component;

public class DialogueComponent(YarnProgram program)
{
    public YarnProgram Program { get; set; } = program;
    public string DefaultStartNode { get; set; } = "Start";
    public string Language { get; set; } = "en";
    public IVariableStorage? VariableStorage { get; set; }
    public bool IsActive { get; set; } = false;
    public string? CurrentNode { get; set; }
    public DialogueNodeType CurrentNodeType { get; set; }
    public string? CurrentText { get; set; }
    public string? CurrentSpeaker { get; set; }
    public List<string> CurrentOptions { get; set; } = [];
    public int SelectedOptionIndex { get; set; } = 0;
    public bool WaitingForInput { get; set; } = false;
}

public enum DialogueNodeType
{
    Line,
    Options,
    Command,
    End,
}
