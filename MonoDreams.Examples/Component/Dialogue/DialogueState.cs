using DefaultEcs;

namespace MonoDreams.Examples.Component.Dialogue;

public class DialogueState
{
    public bool IsActive;
    public bool WasTriggered;
    public Entity BoxEntity;
    public Entity TextEntity;
    public Entity IndicatorEntity;
}
