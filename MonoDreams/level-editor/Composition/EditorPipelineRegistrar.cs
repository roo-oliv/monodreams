#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DefaultEcs.System;
using MonoDreams.State;
using MonoDreams.System;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// One named, ordered entry in a screen's pipeline as registered through
/// <see cref="EditorPipelineRegistrar"/>. It holds the child system, the
/// <see cref="EditTimeBehavior"/> policy declared at the registration site, and the
/// <see cref="GatedSystem"/> the registrar wrapped the child in. <see cref="IsEnabled"/> is the
/// runtime toggle the systems panel flips: it maps straight onto the gate's own
/// <see cref="GatedSystem.IsEnabled"/>, so disabling an entry stops the child in <b>both</b>
/// run modes (a master kill switch, orthogonal to the per-mode policy).
/// </summary>
public sealed class EditorPipelineEntry
{
    internal EditorPipelineEntry(string name, ISystem<GameState> system, EditTimeBehavior policy,
        bool enabledInEditByDefault)
    {
        Name = name;
        System = system;
        Policy = policy;
        EnabledInEditByDefault = enabledInEditByDefault;
        Gate = new GatedSystem(system, policy);
    }

    /// <summary>The unique, screen-chosen name of this entry (e.g. <c>"logic"</c>, <c>"editor.gizmo"</c>).</summary>
    public string Name { get; }

    /// <summary>The child system as registered (unwrapped).</summary>
    public ISystem<GameState> System { get; }

    /// <summary>The edit-time policy declared at the registration site.</summary>
    public EditTimeBehavior Policy { get; }

    /// <summary>
    /// Whether this system runs in <see cref="RunMode.Edit"/> by default — the initial state the
    /// systems panel shows for the Edit column. Today this always equals the policy's Edit column
    /// (<see cref="GatedSystem.ShouldRun"/> with <see cref="RunMode.Edit"/>); see
    /// <see cref="EditorPipelineRegistrar.Add"/> for why a contradicting declaration throws.
    /// </summary>
    public bool EnabledInEditByDefault { get; }

    /// <summary>The gate wrapping <see cref="System"/>; what <see cref="EditorPipelineRegistrar.Build"/> sequences.</summary>
    public GatedSystem Gate { get; }

    /// <summary>
    /// The runtime toggle (the systems panel's checkbox): flips the gate's
    /// <see cref="GatedSystem.IsEnabled"/>, stopping/starting the child in both modes.
    /// </summary>
    public bool IsEnabled
    {
        get => Gate.IsEnabled;
        set => Gate.IsEnabled = value;
    }
}

/// <summary>
/// The single composition seam for run-state-aware pipelines: a screen registers each pipeline
/// entry — <c>Add(name, system, policy)</c> — in execution order, then calls <see cref="Build"/>
/// to get the final <see cref="SequentialSystem{GameState}"/> with every entry wrapped in a
/// <see cref="GatedSystem"/> per its policy (a <see cref="EditTimeBehavior.RunNormally"/> gate is
/// a pass-through, so wrapping everything costs two boolean checks and buys a uniform toggle
/// handle). The registrar <b>retains</b> the ordered entry list at runtime, exposing
/// <see cref="Entries"/> / <see cref="SetEnabled"/> — the seam the editor's systems panel binds
/// to (enumerate the pipeline, flip systems on/off live).
///
/// <para><b>Why the edit-mode default is declared at the registration site, not by an interface
/// on the system.</b> A system type is reusable across games with different editor needs — the
/// same <c>CameraFollowSystem</c> may be frozen in one game's editor (the editor owns the camera)
/// and live in another's (the editor previews the follow). Baking the policy into the system type
/// (an interface / attribute) would force one game's choice on every other; keeping it a
/// registration argument keeps systems policy-unaware (ECS purity: the <i>decision to run</i>
/// belongs to the assembler) and lets each screen declare its own matrix. This is the same
/// reasoning as the foundation premise "Edit-time behaviour is a per-system policy honoured by
/// <c>GatedSystem</c>" — the registrar is that premise's composition-side counterpart.</para>
///
/// <para><b>Runtime toggling vs. policy override.</b> <see cref="SetEnabled"/> composes with the
/// existing <see cref="GatedSystem"/> mechanics: it flips the gate's <c>IsEnabled</c>, a master
/// switch that stops the child in BOTH modes. Overriding the per-mode <i>policy</i> at runtime
/// ("run collision in Edit just for now") is a deliberate follow-up for the systems-panel wave —
/// until it lands, <c>enabledInEditByDefault</c> may not contradict the policy's Edit column
/// (see <see cref="Add"/>).</para>
/// </summary>
public sealed class EditorPipelineRegistrar
{
    private readonly List<EditorPipelineEntry> _entries = new();
    private readonly Dictionary<string, EditorPipelineEntry> _byName = new(StringComparer.Ordinal);
    private bool _built;

