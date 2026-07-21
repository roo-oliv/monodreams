#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Inspector;

namespace MonoDreams.LevelEditor.UI;

/// <summary>The collapsible sections the editor's LEFT strip can stack — distributed across the
/// Entities / Systems / Scenes tabs (<c>EditorPanelTab</c>). Entities tab = <see cref="Entities"/>
/// (the tree ALONE — the Inspector left for its dedicated right panel); Systems tab =
/// <see cref="Systems"/>; the Scenes tab shows info rows (no collapsible section). The Inspector is
/// no longer a section — it is <c>EditorPanelModel.BuildInspector</c> in the dedicated right panel.</summary>
public enum EditorPanelSection
{
    /// <summary>The pipeline listing (every registrar entry of both pipelines) — the Wave-8 systems
    /// panel, a section with per-group collapse. Hosted by the Systems tab.</summary>
    Systems,

    /// <summary>The world's entities as a parent/child tree — selectable, two-way with the
    /// viewport selection AND the dedicated Inspector panel. Hosted by the Entities tab.</summary>
    Entities,
}

/// <summary>The Scenes-tab info the model renders (root path, levels dir, current scene id). Null
/// <see cref="ProjectRoot"/> = the project is unresolved (Save disabled).</summary>
public readonly record struct EditorProjectInfo(string? ProjectRoot, string? LevelsDir, string? SceneId);

/// <summary>What a single panel row represents (drives its visuals + click behavior).</summary>
public enum PanelRowKind
{
    /// <summary>A section header (Systems / Entities) — clicking collapses/expands the whole section
    /// body.</summary>
    SectionHeader,

    /// <summary>The "UPDATE" / "DRAW" separator inside the Systems section (non-interactive).</summary>
    PipelineSubheader,

    /// <summary>A pipeline entry (leaf or group) — checkbox toggles enabled; a group's arrow
    /// collapses its children.</summary>
    PipelineEntry,

    /// <summary>A scene-tree entity — clicking the label selects it; the arrow collapses its
    /// subtree.</summary>
    SceneEntity,

    /// <summary>The Inspector's filter field row (PF-A §3, the DevTools search) — clicking focuses it;
    /// typing narrows the component + member rows.</summary>
    InspectorFilter,

    /// <summary>An Inspector component-type row — clicking expands/collapses its member values; its
    /// right-gutter <c>×</c> deletes the component (guarded).</summary>
    InspectorComponent,

    /// <summary>An Inspector member <c>name: value</c> row — clicking an editable value opens an inline
    /// field / toggles a bool / cycles an enum (PF-A §3).</summary>
    InspectorMember,

    /// <summary>The trailing "+ Add component" row (PF-A §3) — clicking opens the filterable add popup.</summary>
    InspectorAddComponent,

    /// <summary>A Scenes-panel entry (Scenes tab) — clicking it switches to that scene/screen
    /// through the dirty-gated select flow.</summary>
    SceneCatalogEntry,

    /// <summary>A non-interactive informational row (e.g. "(no selection)").</summary>
    Info,
}

/// <summary>
/// One flattened row of the right-strip editor panel. The rows are produced purely by
/// <see cref="EditorPanelModel.Build"/> from the pipeline registrars, the scene tree, the
/// Inspector data, and the collapse state — then a rendering system pools visuals over the
/// <b>visible</b> window (single vertical scroll across all sections). Pure data.
/// </summary>
public sealed class PanelRow
{
    public required PanelRowKind Kind;
    public required string Label;

    /// <summary>Indentation level (0 = section header / flush-left).</summary>
    public int Depth;

    /// <summary>Whether the row shows a disclosure arrow (collapsible/expandable).</summary>
    public bool Collapsible;

    /// <summary>Arrow state when <see cref="Collapsible"/> (true = expanded ▾, false = collapsed ▸).</summary>
    public bool Expanded;

    /// <summary>Whether the row shows an enabled-checkbox (pipeline rows only).</summary>
    public bool HasCheckbox;

    /// <summary>The tri-state checkbox value (pipeline rows).</summary>
    public PipelineEnabledState CheckboxState;

    /// <summary>Whether the group checkbox shows the indeterminate minus bar (pipeline groups only).</summary>
    public bool ShowMinusBar;

    /// <summary>Whether this scene-entity row is the currently-selected entity (highlight).</summary>
    public bool Selected;

