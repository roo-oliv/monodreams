#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;
using MonoDreams.LevelEditor.Component;
using MonoDreams.LevelEditor.Composition;
using MonoDreams.LevelEditor.Inspector;

namespace MonoDreams.LevelEditor.UI;

/// <summary>The collapsible sections the editor's right strip can stack — now distributed across the
/// Scene / Systems / Project tabs (<c>EditorRightTab</c>). Scene tab = <see cref="Scene"/> +
/// <see cref="Inspector"/>; Systems tab = <see cref="Systems"/>; the Project tab shows info rows
/// (no collapsible section).</summary>
public enum EditorPanelSection
{
    /// <summary>The pipeline listing (every registrar entry of both pipelines) — the Wave-8 systems
    /// panel, now a section with per-group collapse. Hosted by the Systems tab.</summary>
    Systems,

    /// <summary>The world's entities as a parent/child tree — selectable, two-way with the
    /// viewport selection. Hosted by the Scene tab.</summary>
    Scene,

    /// <summary>The selected entity's attached components + (on demand) their member values. Hosted
    /// by the Scene tab.</summary>
    Inspector,
}

/// <summary>The project-tab info the model renders (root path, levels dir, current scene id). Null
/// <see cref="ProjectRoot"/> = the project is unresolved (Save disabled).</summary>
public readonly record struct EditorProjectInfo(string? ProjectRoot, string? LevelsDir, string? SceneId);

/// <summary>What a single panel row represents (drives its visuals + click behavior).</summary>
public enum PanelRowKind
{
    /// <summary>A section header (Systems / Scene / Inspector) — clicking collapses/expands the
    /// whole section body.</summary>
    SectionHeader,

    /// <summary>The "UPDATE" / "DRAW" separator inside the Systems section (non-interactive).</summary>
    PipelineSubheader,

    /// <summary>A pipeline entry (leaf or group) — checkbox toggles enabled; a group's arrow
    /// collapses its children.</summary>
    PipelineEntry,

    /// <summary>A scene-tree entity — clicking the label selects it; the arrow collapses its
    /// subtree.</summary>
    SceneEntity,

    /// <summary>An Inspector component-type row — clicking expands/collapses its member values.</summary>
    InspectorComponent,

    /// <summary>An Inspector member <c>name: value</c> row (read-only).</summary>
    InspectorMember,

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
    public const string SceneTitle = "SCENE";
    public const string InspectorTitle = "INSPECTOR";
    private const string UpdateSub = "UPDATE";
    private const string DrawSub = "DRAW";

    /// <summary>The muted placeholder the Project tab shows until the Scenes list lands (UX-C).</summary>
    public const string ScenesListPlaceholder = "(scenes list lands in UX-C)";

