using DefaultEcs;

namespace MonoDreams.Examples.Objects;

public struct DialogueZone
{
    public string NodeName { get; set; }
    
    public DialogueZone(string nodeName)
    {
        NodeName = nodeName;
    }
    
    public static Entity Create(World world, string nodeName)
    {
        var entity = world.CreateEntity();
        entity.Set(new DialogueZone(nodeName));
        return entity;
    }
} 