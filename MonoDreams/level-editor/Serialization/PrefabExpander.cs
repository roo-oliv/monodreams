#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using DefaultEcs;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.LevelEditor.Component;

namespace MonoDreams.LevelEditor.Serialization;

/// <summary>
/// The ONE prefab-expansion implementation. Reconstructs a linked prefab instance — the root plus its
/// prefab-owned <c>ChildOf</c> descendants — from a <c>.mdprefab</c>, applies the instance's
/// whole-component overrides over the root, stamps the <see cref="PrefabInstanceComponent"/> marker,
/// and finishes the subtree (texture rehydration + transient <c>DrawComponent</c> restore, via
/// <see cref="SceneRehydration"/> — the reader's top-level loop never sees the prefab-owned children,
/// so the expander MUST finish them). All three consumers share it:
/// <list type="bullet">
///   <item><b>Scene load</b> — <see cref="ExpandScene"/> is the reader's whole-scene entry; the
///   <see cref="SceneSerializer.Deserialize"/> callback (<see cref="ExpandInstance"/>) expands each
///   compact <c>prefab</c> entry in place. The reader's re-tag stamps <c>SceneObjectComponent</c> + the
///   scene id on the top-level instance root.</item>
///   <item><b>Code / factory</b> — <see cref="Instantiate"/> spawns one instance (no scene), used by
///   <c>PrefabFactory</c> for the <c>EntitySpawnRequest("prefab:&lt;id&gt;")</c> channel.</item>
///   <item><b>Propagation</b> — <c>PrefabPropagation</c> calls <see cref="Instantiate"/> with the
///   captured overrides to rebuild each open instance from a just-saved prefab.</item>
/// </list>
///
/// <para><b>Fail-loud + cycles.</b> A missing prefab (<see cref="PrefabCache.Resolve"/> returns null)
/// aborts loud (the unknown-component stance) — the reader lets it surface, the factory catches it and
/// warns-and-drops. A cycle (an instance of X reached while X is already expanding, directly or
/// transitively) is refused loud via the expansion stack, with a depth cap (<see cref="MaxDepth"/>) as
/// a backstop. Resolution is memoized per pass (<see cref="PrefabCache"/>), so instantiating several
/// instances of one prefab reads it once.</para>
/// </summary>
public sealed class PrefabExpander
{
    /// <summary>The maximum prefab nesting depth expansion will follow before refusing (a cycle backstop
    /// in addition to the exact stack-membership check).</summary>
    public const int MaxDepth = 16;

    private readonly SceneSerializer _serializer;
    private readonly Func<string, PrefabData?> _source;
    private readonly Func<string, Texture2D>? _loadTexture;
    private readonly Func<string, Texture2D?>? _fileTextureLoader;

    // Per-pass state (single-threaded, synchronous expansion): the memo cache and the cycle-guard stack.
    // Owned by whichever public entry began the pass; a re-entrant (nested) call shares them.
    private PrefabCache? _cache;
    private readonly List<string> _stack = new();

