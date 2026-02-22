namespace MonoDreams.Component;

public class EntityInfo(string type, string name = null)
{
    public string Type { get; } = type;
    public string Name { get; } = name;
}
