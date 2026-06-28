#if DEBUG
using System.Collections.Concurrent;
using System.Reflection;
using DefaultEcs;
using DefaultEcs.Serialization;

namespace MonoDreams.Examples.Inspector;

public class ComponentIntrospector : IComponentReader
{
    private readonly List<ComponentSnapshot> _accumulator = new();

    private static readonly ConcurrentDictionary<Type, FieldInfo[]> FieldCache = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

    public void OnRead<T>(in T component, in Entity entity)
    {
        var type = typeof(T);
        var fields = FieldCache.GetOrAdd(type, t =>
            t.GetFields(BindingFlags.Public | BindingFlags.Instance));
        var properties = PropertyCache.GetOrAdd(type, t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .ToArray());

        _accumulator.Add(new ComponentSnapshot
        {
            TypeName = type.Name,
            Type = type,
            BoxedValue = component,
            Fields = fields,
            Properties = properties
        });
    }

    public List<ComponentSnapshot> ReadEntity(Entity entity)
    {
        _accumulator.Clear();
        entity.ReadAllComponents(this);
        return new List<ComponentSnapshot>(_accumulator);
    }
}
#endif
