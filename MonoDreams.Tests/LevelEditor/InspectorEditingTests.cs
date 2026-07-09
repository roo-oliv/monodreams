#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Physics;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Input;
using MonoDreams.LevelEditor.Inspector;
using MonoDreams.LevelEditor.Serialization;
using MonoDreams.LevelEditor.UI;
using MonoDreams.LevelEditor.Undo;
using Xunit;

namespace MonoDreams.Tests.LevelEditor;

/// <summary>
/// Protects the DevTools-grade editable Inspector's CORE (PF-A §3): the undoable value/add/remove
/// commands (incl. the struct write-back of pre-mortem #5 and the SpriteInfo⇔DrawComponent pairing of
/// pre-mortem #6), the invariant-culture parse matrix + enum cycle, the type-color mapping, the
/// registry read accessor, and the "+ Add component" candidate derivation. Pure / world-only — no
/// GraphicsDevice.
/// </summary>
public class InspectorEditingTests
{
    // A struct component (the struct write-back case) with a public field + property of each kind.
    private struct Knob
    {
        public int Count;
        public float Ratio;
        public string? Label;
        public bool Flag;
        public Vector2 Offset;
        public Mode Mode;
        public int Doubled => Count * 2; // read-only computed property
    }

    private enum Mode { Off, Low, High }

    // A class with no public parameterless ctor — the default-initializer fallback case.
    private sealed class NoDefaultCtor
    {
        public NoDefaultCtor(int x) => X = x;
        public int X;
    }

    // ── MemberEditCommand: struct write-back + undo/redo (pre-mortem #5) ─────────────────────────────

    [Fact]
    public void MemberEdit_StructField_WritesBack_AndUndoRedo()
    {
        using var world = new World();
        var e = world.CreateEntity();
        e.Set(new Knob { Count = 1 });

        var cmd = MemberEditCommand.FromCurrent(e, typeof(Knob), nameof(Knob.Count), 42);
        Assert.NotNull(cmd);
        cmd!.Apply(world);
        Assert.Equal(42, e.Get<Knob>().Count); // a struct copy without Set() write-back would have vanished
        cmd.Revert(world);
        Assert.Equal(1, e.Get<Knob>().Count);
        cmd.Apply(world); // redo
        Assert.Equal(42, e.Get<Knob>().Count);
    }

    [Fact]
    public void MemberEdit_ClassComponent_Vector2Property_RoundTrips()
    {
        using var world = new World();
        var e = world.CreateEntity();
        e.Set(new TransformComponent(new Vector2(1, 2)));

        var cmd = MemberEditCommand.FromCurrent(e, typeof(TransformComponent),
            nameof(TransformComponent.Position), new Vector2(9, 9))!;
        cmd.Apply(world);
        Assert.Equal(new Vector2(9, 9), e.Get<TransformComponent>().Position);
        cmd.Revert(world);
        Assert.Equal(new Vector2(1, 2), e.Get<TransformComponent>().Position);
    }

    [Fact]
    public void MemberEdit_DeadTarget_IsNoOp()
    {
        using var world = new World();
        var e = world.CreateEntity();
        e.Set(new Knob());
        var cmd = new MemberEditCommand(e, typeof(Knob), nameof(Knob.Count), 0, 5);
        e.Dispose();
        cmd.Apply(world); // no throw
        cmd.Revert(world);
    }

    [Fact]
    public void MemberEdit_ReadOnlyComputedProperty_IsNotBuildable()
        => Assert.Null(MemberEditCommand.FromCurrent(NewKnob(out _), typeof(Knob), nameof(Knob.Doubled), 10));

    private static Entity NewKnob(out World world)
    {
        world = new World();
        var e = world.CreateEntity();
        e.Set(new Knob());
        return e;
    }