    /// <summary>Whether a click on the row body does anything (false for subheaders / members / info).</summary>
    public bool Interactive = true;

    // ---- payloads (set per kind) ----

    /// <summary>The section this header toggles (<see cref="PanelRowKind.SectionHeader"/>).</summary>
    public EditorPanelSection Section;

    /// <summary>The pipeline entry (<see cref="PanelRowKind.PipelineEntry"/>).</summary>
    public EditorPipelineEntry? Entry;

    /// <summary>The registrar owning <see cref="Entry"/> — the one <c>SetEnabled</c> targets.</summary>
    public EditorPipelineRegistrar? Registrar;

    /// <summary>The scene entity (<see cref="PanelRowKind.SceneEntity"/>).</summary>
    public Entity Entity;

    /// <summary>The component's full type name (<see cref="PanelRowKind.InspectorComponent"/>) — the
    /// key into <see cref="EditorPanelStateComponent.ExpandedInspectorComponents"/>.</summary>
    public string? ComponentKey;

    /// <summary>The catalog entry (<see cref="PanelRowKind.SceneCatalogEntry"/>) a click switches to.</summary>
    public MonoDreams.LevelEditor.Composition.SceneCatalogEntry? CatalogEntry;

    /// <summary>Whether this row carries the unsaved-changes marker (the current scene while dirty) —
    /// the label is prefixed with a <c>●</c> and rendered <see cref="EditorTheme"/>'s Warning color.</summary>
    public bool DirtyMarker;

    // ---- Inspector member payloads (PF-A §3) ----

    /// <summary>The member name (<see cref="PanelRowKind.InspectorMember"/>) — the "Name:" part rendered
    /// muted; the key a <c>MemberEditCommand</c> targets alongside <see cref="ComponentKey"/>.</summary>
    public string? MemberName;

    /// <summary>The member's formatted value string (<see cref="PanelRowKind.InspectorMember"/>) — the
    /// type-colored part, or the seed of the inline edit field.</summary>
    public string? MemberValue;

    /// <summary>Whether the member value can be edited inline / toggled / cycled (a writable member of a
    /// supported kind). A read-only member renders muted and ignores clicks.</summary>
    public bool MemberEditable;

    /// <summary>The member's declared CLR type (<see cref="PanelRowKind.InspectorMember"/>) — the panel
    /// reads it to pick the edit interaction (field / bool toggle / enum cycle) and re-read the color.</summary>
    public Type? MemberType;

    /// <summary>The DevTools syntax-color role for the member value (numbers/strings/bools/enums/muted),
    /// rendered for read-only AND editable member rows.</summary>
    public MonoDreams.LevelEditor.Inspector.InspectorValueRole ValueRole;

    /// <summary>The delete affordance for a component row (<see cref="PanelRowKind.InspectorComponent"/>):
    /// whether it shows a <c>×</c>, and whether the <c>×</c> deletes or refuses (Transform is guarded).</summary>
    public InspectorDeleteAffordance DeleteAffordance;
}

/// <summary>The per-component delete affordance in the Inspector (PF-A §3): structural components show
/// no <c>×</c>; <c>TransformComponent</c> shows one but refuses (status hint); everything else deletes.</summary>
public enum InspectorDeleteAffordance
{
    /// <summary>No <c>×</c> — a structural component (<c>SceneEntityId</c> / <c>ChildOf</c> / prefab
    /// markers) is never designer-deletable.</summary>
    None,

    /// <summary>A <c>×</c> is shown but clicking it refuses with a status hint —
    /// <c>TransformComponent</c> (an entity must keep its single spatial component).</summary>
    Guarded,

    /// <summary>A <c>×</c> that deletes the component through an undoable <c>RemoveComponentCommand</c>.</summary>
    Deletable,
}

/// <summary>
/// The pure assembler of the editor right strip: given the two pipeline registrars (Systems), the
/// pre-built scene tree (Scene), the Inspector's component data for the current selection, and the
/// <see cref="EditorPanelStateComponent"/> collapse/expand state, it produces the flat ordered list
/// of <see cref="PanelRow"/>s the panel renders as one scroll. No world access, no GraphicsDevice —
/// unit-testable directly (the crux of the section-collapse / group-collapse / tree / inspector
/// tests).
/// </summary>
public static class EditorPanelModel
{
    public const string SystemsTitle = "SYSTEMS";
    public const string EntitiesTitle = "ENTITIES";
    public const string InspectorTitle = "INSPECTOR";
    private const string UpdateSub = "UPDATE";
    private const string DrawSub = "DRAW";