    /// <param name="serializer">The component round-trip seam (also exposes the registry the expander
    /// applies overrides through). The prefab's own entities are created through it, so nested prefab
    /// entries recurse through this same expander.</param>
    /// <param name="source">The raw prefab resolver (<c>id → <see cref="PrefabData"/></c>): source-first
    /// via the editor project context in-editor, else <c>TitleContainer</c> in a shipped game, or an
    /// in-memory dictionary in tests. Wrapped in a per-pass <see cref="PrefabCache"/>.</param>
    /// <param name="loadTexture">Content-key texture loader (rehydration); null in a pure in-memory test
    /// leaves <c>SpriteSheet</c> null.</param>
    /// <param name="fileTextureLoader">Loader for <c>file:</c> asset keys (runtime drop-folder art).</param>
    public PrefabExpander(
        SceneSerializer serializer,
        Func<string, PrefabData?> source,
        Func<string, Texture2D>? loadTexture = null,
        Func<string, Texture2D?>? fileTextureLoader = null)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _loadTexture = loadTexture;
        _fileTextureLoader = fileTextureLoader;
    }

    /// <summary>
    /// Expands <paramref name="scene"/> (which may contain compact <c>prefab</c> entries) into
    /// <paramref name="world"/> through <see cref="SceneSerializer.Deserialize"/>, returning the created
    /// top-level entities indexed 1:1 with <see cref="SceneData.Entities"/> (a prefab entry's index holds
    /// its instance ROOT; the prefab-owned children are extra). The reader calls this instead of the bare
    /// <c>Deserialize</c>; ordinary scenes with no prefab entries behave identically.
    /// </summary>
    public List<Entity> ExpandScene(World world, SceneData scene)
    {
        var owns = BeginPass();
        try { return _serializer.Deserialize(world, scene, ExpandInstance); }
        finally { EndPass(owns); }
    }

    /// <summary>
    /// Instantiates a single linked instance of <paramref name="prefabId"/> into <paramref name="world"/>
    /// and returns its root (carrying <see cref="PrefabInstanceComponent"/>, subtree rehydrated). The
    /// optional <paramref name="overrides"/> map (whole-component bodies, keyed by the registry key,
    /// including <c>core.Transform</c>) is applied over the root; null spawns the prefab verbatim (the
    /// factory then places it). The caller stamps <c>SceneObjectComponent</c> / the scene id as needed.
    /// </summary>
    public Entity Instantiate(World world, string prefabId, IReadOnlyDictionary<string, JsonElement>? overrides = null)
    {
        var owns = BeginPass();
        try { return ExpandInstanceCore(world, prefabId, overrides); }
        finally { EndPass(owns); }
    }

    /// <summary>The <see cref="SceneSerializer.Deserialize"/> callback for a compact prefab entry: expand
    /// its <see cref="SceneEntityData.Prefab"/> id, applying the entry's <c>components{}</c> (Transform +
    /// overrides) over the root.</summary>
    private Entity ExpandInstance(World world, SceneEntityData entry)
        => ExpandInstanceCore(world, entry.Prefab!, entry.Components);

    private Entity ExpandInstanceCore(World world, string prefabId, IReadOnlyDictionary<string, JsonElement>? overrides)
    {
        if (_stack.Contains(prefabId))
            throw new InvalidOperationException(
                $"[level-editor] Prefab cycle detected: expanding '{prefabId}' recurses into itself " +
                $"(chain: {string.Join(" -> ", _stack)} -> {prefabId}). Refused (pre-mortem #7).");
        if (_stack.Count >= MaxDepth)
            throw new InvalidOperationException(
                $"[level-editor] Prefab expansion exceeded the max nesting depth ({MaxDepth}) at '{prefabId}' " +
                $"(chain: {string.Join(" -> ", _stack)}). Refused as a likely cycle.");

        var prefab = _cache!.Resolve(prefabId)
            ?? throw new InvalidOperationException(
                $"[level-editor] Prefab '{prefabId}' not found (no .mdprefab resolved for that id). A scene / " +
                "prefab references a prefab that does not exist; aborting expansion (the missing-prefab " +
                "fail-loud stance, sibling of the unknown-component policy).");

        // CM one-camera rule: a prefab is a class, not a scene — it must carry NO camera entity. A legacy
        // prefab file that (illegally) contains one is refused loud on expansion (the prefab writer refuses
        // it at author time; this guards a file that predates that rule).
        foreach (var entry in prefab.Scene.Entities)
            if (entry.Components != null && entry.Components.ContainsKey(EngineComponentSerializers.CameraKey))
                throw new InvalidOperationException(
                    $"[level-editor] Prefab '{prefabId}' contains a camera entity ('{EngineComponentSerializers.CameraKey}') — " +
                    "a camera belongs to a scene, not a prefab (multi-camera terrain). Refused on expansion.");

        _stack.Add(prefabId);
        List<Entity> created;
        try
        {
            // The prefab's own entities (root + children, and any nested prefab entries which recurse
            // through this SAME callback under the cycle guard).
            created = _serializer.Deserialize(world, prefab.Scene, ExpandInstance);
        }
        finally
        {
            _stack.RemoveAt(_stack.Count - 1);
        }

        var root = created[prefab.RootIndex];

        // Whole-component overrides OVER the root (Transform included when present) — pre-mortem #1: a
        // byte-different component replaces the inherited one; an inherited (omitted) component keeps the
        // prefab's value the deserialize above already set.
        if (overrides != null) ApplyOverrides(root, overrides);

        // The linked-instance marker (structurally captured — never a component body). SceneObjectComponent
        // / the scene id are the CALLER's to stamp (reader re-tag, factory, propagation), so a nested
        // instance root inside a prefab is never mistaken for a scene root.
        root.Set(new PrefabInstanceComponent(prefabId));

        // Finish the WHOLE subtree (root + prefab-owned children) — the reader's top-level loop never sees
        // the children, so the expander rehydrates textures + restores DrawComponents here.
        SceneRehydration.RehydrateTextures(created, _loadTexture, _fileTextureLoader);
        SceneRehydration.RestoreDrawComponents(created);

        return root;
    }

    /// <summary>Applies each override as a whole-component replacement on <paramref name="root"/> through
    /// its registered serializer. An override keyed by an unregistered component fails loud (a file that
    /// references a component the runtime cannot reconstruct — the unknown-component stance).</summary>
    private void ApplyOverrides(Entity root, IReadOnlyDictionary<string, JsonElement> overrides)
    {
        foreach (var (key, element) in overrides)
        {
            var serializer = _serializer.Registry.GetByKey(key)
                ?? throw new InvalidOperationException(
                    $"[level-editor] Prefab instance override references component key '{key}' but no serializer " +
                    "is registered for it. Register it before loading (engine components via " +
                    "EngineComponentSerializers.RegisterEngineComponents; game components via registry.Register).");
            serializer.Read(root, element);
        }
    }

    private bool BeginPass()
    {
        if (_cache != null) return false; // a nested/shared pass — reuse the owner's cache + stack
        _cache = new PrefabCache(_source);
        _stack.Clear();
        return true;
    }

    private void EndPass(bool owns)
    {
        if (!owns) return;
        _cache = null;
        _stack.Clear();
    }
}