    // ── Parse matrix (invariant culture) ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(typeof(float), "1.5", true)]
    [InlineData(typeof(float), "nope", false)]
    [InlineData(typeof(int), "42", true)]
    [InlineData(typeof(int), "4.2", false)]
    [InlineData(typeof(bool), "true", true)]
    [InlineData(typeof(bool), "1", true)]
    [InlineData(typeof(bool), "0", true)]
    [InlineData(typeof(bool), "maybe", false)]
    public void TryParse_Matrix(Type type, string raw, bool expectedOk)
        => Assert.Equal(expectedOk, InspectorValue.TryParse(type, raw, out _));

    [Fact]
    public void TryParse_Vector2_XYForm()
    {
        Assert.True(InspectorValue.TryParse(typeof(Vector2), "3, -4", out var v));
        Assert.Equal(new Vector2(3, -4), (Vector2)v!);
        Assert.False(InspectorValue.TryParse(typeof(Vector2), "3", out _)); // needs two components
    }

    [Fact]
    public void TryParse_Enum_ByName_CaseInsensitive()
    {
        Assert.True(InspectorValue.TryParse(typeof(Mode), "high", out var m));
        Assert.Equal(Mode.High, (Mode)m!);
        Assert.False(InspectorValue.TryParse(typeof(Mode), "bogus", out _));
    }

    [Fact]
    public void TryParse_String_KeepsExactText()
    {
        Assert.True(InspectorValue.TryParse(typeof(string), "hello world", out var s));
        Assert.Equal("hello world", s);
    }

