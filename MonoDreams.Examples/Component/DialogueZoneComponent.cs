namespace MonoDreams.Examples.Component;

public class DialogueZoneComponent
{
    public string YarnNodeName { get; set; }
    public string NpcName { get; set; }
    public bool OneTimeOnly { get; set; }
    public bool AutoStart { get; set; }
    public bool HasBeenTriggered { get; set; }

    public DialogueZoneComponent(string yarnNodeName, bool oneTimeOnly = false, bool autoStart = true, string npcName = null)
    {
        YarnNodeName = yarnNodeName;
        NpcName = npcName;
        OneTimeOnly = oneTimeOnly;
        AutoStart = autoStart;
        HasBeenTriggered = false;
    }
}