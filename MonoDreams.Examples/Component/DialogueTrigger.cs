using Microsoft.Xna.Framework.Input;

namespace MonoDreams.Examples.Component;

public enum TriggerType
{
    Proximity,
    Interaction,
    Automatic
}

public class DialogueTrigger
{
    // How this dialogue is triggered
    public TriggerType Type { get; set; } = TriggerType.Interaction;
    
    // Which node to start dialogue from
    public string StartNode { get; set; } = "Start";
    
    // Interaction key (for Interaction trigger type)
    public Keys InteractionKey { get; set; } = Keys.E;
    
    // Trigger radius (for Proximity trigger type)
    public float ProximityRadius { get; set; } = 2.0f;
    
    // Whether this trigger can be used multiple times
    public bool OneTimeOnly { get; set; } = false;
    
    // Whether this trigger has been used
    public bool HasBeenUsed { get; set; } = false;
}
