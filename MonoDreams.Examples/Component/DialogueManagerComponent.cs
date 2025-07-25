using MonoDreams.Examples.Dialogue;
using MonoDreams.YarnSpinner;

namespace MonoDreams.Examples.Component;

public class DialogueManagerComponent
{
    public DialogueRunner DialogueRunner { get; private set; }
    public InMemoryVariableStorage DialogueStorage { get; private set; }
    public YarnSpinnerImporter Importer { get; private set; }

    public DialogueManagerComponent()
    {
        DialogueStorage = new InMemoryVariableStorage();
        // DialogueRunner = new DialogueRunner(TODO, TODO, TODO, TODO, TODO, DialogueStorage);
        Importer = new YarnSpinnerImporter();
    }

    public void StartDialogue(string nodeName)
    {
        // DialogueRunner.StartDialogue(nodeName);
    }
}