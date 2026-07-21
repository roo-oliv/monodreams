#nullable enable
using System.Globalization;
using System.Linq;
using System.Threading;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Inspector;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the read-only component inspector (task items 3 + 4): enumerating an entity's attached
/// components via <c>ReadAllComponents</c> (the "which components are attached" list) and reflecting
/// each component's public fields/properties into <c>name: value</c> rows — for <b>arbitrary</b>
/// component types (structs, tags with no members, mixed field types, nulls) without throwing, with
/// culture-invariant float formatting. Pure — no GraphicsDevice.
/// </summary>
public class ComponentInspectorTests
{
    /// <summary>A component with a mix of field/property types (incl. a null reference and a
    /// computed property) — the "mixed field types" case the contract asks for.</summary>
    private struct MixedComponent
    {
        public int Count;
        public float Ratio;
        public string? Label;
        public bool Flag;
        public Vector2 Offset;
        public string? Missing; // left null
        public int Doubled => Count * 2; // read-only computed property
    }

    [Fact]
    public void ReadMembers_ReflectsMixedFieldTypes_WithInvariantFloatFormatting_AndNulls()
    {
        var c = new MixedComponent
        {
            Count = 3,
            Ratio = 1.5f,
            Label = "hi",
            Flag = true,
            Offset = new Vector2(2, 4),
            Missing = null,
        };

        var members = ComponentInspector.ReadMembers(typeof(MixedComponent), c)
            .ToDictionary(m => m.Name, m => m.Value);

        Assert.Equal("3", members["Count"]);
        Assert.Equal("1.5", members["Ratio"]); // invariant decimal point, never "1,5"
        Assert.Equal("hi", members["Label"]);
        Assert.Equal("True", members["Flag"]);
        Assert.Equal("null", members["Missing"]);
        Assert.Equal("6", members["Doubled"]); // computed read-only property is reflected too
        Assert.Contains("2", members["Offset"]); // Vector2 ToString, doesn't throw
    }

    [Fact]
    public void ReadMembers_UnderCommaDecimalCulture_StillUsesPeriod()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE"); // comma decimal
            var members = ComponentInspector.ReadMembers(typeof(MixedComponent),
                    new MixedComponent { Ratio = 1.5f })
                .ToDictionary(m => m.Name, m => m.Value);
            Assert.Equal("1.5", members["Ratio"]);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void ReadMembers_NullValue_YieldsNoMembers_AndDoesNotThrow()
    {
        Assert.Empty(ComponentInspector.ReadMembers(typeof(MixedComponent), null));
    }

    [Fact]
    public void ReadMembers_CarriesMemberTypeAndEditability_ForTheEditableInspector()
    {
        var members = ComponentInspector.ReadMembers(typeof(MixedComponent), new MixedComponent { Count = 2 });
        var count = members.First(m => m.Name == "Count");
        Assert.Equal(typeof(int), count.MemberType);
        Assert.True(count.Editable);                    // a writable int field is editable
        var doubled = members.First(m => m.Name == "Doubled");
        Assert.False(doubled.Editable);                 // a read-only computed property is not editable
        Assert.Equal(typeof(int), doubled.MemberType);  // but its type/color are still known
    }

    [Fact]
    public void Inspect_ListsAttachedComponentTypeNames_Sorted()
    {
        using var world = new World();
        var e = world.CreateEntity();
        e.Set(new TransformComponent(Vector2.Zero));
        e.Set(new EntityInfoComponent("Player", "Hero"));

        var names = ComponentInspector.ComponentTypeNames(e);

        Assert.Contains("TransformComponent", names);
        Assert.Contains("EntityInfoComponent", names);
        // Deterministic order (sorted by type name).
        Assert.Equal(names.OrderBy(n => n, global::System.StringComparer.Ordinal).ToList(), names);
    }

    [Fact]
    public void Inspect_TagComponent_HasNoMembers()
    {
        using var world = new World();
        var e = world.CreateEntity();
        e.Set(new SelectedComponent()); // empty tag struct

        var info = ComponentInspector.Inspect(e).Single(c => c.TypeName == nameof(SelectedComponent));
        Assert.False(info.HasMembers);
    }

    [Fact]
    public void Inspect_ReflectsAComponentsMemberValues()
    {
        using var world = new World();
        var e = world.CreateEntity();
        e.Set(new EntityInfoComponent("Player", "Hero"));

        var info = ComponentInspector.Inspect(e).Single(c => c.TypeName == nameof(EntityInfoComponent));
        var members = info.Members.ToDictionary(m => m.Name, m => m.Value);

        Assert.Equal("Hero", members["Name"]);
        Assert.Equal("Player", members["Type"]);
    }

    [Fact]
    public void Inspect_DeadEntity_IsEmpty()
    {
        using var world = new World();
        var e = world.CreateEntity();
        e.Dispose();
        Assert.Empty(ComponentInspector.Inspect(e));
    }
}