    [Fact]
    public void TryParse_Float_UnderCommaDecimalCulture_StillUsesPeriod()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE"); // comma decimal
            Assert.True(InspectorValue.TryParse(typeof(float), "1.5", out var f));
            Assert.Equal(1.5f, (float)f!);
            Assert.False(InspectorValue.TryParse(typeof(float), "1,5", out _)); // comma is NOT the decimal
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Fact]
    public void NextEnumValue_Cycles_Wrapping()
    {
        Assert.Equal(Mode.Low, InspectorValue.NextEnumValue(typeof(Mode), Mode.Off));
        Assert.Equal(Mode.Off, InspectorValue.NextEnumValue(typeof(Mode), Mode.High)); // wraps
    }

    [Fact]
    public void Kind_ClassifiesSupportedTypes_ElseReadOnly()
    {
        Assert.Equal(InspectorValueKind.Float, InspectorValue.Kind(typeof(float)));
        Assert.Equal(InspectorValueKind.Int, InspectorValue.Kind(typeof(int)));
        Assert.Equal(InspectorValueKind.String, InspectorValue.Kind(typeof(string)));
        Assert.Equal(InspectorValueKind.Bool, InspectorValue.Kind(typeof(bool)));
        Assert.Equal(InspectorValueKind.Vector2, InspectorValue.Kind(typeof(Vector2)));
        Assert.Equal(InspectorValueKind.Enum, InspectorValue.Kind(typeof(Mode)));
        Assert.Equal(InspectorValueKind.ReadOnly, InspectorValue.Kind(typeof(DateTime)));
        Assert.Equal(InspectorValueKind.ReadOnly, InspectorValue.Kind(null));
    }

    // ── Type-color mapping (pure) ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Role_MapsPerTypeAndValue()
    {
        Assert.Equal(InspectorValueRole.Number, InspectorValue.Role(typeof(float), 1.5f));
        Assert.Equal(InspectorValueRole.Number, InspectorValue.Role(typeof(int), 3));
        Assert.Equal(InspectorValueRole.Number, InspectorValue.Role(typeof(Vector2), Vector2.One));
        Assert.Equal(InspectorValueRole.Text, InspectorValue.Role(typeof(string), "x"));
        Assert.Equal(InspectorValueRole.Muted, InspectorValue.Role(typeof(string), string.Empty)); // empty → muted
        Assert.Equal(InspectorValueRole.True, InspectorValue.Role(typeof(bool), true));
        Assert.Equal(InspectorValueRole.False, InspectorValue.Role(typeof(bool), false));
        Assert.Equal(InspectorValueRole.EnumValue, InspectorValue.Role(typeof(Mode), Mode.Low));
        Assert.Equal(InspectorValueRole.Muted, InspectorValue.Role(typeof(object), null)); // null → muted
        Assert.Equal(InspectorValueRole.Muted, InspectorValue.Role(typeof(DateTime), DateTime.UnixEpoch)); // unsupported → muted
    }

    [Fact]
    public void ForRole_ResolvesTheDocumentedThemeColors()
    {
        Assert.Equal(EditorTheme.Info, InspectorValue.ForRole(InspectorValueRole.Number));
        Assert.Equal(EditorTheme.Warning, InspectorValue.ForRole(InspectorValueRole.Text));
        Assert.Equal(EditorTheme.Success, InspectorValue.ForRole(InspectorValueRole.True));
        Assert.Equal(EditorTheme.Danger, InspectorValue.ForRole(InspectorValueRole.False));
        Assert.Equal(EditorTheme.Accent, InspectorValue.ForRole(InspectorValueRole.EnumValue));
        Assert.Equal(EditorTheme.TextMuted, InspectorValue.ForRole(InspectorValueRole.Muted));
    }

    // ── Add / Remove commands + guardrails ───────────────────────────────────────────────────────────

    [Fact]
    public void AddComponent_AddsDefault_AndUndoRemoves()
    {
        using var world = new World();
        var e = world.CreateEntity();
        e.Set(new TransformComponent(Vector2.Zero));

        var cmd = new AddComponentCommand(e, typeof(RigidBodyComponent), new RigidBodyComponent());
        cmd.Apply(world);
        Assert.True(e.Has<RigidBodyComponent>());
        cmd.Revert(world);
        Assert.False(e.Has<RigidBodyComponent>());
    }

    [Fact]
    public void RemoveComponent_SnapshotsFieldForField_AndUndoRestores()
    {
        using var world = new World();
        var e = world.CreateEntity();
        e.Set(new TransformComponent(Vector2.Zero));
        e.Set(new RigidBodyComponent(mass: 5f));

        var cmd = RemoveComponentCommand.Create(e, typeof(RigidBodyComponent))!;
        Assert.NotNull(cmd);
        cmd.Apply(world);
        Assert.False(e.Has<RigidBodyComponent>());
        cmd.Revert(world);
        Assert.True(e.Has<RigidBodyComponent>());
        Assert.Equal(5f, e.Get<RigidBodyComponent>().Mass); // restored field-for-field
    }

    // SpriteInfo ⇔ DrawComponent pairing (pre-mortem #6), BOTH ways incl. undo.
    [Fact]
    public void AddSpriteInfo_AlsoAddsPairedDrawComponent_UndoRemovesBoth()
    {
        using var world = new World();
        var e = world.CreateEntity();
        e.Set(new TransformComponent(Vector2.Zero));

        var cmd = new AddComponentCommand(e, typeof(SpriteInfoComponent),
            new SpriteInfoComponent { Target = RenderTargetID.Main });
        cmd.Apply(world);
        Assert.True(e.Has<SpriteInfoComponent>());
        Assert.True(e.Has<DrawComponent>()); // the transient pair is added
        cmd.Revert(world);
        Assert.False(e.Has<SpriteInfoComponent>());
        Assert.False(e.Has<DrawComponent>()); // both gone
    }

    [Fact]
    public void RemoveSpriteInfo_AlsoRemovesPairedDrawComponent_UndoRestoresBoth()
    {
        using var world = new World();
        var e = world.CreateEntity();
        e.Set(new TransformComponent(Vector2.Zero));
        e.Set(new SpriteInfoComponent { Target = RenderTargetID.HUD, LayerDepth = 0.3f });
        e.Set(new DrawComponent { Type = DrawElementType.Sprite, Target = RenderTargetID.HUD });

        var cmd = RemoveComponentCommand.Create(e, typeof(SpriteInfoComponent))!;
        cmd.Apply(world);
        Assert.False(e.Has<SpriteInfoComponent>());
        Assert.False(e.Has<DrawComponent>()); // transient pair removed
        cmd.Revert(world);
        Assert.True(e.Has<SpriteInfoComponent>());
        Assert.Equal(0.3f, e.Get<SpriteInfoComponent>().LayerDepth);
        Assert.True(e.Has<DrawComponent>()); // pair restored (re-derived from the sprite's Target)
        Assert.Equal(RenderTargetID.HUD, e.Get<DrawComponent>().Target);
    }

    // ── Registry read accessor + candidate derivation ────────────────────────────────────────────────

    [Fact]
    public void Registry_ExposesRegisteredComponents_AndStructuralFlag()
    {
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();

        var all = registry.RegisteredComponents();
        Assert.Contains(all, kv => kv.Type == typeof(TransformComponent) && kv.Key == EngineComponentSerializers.TransformKey);
        Assert.Contains(all, kv => kv.Type == typeof(ChildOfComponent));

        Assert.True(registry.IsStructural(typeof(ChildOfComponent)));
        Assert.True(registry.IsStructural(typeof(SceneEntityIdComponent)));
        Assert.False(registry.IsStructural(typeof(TransformComponent)));
        Assert.Equal(typeof(RigidBodyComponent), registry.TypeForKey(EngineComponentSerializers.RigidBodyKey));
        Assert.Null(registry.TypeForKey("nope.Missing"));
    }

    [Fact]
    public void AddCandidates_AreRegisteredMinusPresentMinusStructural_SortedByKey()
    {
        var registry = new ComponentSerializerRegistry();
        registry.RegisterEngineComponents();
        var present = new HashSet<Type> { typeof(TransformComponent) };

        var candidates = InspectorAddCandidates.Derive(registry.RegisteredComponents(), present, registry.IsStructural);
        var types = candidates.Select(c => c.Type).ToHashSet();

        Assert.DoesNotContain(typeof(TransformComponent), types);      // present
        Assert.DoesNotContain(typeof(ChildOfComponent), types);        // structural
        Assert.DoesNotContain(typeof(SceneEntityIdComponent), types);  // structural
        Assert.DoesNotContain(typeof(SpriteInfoComponent), types);     // never-addable (palette-authored)
        Assert.DoesNotContain(typeof(BoundaryComponent), types);       // never-addable (boundary tool)
        Assert.Contains(typeof(RigidBodyComponent), types);            // a plain addable engine component
        Assert.Contains(typeof(CameraFollowTargetComponent), types);

        var keys = candidates.Select(c => c.Key).ToList();
        Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal).ToList(), keys); // deterministic
        Assert.All(candidates, c => Assert.Equal(c.Type.Name, c.DisplayName));
    }

    // ── Default-initializer table ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Defaults_BoxCollider_UsesPassiveFootprint()
    {
        using var world = new World();
        var e = world.CreateEntity();
        var box = Assert.IsType<BoxColliderComponent>(InspectorComponentDefaults.Build(typeof(BoxColliderComponent), e));
        Assert.True(box.Passive); // a static footprint blocker
    }

    [Fact]
    public void Defaults_TypeWithoutParameterlessCtor_StillBuildsAnInstance()
    {
        using var world = new World();
        var e = world.CreateEntity();
        Assert.IsType<NoDefaultCtor>(InspectorComponentDefaults.Build(typeof(NoDefaultCtor), e));
    }

    // ── Keyboard-ownership gate: typing in the Inspector never fires an editor chord ──────────────────

    [Fact]
    public void ShortcutGate_InspectorEditing_SuppressesEditingShortcuts()
    {
        var typing = new ViewportShortcutContext
        {
            CursorOverViewport = true, Editing = true, InspectorEditing = true,
        };
        Assert.False(typing.AllowsEditing); // G/S/R/Delete cannot fire while a field owns the keyboard

        var idle = new ViewportShortcutContext { CursorOverViewport = true, Editing = true };
        Assert.True(idle.AllowsEditing);
    }
}
