using Yarn;

namespace MonoDreams.YarnSpinner;

public class DialogueRunner
{
    private Yarn.Dialogue _dialogue;
    private IVariableStorage _variableStorage;

    public DialogueRunner(IVariableStorage? variableStorage = null)
    {
        _variableStorage = variableStorage ?? new InMemoryVariableStorage();
        _dialogue = new Yarn.Dialogue(_variableStorage);
    }
}