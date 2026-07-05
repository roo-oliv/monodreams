#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DefaultEcs;
using DefaultEcs.Serialization;

namespace MonoDreams.LevelEditor.UI;

/// <summary>One "name: value" row of a component's read-only member display.</summary>
public readonly struct InspectorMember
{
    public readonly string Name;
    public readonly string Value;

    public InspectorMember(string name, string value)
    {
        Name = name;
        Value = value;
    }
}

/// <summary>A component attached to an entity as the INSPECTOR renders it: its short type name (the
/// row label), its <see cref="Type"/> (whose <see cref="Type.FullName"/> keys the expand state), and
/// its public members formatted read-only as "name: value" rows.</summary>
public sealed class InspectedComponent
{
    public string TypeName { get; }
    public Type Type { get; }
    public IReadOnlyList<InspectorMember> Members { get; }

    public InspectedComponent(string typeName, Type type, IReadOnlyList<InspectorMember> members)
    {
        TypeName = typeName;
        Type = type;
        Members = members;
    }
}

/// <summary>
/// Pure reflection helper for the editor's INSPECTOR section: enumerates which components are
/// attached to an entity and formats each component's public fields/properties as read-only
/// "name: value" rows. World-only (no GraphicsDevice), unit-testable.
///
/// <para><b>Component enumeration</b> uses DefaultEcs' <c>Entity.ReadAllComponents</c> +
/// <see cref="IComponentReader"/> — the same mechanism <c>ComponentSerializerRegistry</c> uses to
/// discover every component on an entity (it is the only way to see a component with no registered
/// serializer). Every component the entity carries is listed (engine tags included) — this is a
/// state viewer, not the serializer's opt-in set. Components are sorted by type name for a stable,
/// diffable display.</para>
///
/// <para><b>Member reflection</b> reads public instance fields and readable, non-indexer public
/// instance properties, sorted by name. It is deliberately <b>read-only and defensive</b>: it never
/// recurses into nested objects (each value is a single formatted line via
/// <see cref="FormatValue"/>), every getter is guarded (a throwing property renders
/// <c>&lt;error&gt;</c>, never a crash), nulls render <c>null</c>, and both the member count
/// (<see cref="MaxMembers"/>) and each value's length (<see cref="MaxValueLength"/>) are capped so a
/// large component (e.g. a mesh buffer) cannot flood the panel. This handles arbitrary component
/// types — structs, class refs, nulls — gracefully without throwing (editing is out of scope v1).</para>
/// </summary>
public static class ComponentInspector
{
    /// <summary>The most member rows shown per component; the rest collapse into a "… (+N more)" row.</summary>
    public const int MaxMembers = 40;

    /// <summary>The longest a single member value string may be; longer values are truncated with "…".</summary>
    public const int MaxValueLength = 80;

    /// <summary>Enumerates the components attached to <paramref name="entity"/>, sorted by type name,
    /// each with its formatted member rows. An empty list for a dead entity.</summary>
    public static List<InspectedComponent> Inspect(Entity entity)
    {
        var result = new List<InspectedComponent>();
        if (!entity.IsAlive) return result;

        var collector = new Collector();
        entity.ReadAllComponents(collector);
        foreach (var (type, value) in collector.Found)
            result.Add(new InspectedComponent(type.Name, type, Members(value, type)));

        result.Sort((a, b) => string.CompareOrdinal(a.TypeName, b.TypeName));
        return result;
    }

    /// <summary>Formats a component instance's public fields + readable properties as sorted
    /// "name: value" rows (read-only). Never throws — a throwing getter renders <c>&lt;error&gt;</c>.</summary>
    public static IReadOnlyList<InspectorMember> Members(object? component, Type type)
    {
        var members = new List<InspectorMember>();
        if (component == null) return members;

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            members.Add(new InspectorMember(field.Name, ReadField(field, component)));

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length != 0) continue;
            members.Add(new InspectorMember(prop.Name, ReadProperty(prop, component)));
        }

        members.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        if (members.Count > MaxMembers)
        {
            var extra = members.Count - MaxMembers;
            members.RemoveRange(MaxMembers, extra);
            members.Add(new InspectorMember("…", $"(+{extra} more)"));
        }
        return members;
    }

    /// <summary>Formats a single member value for display: <c>null</c> for null; the invariant
    /// string form for scalars/enums/known spatial types; a guarded <c>ToString()</c> otherwise —
    /// truncated to <see cref="MaxValueLength"/>. Never throws.</summary>
    public static string FormatValue(object? value)
    {
        if (value == null) return "null";
        string text;
        try
        {
            text = value switch
            {
                string s => s,
                float f => f.ToString("0.###", CultureInfo.InvariantCulture),
                double d => d.ToString("0.###", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? value.GetType().Name,
            };
        }
        catch
        {
            return "<error>";
        }

        if (text.Length > MaxValueLength)
            text = text.Substring(0, MaxValueLength - 1) + "…";
        return text;
    }

    private static string ReadField(FieldInfo field, object component)
    {
        try { return FormatValue(field.GetValue(component)); }
        catch { return "<error>"; }
    }

    private static string ReadProperty(PropertyInfo prop, object component)
    {
        try { return FormatValue(prop.GetValue(component)); }
        catch { return "<error>"; }
    }

    /// <summary>The <see cref="IComponentReader"/> that boxes every component the entity carries.</summary>
    private sealed class Collector : IComponentReader
    {
        public readonly List<(Type Type, object? Value)> Found = new();

        public void OnRead<T>(in T component, in Entity componentOwner)
            => Found.Add((typeof(T), component));
    }
}