    /// <summary>The Scenes-list header row shown in the Scenes tab.</summary>
    public const string ScenesTitle = "Scenes";

    /// <summary>
    /// Builds the flat row list for the ACTIVE left-strip tab (<paramref name="activeTab"/>):
    /// <c>Entities</c> → the entity tree (ALONE — the Inspector is a separate panel now);
    /// <c>Systems</c> → the pipeline listing; <c>Scenes</c> → project info + the scene catalog
    /// (<paramref name="project"/>/<paramref name="sceneCatalog"/>). Only the active tab's rows are
    /// produced — the tab bar itself is rendered separately (persistent tab entities), not as pooled
    /// rows. <paramref name="update"/>/<paramref name="draw"/> may be null (pipelines not yet bound →
    /// the Systems body shows nothing). <paramref name="sceneNodes"/> is the pre-order tree from
    /// <see cref="EntitySceneTree.Build"/>; <paramref name="sceneLabel"/> names an entity;
    /// <paramref name="selected"/> is the currently-selected entity (highlighted in the tree).
    /// </summary>
    public static List<PanelRow> Build(
        EditorPanelStateComponent state,
        EditorPanelTab activeTab,
        EditorPipelineRegistrar? update,
        EditorPipelineRegistrar? draw,
        IReadOnlyList<EntitySceneTree.Node> sceneNodes,
        Func<Entity, string> sceneLabel,
        Entity selected,
        EditorProjectInfo project = default,
        IReadOnlyList<MonoDreams.LevelEditor.Composition.SceneCatalogEntry>? sceneCatalog = null,
        bool isDirty = false)
    {
        var rows = new List<PanelRow>();

        switch (activeTab)
        {
            case EditorPanelTab.Systems:
                rows.Add(SectionHeader(EditorPanelSection.Systems, SystemsTitle, !state.SystemsCollapsed));
                if (!state.SystemsCollapsed)
                {
                    AppendPipeline(rows, state, UpdateSub, update);
                    AppendPipeline(rows, state, DrawSub, draw);
                }
                break;

            case EditorPanelTab.Scenes:
                AppendProject(rows, project, sceneCatalog, isDirty);
                break;

            case EditorPanelTab.Entities:
            default:
                rows.Add(SectionHeader(EditorPanelSection.Entities, EntitiesTitle, !state.EntitiesCollapsed));
                if (!state.EntitiesCollapsed)
                    AppendScene(rows, state, sceneNodes, sceneLabel, selected);
                break;
        }

        return rows;
    }

    /// <summary>
    /// Builds the flat row list for the dedicated <b>editable Inspector panel</b> (the right region —
    /// UX2-B, upgraded by PF-A to Chrome DevTools' element/styles model): a <b>filter field</b> row at
    /// the top (the DevTools search), the selection's title, its attached component rows (each with a
    /// delete <c>×</c> affordance + expandable to type-colored member values), and a trailing
    /// <b>+ Add component</b> row. Null <paramref name="inspectorComponents"/> → "(no selection)".
    ///
    /// <para><paramref name="filter"/> (the <see cref="EditorPanelStateComponent.InspectorFilter"/>
    /// text) narrows the rows case-insensitively: a component row survives when its type name OR any of
    /// its members (name or value) matches; a name-matched component shows all its members, else only
    /// its matching members. <paramref name="deleteAffordance"/> classifies each component's <c>×</c>
    /// (structural = none, Transform = guarded, else deletable) — supplied by the panel, which has the
    /// registry. <paramref name="showAddRow"/> appends the "+ Add component" row (off in a no-registry
    /// unit test). Pure; unit-testable with hand-fed inputs, no world.</para>
    /// </summary>
    public static List<PanelRow> BuildInspector(
        EditorPanelStateComponent state,
        IReadOnlyList<ComponentInspector.ComponentInfo>? inspectorComponents,
        string? inspectorTitle,
        string? filter = null,
        Func<ComponentInspector.ComponentInfo, InspectorDeleteAffordance>? deleteAffordance = null,
        bool showAddRow = false)
    {
        var rows = new List<PanelRow>();
        AppendInspector(rows, state, inspectorComponents, inspectorTitle, filter ?? string.Empty,
            deleteAffordance, showAddRow);
        return rows;
    }

