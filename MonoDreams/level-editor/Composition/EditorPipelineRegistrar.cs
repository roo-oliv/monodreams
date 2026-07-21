#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DefaultEcs.System;
using DefaultEcs.Threading;
using MonoDreams.State;
using MonoDreams.System;

namespace MonoDreams.LevelEditor.Composition;

/// <summary>
/// The tri-state enabled view of a pipeline entry, as the systems panel renders it. A <b>leaf</b>
/// is two-valued (<see cref="On"/>/<see cref="Off"/> — its gate's toggle). A <b>group</b>'s state
/// is <b>derived from its descendant leaves</b> (the Gmail/Material checkbox convention): every
/// leaf enabled → <see cref="On"/> (checked), none → <see cref="Off"/> (unchecked), some →
/// <see cref="Mixed"/> (the indeterminate minus-bar state).
/// </summary>
public enum PipelineEnabledState
{
    /// <summary>No descendant leaf is enabled (leaf: the toggle is off).</summary>
    Off,

    /// <summary>Some — but not all — descendant leaves are enabled (groups only).</summary>
    Mixed,

    /// <summary>Every descendant leaf is enabled (leaf: the toggle is on).</summary>
    On,
}

/// <summary>How a group composes its children into one system: in order
/// (<see cref="Sequential"/>, the default) or concurrently over an <c>IParallelRunner</c>
/// (<see cref="Parallel"/> — e.g. the reference screen's hardware-input pair).</summary>
public enum PipelineCompositeKind
{
    Sequential,
    Parallel,
}

/// <summary>
/// One named node in a screen's pipeline as registered through
/// <see cref="EditorPipelineRegistrar"/> — either a <b>leaf</b> (one system) or a <b>group</b>
/// (a named composite of child entries; see <see cref="EditorPipelineRegistrar.AddGroup"/>).
/// It holds the child system (for a group: the composite the registrar built), the
/// <see cref="EditTimeBehavior"/> policy declared at the registration site, and the
/// <see cref="GatedSystem"/> the registrar wrapped it in.
///
/// <para><b>Enabled semantics (the systems panel's checkbox).</b> The runtime toggle axis lives
/// on the LEAVES: a leaf's <see cref="IsEnabled"/> maps straight onto its gate's
/// <see cref="GatedSystem.IsEnabled"/>, stopping the child in <b>both</b> run modes (a master
/// kill switch, orthogonal to the per-mode policy). A <b>group</b> has no toggle state of its
/// own: its <see cref="EnabledState"/> is <b>derived</b> from its descendant leaves (all → On,
/// none → Off, some → Mixed), and setting its <see cref="IsEnabled"/> <b>cascades</b> to every
/// descendant leaf. The group's own gate enforces the group <i>policy</i> only — its
/// <c>IsEnabled</c> is never flipped by the toggle seam, so the derived state can never claim
/// "enabled" while a hidden group switch blocks everything.</para>
/// </summary>
public sealed class EditorPipelineEntry
{
    private readonly List<EditorPipelineEntry> _children = new();

    /// <summary>Leaf constructor: the gate wraps the system immediately.</summary>
    internal EditorPipelineEntry(string name, string localName, EditorPipelineEntry? parent,
        ISystem<GameState> system, EditTimeBehavior policy, bool enabledInEditByDefault)
    {
        Name = name;
        LocalName = localName;
        Parent = parent;
        System = system;
        Policy = policy;
        EnabledInEditByDefault = enabledInEditByDefault;
        Gate = new GatedSystem(system, policy);
    }

    /// <summary>Group constructor: the composite (and its gate) is sealed after the children
    /// callback ran, in <see cref="SealGroup"/>.</summary>
    internal EditorPipelineEntry(string name, string localName, EditorPipelineEntry? parent,
        EditTimeBehavior policy, PipelineCompositeKind kind, bool enabledInEditByDefault)
    {
        Name = name;
        LocalName = localName;
        Parent = parent;
        Policy = policy;
        Kind = kind;
        IsGroup = true;
        EnabledInEditByDefault = enabledInEditByDefault;
        System = null!; // sealed by SealGroup once the children exist
        Gate = null!;
    }

    internal void AddChild(EditorPipelineEntry child) => _children.Add(child);

