#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DefaultEcs;
using DefaultEcs.Serialization;

namespace MonoDreams.LevelEditor.Inspector;

/// <summary>
/// Read-only reflection over an entity's <b>attached components</b> (the "Inspector" panel section):
/// enumerates every component actually on an entity via DefaultEcs's <c>ReadAllComponents</c> (the
/// same discovery the serializer registry uses), then reflects each component's public
/// fields/properties into <c>name: value</c> rows. Read-only display (editing values is out of
/// scope v1). Game-agnostic — it handles <b>arbitrary</b> component types (structs, classes, tags
/// with no members, refs, nulls) without throwing: every member read and every value format is
/// guarded, so a component that throws in a getter shows <c>&lt;error&gt;</c> rather than crashing
/// the editor.
///
/// <para>Component and member ordering is sorted (type name, then member name) so the panel is
/// deterministic and unit-testable.</para>
/// </summary>
public static class ComponentInspector
{
    /// <summary>Long member values are truncated to keep a panel row readable.</summary>
    public const int MaxValueLength = 80;

    /// <summary>One reflected member: its name and its current value formatted for display.</summary>
    public readonly struct Member
    {
        public readonly string Name;
        public readonly string Value;
        public Member(string name, string value) { Name = name; Value = value; }
    }

    /// <summary>One component attached to an entity: its short + full type name and its member rows
    /// (empty for a tag component with no public data).</summary>
    public sealed class ComponentInfo
    {
        public required string TypeName;
        public required string FullTypeName;
        public required IReadOnlyList<Member> Members;
        public bool HasMembers => Members.Count > 0;
    }

    /// <summary>
    /// Enumerates <paramref name="entity"/>'s components (each with its reflected member rows),
    /// sorted by type name. A dead entity yields an empty list.
    /// </summary>
    public static List<ComponentInfo> Inspect(Entity entity)
    {
        var reader = new Reader();
        if (entity.IsAlive)
            entity.ReadAllComponents(reader);
        reader.Components.Sort(static (a, b) => string.CompareOrdinal(a.TypeName, b.TypeName));
        return reader.Components;
    }

    /// <summary>Just the component type names on <paramref name="entity"/> (item 3: the attached-
    /// component list), sorted. A thin projection over <see cref="Inspect"/>.</summary>
    public static List<string> ComponentTypeNames(Entity entity)
    {
        var names = new List<string>();
        foreach (var c in Inspect(entity)) names.Add(c.TypeName);
        return names;
    }

    /// <summary>
    /// Reflects a component instance's public instance fields + readable, non-indexer properties
    /// into sorted <c>name: value</c> rows. Pure and directly testable (pass any boxed value). A
    /// null <paramref name="value"/> (a null reference-type component) yields no members; a getter
    /// that throws yields <c>&lt;error&gt;</c> for that member only.
    /// </summary>
    public static List<Member> ReadMembers(Type type, object? value)
    {
        var members = new List<Member>();
        if (value == null) return members;

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            members.Add(new Member(field.Name, SafeFormat(() => field.GetValue(value))));

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
            members.Add(new Member(prop.Name, SafeFormat(() => prop.GetValue(value))));
        }

        members.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return members;
    }

    private static string SafeFormat(Func<object?> getter)
    {
        object? raw;
        try { raw = getter(); }
        catch (Exception ex) { return $"<error: {ex.GetType().Name}>"; }
        return FormatValue(raw);
    }

    /// <summary>Formats a member value for display: culture-invariant for the numeric primitives
    /// (so <c>1.5</c> never reads as <c>1,5</c> under a comma-decimal locale), <c>null</c> for a
    /// null reference, else the value's <c>ToString()</c> (guarded + length-capped).</summary>
    public static string FormatValue(object? value)
    {
        if (value == null) return "null";
        string text;
        try
        {
            text = value switch
            {
                float f => f.ToString(CultureInfo.InvariantCulture),
                double d => d.ToString(CultureInfo.InvariantCulture),
                decimal m => m.ToString(CultureInfo.InvariantCulture),
                IFormattable fmt => fmt.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? "null",
            };
        }
        catch (Exception ex) { return $"<error: {ex.GetType().Name}>"; }

        if (text.Length > MaxValueLength)
            text = text.Substring(0, MaxValueLength - 1) + "…"; // ellipsis
        return text;
    }

    private sealed class Reader : IComponentReader
    {
        public readonly List<ComponentInfo> Components = new();

        public void OnRead<T>(in T component, in Entity componentOwner)
        {
            var type = typeof(T);
            object? boxed = component; // boxes a struct copy; a class component is the live ref
            Components.Add(new ComponentInfo
            {
                TypeName = type.Name,
                FullTypeName = type.FullName ?? type.Name,
                Members = ReadMembers(type, boxed),
            });
        }
    }
}
