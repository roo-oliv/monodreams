#if ARCH_AOT_GENERATOR

// MEASURED INCOMPATIBILITY (issue #119, wave 0, contract item 2).
//
// `Arch.AOT.SourceGenerator` 1.0.1 (Feb 2024, the latest published version) generates:
//
//     using Arch.Core.Utils;
//     ...
//     ArrayRegistry.Add<global::Some.Component>();
//
// That namespace is Arch 1.x's layout. In Arch 2.1.0 — the version this migration pins — both
// `ArrayRegistry` and `ComponentRegistry` live in `Arch.Core`, not `Arch.Core.Utils`, so the
// generated file fails to compile with CS0103 ("the name 'ArrayRegistry' does not exist in the
// current context") and NO amount of configuration on the consuming project fixes it.
//
// The method itself is unchanged (`static void Add<T>()`), so a forwarding type declared in the
// consuming compilation under the OLD namespace makes the generated code resolve and the AOT
// priming work. This file is that shim, and it is the thing wave 2/3 has to inherit: the engine
// either ships an equivalent shim next to the facade, vendors a fixed generator, or drops the
// generator in favour of registering component types itself (the facade already needs a component
// type registry for `ReadAllComponents`, so a facade-owned registration path is the likely answer).
//
// Guarded by ARCH_AOT_GENERATOR so the `UseArchAotGenerator=false` negative control keeps compiling
// (without the generator there is nothing to forward for).

namespace Arch.Core.Utils;

/// <summary>
/// Namespace shim forwarding Arch 1.x's <c>Arch.Core.Utils.ArrayRegistry</c> — the name
/// <c>Arch.AOT.SourceGenerator</c> 1.0.1 emits — onto Arch 2.1.0's <c>Arch.Core.ArrayRegistry</c>.
/// </summary>
internal static class ArrayRegistry
{
    /// <summary>
    /// Registers <typeparamref name="T"/>'s backing array with Arch ahead of any reflection, which
    /// is the whole point of the generator under NativeAOT.
    /// </summary>
    public static void Add<T>() => global::Arch.Core.ArrayRegistry.Add<T>();
}

#endif
