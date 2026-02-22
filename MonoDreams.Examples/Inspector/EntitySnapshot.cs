#if DEBUG
using System.Reflection;
using DefaultEcs;

namespace MonoDreams.Examples.Inspector;

public class EntitySnapshot
{
    public Entity Entity;
    public string DisplayName;
    public List<ComponentSnapshot> Components;
    public List<EntitySnapshot> Children = new();
}

public class ComponentSnapshot
{
    public string TypeName;
    public Type Type;
    public object BoxedValue;
    public FieldInfo[] Fields;
    public PropertyInfo[] Properties;
}
#endif