    /// <summary>
    /// Builds the flat row list for the ACTIVE right-strip tab (<paramref name="activeTab"/>):
    /// <c>Scene</c> → the Scene tree + the Inspector sections; <c>Systems</c> → the pipeline listing;
    /// <c>Project</c> → project info rows (<paramref name="project"/>). Only the active tab's rows
    /// are produced — the tab bar itself is rendered separately (persistent tab entities), not as
    /// pooled rows. <paramref name="update"/>/<paramref name="draw"/> may be null (pipelines not yet
    /// bound → the Systems body shows nothing). <paramref name="sceneNodes"/> is the pre-order tree
    /// from <see cref="EntitySceneTree.Build"/>; <paramref name="sceneLabel"/> names an entity;
    /// <paramref name="selected"/> is the currently-selected entity (highlighted + the Inspector
    /// binds to it). <paramref name="inspectorComponents"/> is null when nothing is selected (the
    /// Inspector shows "(no selection)").
    /// </summary>
    public static List<PanelRow> Build(
        EditorPanelStateComponent state,
        EditorRightTab activeTab,
        EditorPipelineRegistrar? update,
        EditorPipelineRegistrar? draw,
        IReadOnlyList<EntitySceneTree.Node> sceneNodes,
        Func<Entity, string> sceneLabel,
        Entity selected,
        IReadOnlyList<ComponentInspector.ComponentInfo>? inspectorComponents,
        string? inspectorTitle,
        EditorProjectInfo project = default)
    {
        var rows = new List<PanelRow>();

        switch (activeTab)
        {
            case EditorRightTab.Systems:
                rows.Add(SectionHeader(EditorPanelSection.Systems, SystemsTitle, !state.SystemsCollapsed));
                if (!state.SystemsCollapsed)
                {
                    AppendPipeline(rows, state, UpdateSub, update);
                    AppendPipeline(rows, state, DrawSub, draw);
                }
                break;

            case EditorRightTab.Project:
                AppendProject(rows, project);
                break;

            case EditorRightTab.Scene:
            default:
                rows.Add(SectionHeader(EditorPanelSection.Scene, SceneTitle, !state.SceneCollapsed));
                if (!state.SceneCollapsed)
                    AppendScene(rows, state, sceneNodes, sceneLabel, selected);

                rows.Add(SectionHeader(EditorPanelSection.Inspector, InspectorTitle, !state.InspectorCollapsed));
                if (!state.InspectorCollapsed)
                    AppendInspector(rows, state, inspectorComponents, inspectorTitle);
                break;
        }

        return rows;
    }

    /// <summary>The tab (<see cref="EditorRightTab"/>) that HOSTS a given collapsible section — a
    /// section op issued from the headless channel activates this tab first (UX-B §2.2: existing
    /// <c>panel:*</c> ops keep working against whichever tab hosts their section).</summary>
    public static EditorRightTab HostTab(EditorPanelSection section) => section switch
    {
        EditorPanelSection.Systems => EditorRightTab.Systems,
        _ => EditorRightTab.Scene, // Scene + Inspector both live in the Scene tab
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

    private static void AppendProject(List<PanelRow> rows, EditorProjectInfo project)
    {
        rows.Add(Info($"Project: {(string.IsNullOrEmpty(project.ProjectRoot) ? "(unresolved)" : project.ProjectRoot)}", depth: 1));
        rows.Add(Info($"Levels: {(string.IsNullOrEmpty(project.LevelsDir) ? "-" : project.LevelsDir)}", depth: 1));
        rows.Add(Info($"Scene: {(string.IsNullOrEmpty(project.SceneId) ? "-" : project.SceneId)}", depth: 1));
        rows.Add(Info(ScenesListPlaceholder, depth: 1));
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
        IReadOnlyList<ComponentInspector.ComponentInfo>? components, string? title)
    {
        if (components == null)
        {
            rows.Add(Info("(no selection)", depth: 1));
            return;
        }

        if (!string.IsNullOrEmpty(title))
            rows.Add(Info(title!, depth: 1));

        if (components.Count == 0)
        {
            rows.Add(Info("(no components)", depth: 1));
            return;
        }

        foreach (var comp in components)
        {
            var expanded = state.ExpandedInspectorComponents.Contains(comp.FullTypeName);
            rows.Add(new PanelRow
            {
                Kind = PanelRowKind.InspectorComponent,
                Label = comp.TypeName,
                Depth = 1,
                Collapsible = comp.HasMembers,
                Expanded = expanded,
                ComponentKey = comp.FullTypeName,
            });
            if (!expanded) continue;
            foreach (var m in comp.Members)
                rows.Add(new PanelRow
                {
                    Kind = PanelRowKind.InspectorMember,
                    Label = $"{m.Name}: {m.Value}",
                    Depth = 2,
                    Interactive = false,
                });
        }
    }

    private static PanelRow Info(string text, int depth) => new()
    {
        Kind = PanelRowKind.Info,
        Label = text,
        Depth = depth,
        Interactive = false,
    };
}
