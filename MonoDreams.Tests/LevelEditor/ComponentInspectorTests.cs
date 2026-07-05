#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.LevelEditor.UI;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>A struct component with a mix of member types, to prove the read-only reflection viewer
/// formats scalars, strings, bools, enums and spatial structs.</summary>
public struct MixedInspectorComponent
{
    public int Health;
    public float Speed;
    public string Label;
    public bool Active;
    public Vector2 Position;
    public SampleFacing Facing;
}

public enum SampleFacing { Left, Right }

/// <summary>A class component with nullable ref members, to prove null renders as "null".</summary>
public sealed class RefInspectorComponent
{
    public string? Name;
    public object? Payload;
}

/// <summary>A class component whose getter throws, to prove the viewer is guarded (never crashes).</summary>
public sealed class ThrowingInspectorComponent
{
    public int Ok => 1;
    public int Bad => throw new InvalidOperationException("boom");
}

/// <summary>
/// Protects the INSPECTOR's reflection (<see cref="ComponentInspector"/>): it enumerates which
/// components are attached to an entity (via DefaultEcs <c>ReadAllComponents</c>, sorted) and
/// formats each component's public members as read-only "name: value" rows — handling arbitrary
/// types (structs, refs, nulls, throwing getters) gracefully without throwing. Pure logic.
/// </summary>
public class ComponentInspectorTests
{
    [Fact]
    public void Inspect_ListsAttachedComponents_SortedByTypeName()
    {
        using var world = new World();
        var e = world.CreateEntity();
        e.Set(new MixedInspectorComponent { Health = 42 });
        e.Set(new RefInspectorComponent());

        var components = ComponentInspector.Inspect(e);
        var names = components.Select(c => c.TypeName).ToList();

        Assert.Contains(nameof(MixedInspectorComponent), names);
        Assert.Contains(nameof(RefInspectorComponent), names);
        // Sorted by type name (ordinal): "Mixed…" before "Ref…".
        Assert.True(names.IndexOf(nameof(MixedInspectorComponent)) < names.IndexOf(nameof(RefInspectorComponent)));
    }

    [Fact]
    public void Members_MixedFieldTypes_FormatsEachAsNameColonValue()
    {
        var component = new MixedInspectorComponent
        {
            Health = 42,
            Speed = 1.5f,
            Label = "hero",
            Active = true,
            Position = new Vector2(3, 4),
            Facing = SampleFacing.Right,
        };

        var members = ComponentInspector.Members(component, typeof(MixedInspectorComponent));

        // Sorted by name (ordinal).
        Assert.Equal(new[] { "Active", "Facing", "Health", "Label", "Position", "Speed" },
            members.Select(m => m.Name).ToArray());
        Assert.Equal("True", Value(members, "Active"));
        Assert.Equal("Right", Value(members, "Facing"));
        Assert.Equal("42", Value(members, "Health"));
        Assert.Equal("hero", Value(members, "Label"));
        Assert.Equal("1.5", Value(members, "Speed"));
        var pos = Value(members, "Position");
        Assert.Contains("3", pos);
        Assert.Contains("4", pos);
    }

    [Fact]
    public void Members_NullRefMember_RendersNull()
    {
        var members = ComponentInspector.Members(new RefInspectorComponent(), typeof(RefInspectorComponent));
        Assert.Equal("null", Value(members, "Name"));
        Assert.Equal("null", Value(members, "Payload"));
    }

    [Fact]
    public void Members_ThrowingGetter_RendersError_NeverThrows()
    {
        var members = ComponentInspector.Members(new ThrowingInspectorComponent(), typeof(ThrowingInspectorComponent));
        Assert.Equal("1", Value(members, "Ok"));
        Assert.Equal("<error>", Value(members, "Bad"));
    }

    [Fact]
    public void FormatValue_HandlesNullScalarsAndStrings()
    {
        Assert.Equal("null", ComponentInspector.FormatValue(null));
        Assert.Equal("7", ComponentInspector.FormatValue(7));
        Assert.Equal("1.5", ComponentInspector.FormatValue(1.5f));
        Assert.Equal("hi", ComponentInspector.FormatValue("hi"));
        Assert.Equal("Left", ComponentInspector.FormatValue(SampleFacing.Left));
    }

    [Fact]
    public void FormatValue_TruncatesLongValues()
    {
        var value = ComponentInspector.FormatValue(new string('x', ComponentInspector.MaxValueLength + 50));
        Assert.True(value.Length <= ComponentInspector.MaxValueLength);
        Assert.EndsWith("…", value);
    }

    private static string Value(IReadOnlyList<InspectorMember> members, string name)
        => members.First(m => m.Name == name).Value;
}