    internal void SealGroup(ISystem<GameState> composite)
    {
        System = composite;
        Gate = new GatedSystem(composite, Policy);
    }

    /// <summary>The unique, hierarchical name of this entry: a top-level entry's registered name
    /// (e.g. <c>"logic"</c>, <c>"editor.gizmo"</c>); a group child's name prefixed with its
    /// group's (e.g. <c>"logic.movement"</c>).</summary>
    public string Name { get; }

    /// <summary>The registration-site segment of <see cref="Name"/> (for a top-level entry the
    /// two are equal). The panel labels child rows with this — indentation already conveys the
    /// group.</summary>
    public string LocalName { get; }

    /// <summary>The group this entry was registered under, or null for a top-level entry.</summary>
    public EditorPipelineEntry? Parent { get; }

    /// <summary>Nesting depth: 0 for top-level entries, +1 per enclosing group.</summary>
    public int Depth => Parent == null ? 0 : Parent.Depth + 1;

    /// <summary>Whether this entry is a group (a named composite of child entries).</summary>
    public bool IsGroup { get; }

    /// <summary>How a group composes its children (meaningless for leaves; defaults Sequential).</summary>
    public PipelineCompositeKind Kind { get; }

    /// <summary>This group's child entries, in registration (execution) order; empty for a leaf.</summary>
    public IReadOnlyList<EditorPipelineEntry> Children => _children;

    /// <summary>The system this entry runs (for a group: the registrar-built composite of the
    /// children's gates), unwrapped by this entry's own gate.</summary>
    public ISystem<GameState> System { get; private set; }

    /// <summary>The edit-time policy declared at the registration site. For a group it gates the
    /// WHOLE group (exactly like the pre-group opaque composites); a child whose declared policy
    /// admits a mode still only runs when every ancestor group's policy admits it too.</summary>
    public EditTimeBehavior Policy { get; }

    /// <summary>
    /// Whether this system runs in <see cref="RunMode.Edit"/> by default — the initial state the
    /// systems panel shows for the Edit column. Today this always equals the policy's Edit column
    /// (<see cref="GatedSystem.ShouldRun"/> with <see cref="RunMode.Edit"/>); see
    /// <see cref="EditorPipelineRegistrar.Add"/> for why a contradicting declaration throws.
    /// </summary>
    public bool EnabledInEditByDefault { get; }

    /// <summary>The gate wrapping <see cref="System"/>; what the enclosing composite (or
    /// <see cref="EditorPipelineRegistrar.Build"/>, for a top-level entry) sequences.</summary>
    public GatedSystem Gate { get; private set; }

    /// <summary>
    /// The tri-state enabled view the systems panel renders: a leaf's own toggle (On/Off), or a
    /// group's state derived from its descendant leaves (all → On, none → Off, some → Mixed).
    /// </summary>
    public PipelineEnabledState EnabledState
    {
        get
        {
            if (!IsGroup)
                return Gate.IsEnabled ? PipelineEnabledState.On : PipelineEnabledState.Off;
            bool any = false, all = true;
            AccumulateLeafState(this, ref any, ref all);
            return !any ? PipelineEnabledState.Off
                : all ? PipelineEnabledState.On
                : PipelineEnabledState.Mixed;
        }
    }

    /// <summary>
    /// The runtime toggle. Leaf: flips the gate's <see cref="GatedSystem.IsEnabled"/>,
    /// stopping/starting the child in both modes; the getter mirrors it. Group: the setter
    /// <b>cascades</b> to every descendant leaf (the group's own gate is untouched — it enforces
    /// policy only); the getter is <c>EnabledState != Off</c>, i.e. "does anything under here
    /// still run" (use <see cref="EnabledState"/> for the three-valued view).
    /// </summary>
    public bool IsEnabled
    {
        get => EnabledState != PipelineEnabledState.Off;
        set
        {
            if (IsGroup) CascadeSetEnabled(this, value);
            else Gate.IsEnabled = value;
        }
    }

    private static void AccumulateLeafState(EditorPipelineEntry entry, ref bool any, ref bool all)
    {
        foreach (var child in entry._children)
        {
            if (child.IsGroup)
            {
                AccumulateLeafState(child, ref any, ref all);
            }
            else if (child.Gate.IsEnabled) any = true;
            else all = false;
        }
    }