    /// <summary>The tab (<see cref="EditorPanelTab"/>) that HOSTS a given collapsible section — a
    /// section op issued from the headless channel activates this tab first (existing <c>panel:*</c>
    /// ops keep working against whichever tab hosts their section).</summary>
    public static EditorPanelTab HostTab(EditorPanelSection section) => section switch
    {
        EditorPanelSection.Systems => EditorPanelTab.Systems,
        _ => EditorPanelTab.Entities, // the tree lives in the Entities tab
    };

    /// <summary>Middle-truncates a path to <paramref name="maxChars"/> with a central ellipsis
    /// ("/very/long/…/scene") so the head (drive/root) and tail (file) stay legible in the Project
    /// tab. Pure — the panel supplies the char budget from its body width.</summary>
    public static string MiddleEllipsis(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (maxChars <= 1 || text!.Length <= maxChars) return text ?? string.Empty;
        const string ellipsis = "…";
        var keep = maxChars - ellipsis.Length;
        if (keep <= 0) return ellipsis;
        var head = (keep + 1) / 2;
        var tail = keep - head;
        return text.Substring(0, head) + ellipsis + text.Substring(text.Length - tail);
    }

    private static void AppendProject(List<PanelRow> rows, EditorProjectInfo project,
        IReadOnlyList<MonoDreams.LevelEditor.Composition.SceneCatalogEntry>? sceneCatalog, bool isDirty)
    {
        rows.Add(Info($"Project: {(string.IsNullOrEmpty(project.ProjectRoot) ? "(unresolved)" : project.ProjectRoot)}", depth: 1));
        rows.Add(Info($"Levels: {(string.IsNullOrEmpty(project.LevelsDir) ? "-" : project.LevelsDir)}", depth: 1));

        // The Scenes list: each catalog entry as a selectable row (current = highlighted; dirty
        // current = a Warning ● prefix). The switch itself flows through the dirty-gated select
        // handler when the row is clicked (the panel wires that callback).
        rows.Add(Info(ScenesTitle, depth: 1));
        if (sceneCatalog == null || sceneCatalog.Count == 0)
        {
            rows.Add(Info("(no scenes)", depth: 2));
            return;
        }
        foreach (var entry in sceneCatalog)
        {
            var dirty = entry.IsCurrent && isDirty;
            rows.Add(new PanelRow
            {
                Kind = PanelRowKind.SceneCatalogEntry,
                Label = (dirty ? "● " : string.Empty) + entry.Label,
                Depth = 2,
                Selected = entry.IsCurrent,
                DirtyMarker = dirty,
                CatalogEntry = entry,
            });
        }
    }

    private static PanelRow SectionHeader(EditorPanelSection section, string title, bool expanded) => new()
    {
        Kind = PanelRowKind.SectionHeader,
        Label = title,
        Section = section,
        Collapsible = true,
        Expanded = expanded,
        Depth = 0,
    };

    private static void AppendPipeline(List<PanelRow> rows, EditorPanelStateComponent state,
        string subheader, EditorPipelineRegistrar? registrar)
    {
        if (registrar == null) return;
        rows.Add(new PanelRow
        {
            Kind = PanelRowKind.PipelineSubheader,
            Label = subheader,
            Depth = 1,
            Interactive = false,
        });

        foreach (var entry in registrar.Entries) // flattened pre-order: group precedes its children
        {
            if (AncestorGroupCollapsed(entry, state)) continue;
            var st = entry.EnabledState;
            rows.Add(new PanelRow
            {
                Kind = PanelRowKind.PipelineEntry,
                Label = SystemsPanelLayout.LineLabel(entry),
                Depth = 1 + entry.Depth,
                HasCheckbox = true,
                CheckboxState = st,
                ShowMinusBar = entry.IsGroup && st == PipelineEnabledState.Mixed,
                Collapsible = entry.IsGroup,
                Expanded = !state.CollapsedGroups.Contains(entry.Name),
                Entry = entry,
                Registrar = registrar,
            });
        }
    }

    /// <summary>Whether any ancestor group of <paramref name="entry"/> is collapsed (so the entry's
    /// row is hidden).</summary>
    private static bool AncestorGroupCollapsed(EditorPipelineEntry entry, EditorPanelStateComponent state)
    {
        for (var p = entry.Parent; p != null; p = p.Parent)
            if (state.CollapsedGroups.Contains(p.Name))
                return true;
        return false;
    }

