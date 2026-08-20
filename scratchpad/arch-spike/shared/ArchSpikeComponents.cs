namespace MonoDreams.ArchSpike;

/// <summary>
/// The component shapes the AOT proof exercises, mirroring the engine's four real shapes:
/// a plain struct, a second struct so archetypes differ, a zero-sized tag
/// (<c>VisibleComponent</c>) and a managed class component (<c>DrawComponent</c>).
/// <para>
/// They are TOP-LEVEL and at least <c>internal</c> on purpose. <c>Arch.AOT.SourceGenerator</c> 1.0.1
/// emits <c>ArrayRegistry.Add&lt;global::Full.Type.Name&gt;()</c> without any accessibility check, so
/// a component declared as a PRIVATE NESTED type makes the generated registry fail to compile with
/// CS0122. Every MonoDreams component is already a public top-level type, so this constrains the
/// spike, not the engine — but it is why these live here rather than inside <c>Program</c>.
/// </para>
/// </summary>
#if ARCH_AOT_GENERATOR
[Arch.AOT.SourceGenerator.Component]
#endif
internal struct Position
{
    public float X;
    public float Y;
}

#if ARCH_AOT_GENERATOR
[Arch.AOT.SourceGenerator.Component]
#endif
internal struct Velocity
{
    public float X;
    public float Y;
}

/// <summary>Zero-sized tag — the <c>VisibleComponent</c> shape.</summary>
#if ARCH_AOT_GENERATOR
[Arch.AOT.SourceGenerator.Component]
#endif
internal struct Tag
{
}

/// <summary>Managed component — the <c>DrawComponent</c> shape (class, not struct).</summary>
#if ARCH_AOT_GENERATOR
[Arch.AOT.SourceGenerator.Component]
#endif
internal sealed class Payload
{
    public string Name;
    public float Depth;
}

/// <summary>
/// Sacrificial component type, used ONLY by the last probe in <see cref="ArchExercise"/>: the one
/// that asks whether Arch's component-type registry can be reset (contract item C12's
/// <c>ProcessWideState.Reset</c> claim). That probe deliberately breaks this type's registry entry,
/// so nothing else may ever use it.
/// </summary>
#if ARCH_AOT_GENERATOR
[Arch.AOT.SourceGenerator.Component]
#endif
internal struct Doomed
{
    public int N;
}

/// <summary>
/// The SECOND sacrificial component type, and it needs to be a second one: <see cref="Doomed"/> is
/// spent on <c>ComponentRegistry.Remove&lt;T&gt;()</c>, which throws and leaves its entry
/// half-removed, so it can no longer answer the next question — whether the OTHER
/// <c>Remove</c> overload, <c>Remove(Type)</c>, works. A type whose entry is already broken cannot
/// show that a clear succeeded. Like <see cref="Doomed"/>, nothing else may ever use this.
/// </summary>
#if ARCH_AOT_GENERATOR
[Arch.AOT.SourceGenerator.Component]
#endif
internal struct Doomed2
{
    public int N;
}
