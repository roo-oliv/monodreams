#nullable enable
using System.Collections.Generic;
using System.Text.Json;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// The ONE <b>diff-based override</b> computation shared by the compacting writer
/// (<c>SceneWriter</c>, when it serializes a linked prefab instance) and live propagation
/// (<c>PrefabPropagation</c>, when it captures an instance's overrides before rebuilding it). An
/// instance's stored <c>components{}</c> is exactly <c>core.Transform</c> (always instance-owned) plus
/// its <b>overrides</b>: components whose canonical bytes DIFFER from the prefab root's same-key bytes
/// (a change), or that the prefab root does not have at all (an addition). A component whose canonical
/// bytes EQUAL the prefab root's is <b>inherited</b> and omitted — re-expansion restores it from the
/// prefab.
///
/// <para>Override detection is <b>diff-based, not bookkept</b> (pre-mortem #1): there are no per-edit
/// override flags to maintain or desync — the writer serializes the live component and the prefab root
/// component through the same registry and compares their <see cref="CanonicalJson"/> bytes.
/// Determinism (the canonical policy) is what makes byte-equality a reliable "inherited" test.</para>
///
/// <para><b>v1 limitation — removals do not persist.</b> A component the PREFAB has but that was
/// removed on the instance is not tracked: re-expansion restores it (the removal is lost). The tracked
/// per-instance removal set is named terrain; v1 supports whole-component overrides and additions only.</para>
/// </summary>
public static class PrefabDiff
{
    /// <summary>
    /// The instance's compact <c>components{}</c>: <c>core.Transform</c> (always kept, never compared —
    /// it is the instance's placement) plus every component in <paramref name="instanceComponents"/>
    /// whose canonical bytes differ from — or that is absent in — <paramref name="prefabRootComponents"/>.
    /// A component present-and-byte-equal in both is inherited and omitted. Deterministic given the
    /// canonical policy.
    /// </summary>
    public static Dictionary<string, JsonElement> ComputeOverrides(
        IReadOnlyDictionary<string, JsonElement> instanceComponents,
        IReadOnlyDictionary<string, JsonElement> prefabRootComponents)
    {
        var overrides = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in instanceComponents)
        {
            // Transform is always instance-owned (it is where the instance stands) — never inherited.
            if (key == EngineComponentSerializers.TransformKey)
            {
                overrides[key] = value;
                continue;
            }

            // Inherited: present in the prefab root AND byte-equal → omit (re-expansion restores it).
            if (prefabRootComponents.TryGetValue(key, out var prefabValue) &&
                CanonicalJson.CanonicalEquals(value, prefabValue))
                continue;

            // Override (byte-different) or addition (prefab root lacks the key) → keep.
            overrides[key] = value;
        }

        return overrides;
    }
}
