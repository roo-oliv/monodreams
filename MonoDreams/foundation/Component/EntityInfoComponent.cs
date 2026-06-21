namespace MonoDreams.Component;

public class EntityInfoComponent(string type, string name = null)
{
    public string Type { get; } = type;
    public string Name { get; } = name;
}