    /// <summary>The registered entries, in pipeline (execution) order.</summary>
    public IReadOnlyList<EditorPipelineEntry> Entries => _entries;

    /// <summary>
    /// Registers <paramref name="system"/> as the next pipeline entry, wrapped in a
    /// <see cref="GatedSystem"/> with <paramref name="policy"/>.
    /// <paramref name="enabledInEditByDefault"/> declares the system's edit-mode default for the
    /// systems panel; omitted (null) it derives from the policy's Edit column. An explicit value
    /// that <b>contradicts</b> the policy throws: honouring it needs the runtime per-mode policy
    /// override (systems-panel follow-up) — until then, declare the edit-mode default through the
    /// policy itself (<see cref="EditTimeBehavior.Freeze"/> = off in Edit). Throwing is loud and
    /// honest; silently recording a value with no effect would be a lie the panel later exposes.
    /// </summary>
    /// <exception cref="ArgumentException">Duplicate <paramref name="name"/>, or a contradicting
    /// <paramref name="enabledInEditByDefault"/> declaration.</exception>
    /// <exception cref="InvalidOperationException"><see cref="Build"/> was already called.</exception>
    public EditorPipelineEntry Add(string name, ISystem<GameState> system, EditTimeBehavior policy,
        bool? enabledInEditByDefault = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Pipeline entry name must be non-empty.", nameof(name));
        if (system == null) throw new ArgumentNullException(nameof(system));
        if (_built)
            throw new InvalidOperationException(
                $"Cannot add entry '{name}': Build() was already called on this registrar.");
        if (_byName.ContainsKey(name))
            throw new ArgumentException($"Pipeline entry '{name}' is already registered.", nameof(name));

        var policyEditColumn = GatedSystem.ShouldRun(policy, RunMode.Edit);
        if (enabledInEditByDefault is { } declared && declared != policyEditColumn)
            throw new ArgumentException(
                $"Entry '{name}': enabledInEditByDefault={declared} contradicts policy {policy} " +
                $"(whose Edit column is {policyEditColumn}). A per-mode default that differs from the " +
                "policy requires the runtime policy override (systems-panel follow-up); until then " +
                "declare the edit-mode default via the policy itself (Freeze = off in Edit).",
                nameof(enabledInEditByDefault));

        var entry = new EditorPipelineEntry(name, system, policy, enabledInEditByDefault ?? policyEditColumn);
        _entries.Add(entry);
        _byName.Add(name, entry);
        return entry;
    }

    /// <summary>
    /// Builds the final pipeline: a <see cref="SequentialSystem{GameState}"/> over every entry's
    /// gate, in registration order. Callable once — the built pipeline owns the gates' (and thus
    /// the children's) <c>Dispose</c>, so a second build would double-own them.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called more than once.</exception>
    public SequentialSystem<GameState> Build()
    {
        if (_built)
            throw new InvalidOperationException(
                "Build() was already called on this registrar (the built pipeline owns the gates).");
        _built = true;
        return new SequentialSystem<GameState>(_entries.Select(e => (ISystem<GameState>)e.Gate).ToArray());
    }

    /// <summary>
    /// Flips the named entry's runtime toggle (the gate's <c>IsEnabled</c>): <c>false</c> stops the
    /// system in <b>both</b> modes, <c>true</c> restores its policy-gated behaviour.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No entry with that name — loud, listing what exists.</exception>
    public void SetEnabled(string name, bool enabled) => GetEntry(name).IsEnabled = enabled;

    /// <summary>The named entry's current runtime toggle state.</summary>
    /// <exception cref="KeyNotFoundException">No entry with that name.</exception>
    public bool IsEnabled(string name) => GetEntry(name).IsEnabled;

    /// <summary>Looks an entry up by name, throwing loudly (with the registered names) on a miss.</summary>
    /// <exception cref="KeyNotFoundException">No entry with that name.</exception>
    public EditorPipelineEntry GetEntry(string name)
        => _byName.TryGetValue(name, out var entry)
            ? entry
            : throw new KeyNotFoundException(
                $"No pipeline entry named '{name}'. Registered entries: " +
                $"{string.Join(", ", _entries.Select(e => e.Name))}.");
}