    private static void CascadeSetEnabled(EditorPipelineEntry entry, bool enabled)
    {
        foreach (var child in entry._children)
        {
            if (child.IsGroup) CascadeSetEnabled(child, enabled);
            else child.Gate.IsEnabled = enabled;
        }
    }
}

/// <summary>
/// The child-registration surface inside <see cref="EditorPipelineRegistrar.AddGroup"/>: the same
/// <c>Add</c>/<c>AddGroup</c> shapes as the registrar, scoped to the enclosing group. Child names
/// are auto-prefixed with the group's name (<c>g.Add("movement", …)</c> inside group
/// <c>"logic"</c> registers <c>"logic.movement"</c>), so the flat name space stays unique and a
/// child is addressable via <see cref="EditorPipelineRegistrar.SetEnabled"/> by its full name.
/// </summary>
public sealed class EditorPipelineGroupBuilder
{
    private readonly EditorPipelineRegistrar _registrar;
    private readonly EditorPipelineEntry _group;

    internal EditorPipelineGroupBuilder(EditorPipelineRegistrar registrar, EditorPipelineEntry group)
    {
        _registrar = registrar;
        _group = group;
    }

    /// <summary>Registers <paramref name="system"/> as the group's next child, gate-wrapped like
    /// any entry. The policy defaults to <see cref="EditTimeBehavior.RunNormally"/> — the usual
    /// choice for children, since the group's own gate already applies the block policy (a
    /// migrated <c>Freeze</c> composite becomes a Freeze group of RunNormally children, which
    /// runs identically).</summary>
    public EditorPipelineEntry Add(string name, ISystem<GameState> system,
        EditTimeBehavior policy = EditTimeBehavior.RunNormally, bool? enabledInEditByDefault = null)
        => _registrar.AddEntry(_group, name, system, policy, enabledInEditByDefault);

    /// <summary>Registers a nested group (see <see cref="EditorPipelineRegistrar.AddGroup"/>).</summary>
    public EditorPipelineEntry AddGroup(string name, EditTimeBehavior policy,
        Action<EditorPipelineGroupBuilder> children,
        PipelineCompositeKind kind = PipelineCompositeKind.Sequential,
        IParallelRunner? runner = null)
        => _registrar.AddGroupEntry(_group, name, policy, kind, runner, children);
}

/// <summary>
/// The single composition seam for run-state-aware pipelines: a screen registers each pipeline
/// entry — <c>Add(name, system, policy)</c> for a single system, <c>AddGroup(name, policy,
/// children)</c> for a named composite with named children — in execution order, then calls
/// <see cref="Build"/> to get the final <see cref="SequentialSystem{GameState}"/> with every
/// entry wrapped in a <see cref="GatedSystem"/> per its policy (a
/// <see cref="EditTimeBehavior.RunNormally"/> gate is a pass-through, so wrapping everything
/// costs two boolean checks and buys a uniform toggle handle). The registrar <b>retains</b> the
/// entry tree at runtime, exposing <see cref="Entries"/> (flattened, pre-order, with
/// <see cref="EditorPipelineEntry.Depth"/>) / <see cref="Roots"/> (the tree) /
/// <see cref="SetEnabled"/> / <see cref="GetEnabledState"/> — the seam the editor's systems
/// panel binds to (enumerate the pipeline, flip systems on/off live).
///
/// <para><b>Groups: the registrar owns the hierarchy.</b> DefaultEcs composites
/// (<c>SequentialSystem</c>/<c>ParallelSystem</c>) do not expose their children
/// post-construction, so a screen that pre-builds a composite and registers it as one entry makes
/// its systems invisible to the panel. <see cref="AddGroup"/> inverts that: the screen registers
/// NAMED children (arbitrary nesting) and the registrar builds the composite itself — a
/// <c>SequentialSystem</c> (or <c>ParallelSystem</c>, per <see cref="PipelineCompositeKind"/>)
/// over the children's gates — and wraps it in ONE gate carrying the group's policy, exactly
/// where the old opaque composite's gate sat. Run-mode behaviour is therefore unchanged by a
/// migration (a Freeze group still freezes the whole block in Edit); what changes is visibility:
/// every child is an addressable, individually toggleable entry.</para>
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
/// existing <see cref="GatedSystem"/> mechanics: on a leaf it flips the gate's <c>IsEnabled</c>,
/// a master switch that stops the child in BOTH modes; on a group it cascades to every descendant
/// leaf (the group's own checkbox state is <i>derived</i> — see
/// <see cref="EditorPipelineEntry.EnabledState"/>). Overriding the per-mode <i>policy</i> at
/// runtime ("run collision in Edit just for now") is a deliberate follow-up for the systems-panel
/// wave — until it lands, <c>enabledInEditByDefault</c> may not contradict the policy's Edit
/// column (see <see cref="Add"/>).</para>
/// </summary>
public sealed class EditorPipelineRegistrar
{
    private readonly List<EditorPipelineEntry> _roots = new();
    private readonly List<EditorPipelineEntry> _entries = new();
    private readonly Dictionary<string, EditorPipelineEntry> _byName = new(StringComparer.Ordinal);
    private bool _built;