    private static void AppendScene(List<PanelRow> rows, EditorPanelStateComponent state,
        IReadOnlyList<EntitySceneTree.Node> nodes, Func<Entity, string> label, Entity selected)
    {
        if (nodes == null || nodes.Count == 0)
        {
            rows.Add(Info("(no scene entities)", depth: 1));
            return;
        }

        // Flatten the pre-order tree honoring per-entity subtree collapse: when a collapsed node is
        // emitted, skip every following node deeper than it until the depth returns to its level.
        int? hideBelowDepth = null;
        foreach (var node in nodes)
        {
            if (hideBelowDepth is { } hd)
            {
                if (node.Depth > hd) continue; // inside a collapsed subtree
                hideBelowDepth = null;         // back at/above the collapsed level
            }

            var collapsed = state.CollapsedTreeEntities.Contains(node.Entity);
            rows.Add(new PanelRow
            {
                Kind = PanelRowKind.SceneEntity,
                Label = label(node.Entity),
                Depth = 1 + node.Depth,
                Collapsible = node.HasChildren,
                Expanded = !collapsed,
                Selected = node.Entity == selected,
                Entity = node.Entity,
            });

            if (node.HasChildren && collapsed)
                hideBelowDepth = node.Depth;
        }
    }

    private static void AppendInspector(List<PanelRow> rows, EditorPanelStateComponent state,
        IReadOnlyList<ComponentInspector.ComponentInfo>? components, string? title, string filter,
        Func<ComponentInspector.ComponentInfo, InspectorDeleteAffordance>? deleteAffordance, bool showAddRow)
    {
        if (components == null)
        {
            rows.Add(Info("(no selection)", depth: 1));
            return;
        }

        // The DevTools search field — always the first body row when there is a selection.
        rows.Add(new PanelRow
        {
            Kind = PanelRowKind.InspectorFilter,
            Label = filter,
            Depth = 1,
        });

        if (!string.IsNullOrEmpty(title))
            rows.Add(Info(title!, depth: 1));

        if (components.Count == 0)
            rows.Add(Info("(no components)", depth: 1));

        foreach (var comp in components)
        {
            var componentMatches = filter.Length == 0 ||
                comp.TypeName.Contains(filter, StringComparison.OrdinalIgnoreCase);
            var anyMemberMatches = filter.Length == 0 || AnyMemberMatches(comp, filter);
            if (!componentMatches && !anyMemberMatches) continue; // filtered out entirely

            var expanded = state.ExpandedInspectorComponents.Contains(comp.FullTypeName);
            rows.Add(new PanelRow
            {
                Kind = PanelRowKind.InspectorComponent,
                Label = comp.TypeName,
                Depth = 1,
                Collapsible = comp.HasMembers,
                Expanded = expanded,
                ComponentKey = comp.FullTypeName,
                DeleteAffordance = deleteAffordance?.Invoke(comp) ?? InspectorDeleteAffordance.None,
            });
            if (!expanded) continue;

            foreach (var m in comp.Members)
            {
                // When the component NAME matched, show every member; otherwise only matching members.
                if (filter.Length != 0 && !componentMatches && !MemberMatches(m, filter)) continue;
                rows.Add(new PanelRow
                {
                    Kind = PanelRowKind.InspectorMember,
                    Label = m.Name + ":",
                    Depth = 2,
                    ComponentKey = comp.FullTypeName,
                    MemberName = m.Name,
                    MemberValue = m.Value,
                    MemberEditable = m.Editable,
                    MemberType = m.MemberType,
                    ValueRole = m.Role,
                });
            }
        }

        if (showAddRow)
            rows.Add(new PanelRow
            {
                Kind = PanelRowKind.InspectorAddComponent,
                Label = "+ Add component",
                Depth = 1,
            });
    }

    private static bool AnyMemberMatches(ComponentInspector.ComponentInfo comp, string filter)
    {
        foreach (var m in comp.Members)
            if (MemberMatches(m, filter)) return true;
        return false;
    }

    private static bool MemberMatches(ComponentInspector.Member m, string filter) =>
        m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        m.Value.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private static PanelRow Info(string text, int depth) => new()
    {
        Kind = PanelRowKind.Info,
        Label = text,
        Depth = depth,
        Interactive = false,
    };
}
