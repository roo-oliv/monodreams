using DefaultEcs;

namespace MonoDreams.Examples.Component;

public class DialogueUIComponent
{
    public Entity DialogueBox { get; set; }
    public Entity EmoteBox { get; set; }
}

public class DialogueOptionsComponent
{
    public Entity[] OptionEntities { get; set; }
    public int SelectedOption { get; set; }
} 