    /// <summary>Every registered entry — groups AND their children — flattened in pipeline
    /// (pre-)order: a group immediately precedes its children. Pair with
    /// <see cref="EditorPipelineEntry.Depth"/> to render the tree (the systems panel does).</summary>
    public IReadOnlyList<EditorPipelineEntry> Entries => _entries;

    /// <summary>The top-level entries (the tree roots), in pipeline order — what
    /// <see cref="Build"/> sequences.</summary>
    public IReadOnlyList<EditorPipelineEntry> Roots => _roots;

    /// <summary>
    /// Registers <paramref name="system"/> as the next top-level pipeline entry, wrapped in a
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
        => AddEntry(parent: null, name, system, policy, enabledInEditByDefault);

    /// <summary>
    /// Registers a named <b>group</b>: a composite the registrar builds itself over NAMED
    /// children (registered through <paramref name="children"/>; nesting allowed), wrapped in
    /// ONE gate carrying <paramref name="policy"/> — so the group freezes/runs as a whole exactly
    /// like a pre-built composite would, while every child stays visible and individually
    /// toggleable. Child names are auto-prefixed (<c>"logic"</c> + <c>"movement"</c> →
    /// <c>"logic.movement"</c>). <paramref name="kind"/> picks the composite
    /// (<see cref="PipelineCompositeKind.Parallel"/> requires <paramref name="runner"/>).
    /// </summary>
    /// <exception cref="ArgumentNullException">Parallel kind with no runner.</exception>
    /// <exception cref="InvalidOperationException">No children were registered, or
    /// <see cref="Build"/> was already called.</exception>
    public EditorPipelineEntry AddGroup(string name, EditTimeBehavior policy,
        Action<EditorPipelineGroupBuilder> children,
        PipelineCompositeKind kind = PipelineCompositeKind.Sequential,
        IParallelRunner? runner = null)
        => AddGroupEntry(parent: null, name, policy, kind, runner, children);

    internal EditorPipelineEntry AddEntry(EditorPipelineEntry? parent, string name,
        ISystem<GameState> system, EditTimeBehavior policy, bool? enabledInEditByDefault)
    {
        if (system == null) throw new ArgumentNullException(nameof(system));
        var fullName = ValidateAndQualify(parent, name);

        var policyEditColumn = GatedSystem.ShouldRun(policy, RunMode.Edit);
        if (enabledInEditByDefault is { } declared && declared != policyEditColumn)
            throw new ArgumentException(
                $"Entry '{fullName}': enabledInEditByDefault={declared} contradicts policy {policy} " +
                $"(whose Edit column is {policyEditColumn}). A per-mode default that differs from the " +
                "policy requires the runtime policy override (systems-panel follow-up); until then " +
                "declare the edit-mode default via the policy itself (Freeze = off in Edit).",
                nameof(enabledInEditByDefault));

        var entry = new EditorPipelineEntry(fullName, name, parent, system, policy,
            enabledInEditByDefault ?? policyEditColumn);
        Register(entry, parent);
        return entry;
    }

    internal EditorPipelineEntry AddGroupEntry(EditorPipelineEntry? parent, string name,
        EditTimeBehavior policy, PipelineCompositeKind kind, IParallelRunner? runner,
        Action<EditorPipelineGroupBuilder> children)
    {
        if (children == null) throw new ArgumentNullException(nameof(children));
        if (kind == PipelineCompositeKind.Parallel && runner == null)
            throw new ArgumentNullException(nameof(runner),
                $"Group '{name}': a Parallel group needs an IParallelRunner to build its ParallelSystem.");
        var fullName = ValidateAndQualify(parent, name);

        var entry = new EditorPipelineEntry(fullName, name, parent, policy, kind,
            GatedSystem.ShouldRun(policy, RunMode.Edit));
        Register(entry, parent);

        children(new EditorPipelineGroupBuilder(this, entry));
        if (entry.Children.Count == 0)
            throw new InvalidOperationException(
                $"Group '{fullName}' was registered with no children. Register at least one child, " +
                "or use Add for a single system.");

        var childGates = entry.Children.Select(c => (ISystem<GameState>)c.Gate).ToArray();
        ISystem<GameState> composite = kind == PipelineCompositeKind.Parallel
            ? new ParallelSystem<GameState>(runner!, childGates)
            : new SequentialSystem<GameState>(childGates);
        entry.SealGroup(composite);
        return entry;
    }

    private string ValidateAndQualify(EditorPipelineEntry? parent, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Pipeline entry name must be non-empty.", nameof(name));
        var fullName = parent == null ? name : $"{parent.Name}.{name}";
        if (_built)
            throw new InvalidOperationException(
                $"Cannot add entry '{fullName}': Build() was already called on this registrar.");
        if (_byName.ContainsKey(fullName))
            throw new ArgumentException($"Pipeline entry '{fullName}' is already registered.", nameof(name));
        return fullName;
    }

    private void Register(EditorPipelineEntry entry, EditorPipelineEntry? parent)
    {
        _entries.Add(entry); // appended at registration time = flattened pre-order
        _byName.Add(entry.Name, entry);
        if (parent == null) _roots.Add(entry);
        else parent.AddChild(entry);
    }

    /// <summary>
    /// Builds the final pipeline: a <see cref="SequentialSystem{GameState}"/> over every
    /// top-level entry's gate, in registration order (group composites were built at
    /// registration). Callable once — the built pipeline owns the gates' (and thus the
    /// children's) <c>Dispose</c>, so a second build would double-own them.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called more than once.</exception>
    public SequentialSystem<GameState> Build()
    {
        if (_built)
            throw new InvalidOperationException(
                "Build() was already called on this registrar (the built pipeline owns the gates).");
        _built = true;
        return new SequentialSystem<GameState>(_roots.Select(e => (ISystem<GameState>)e.Gate).ToArray());
    }

    /// <summary>
    /// Flips the named entry's runtime toggle. Leaf: the gate's <c>IsEnabled</c> — <c>false</c>
    /// stops the system in <b>both</b> modes, <c>true</c> restores its policy-gated behaviour.
    /// Group: <b>cascades</b> to every descendant leaf (the Gmail semantics the panel builds on).
    /// </summary>
    /// <exception cref="KeyNotFoundException">No entry with that name — loud, listing what exists.</exception>
    public void SetEnabled(string name, bool enabled) => GetEntry(name).IsEnabled = enabled;

    /// <summary>The named entry's current runtime toggle state (group: whether ANY descendant
    /// leaf is enabled; see <see cref="GetEnabledState"/> for the tri-state view).</summary>
    /// <exception cref="KeyNotFoundException">No entry with that name.</exception>
    public bool IsEnabled(string name) => GetEntry(name).IsEnabled;

    /// <summary>The named entry's tri-state enabled view (see
    /// <see cref="EditorPipelineEntry.EnabledState"/>).</summary>
    /// <exception cref="KeyNotFoundException">No entry with that name.</exception>
    public PipelineEnabledState GetEnabledState(string name) => GetEntry(name).EnabledState;

    /// <summary>Looks an entry up by (full) name, throwing loudly (with the registered names) on
    /// a miss. Group children use their prefixed name (<c>"logic.movement"</c>).</summary>
    /// <exception cref="KeyNotFoundException">No entry with that name.</exception>
    public EditorPipelineEntry GetEntry(string name)
        => _byName.TryGetValue(name, out var entry)
            ? entry
            : throw new KeyNotFoundException(
                $"No pipeline entry named '{name}'. Registered entries: " +
                $"{string.Join(", ", _entries.Select(e => e.Name))}.");
}
