#nullable enable
using System;
using System.Collections.Generic;

namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// The right-column editor panel's collapse/expand state — <b>pure data</b> (ECS purity: the
/// state lives here, the behaviour lives in <c>SystemsPanelSystem</c>). It is held on a dedicated
/// editor-infrastructure entity the panel owns, so it survives a transport Restart and is
/// inspectable/testable without touching the panel's private fields.
///
/// <para>The right strip is a vertical stack of three collapsible <see cref="PanelSection"/>s —
/// SYSTEMS (the registrar tree), SCENE (the entity tree), INSPECTOR (the selected entity's
/// components + member values). Each section header toggles the section's whole body via
/// <see cref="SystemsCollapsed"/> / <see cref="SceneCollapsed"/> / <see cref="InspectorCollapsed"/>.
/// Within SYSTEMS a group row's children collapse via <see cref="CollapsedGroups"/> (keyed by the
/// registrar entry's full name); within INSPECTOR a component's member rows expand via
/// <see cref="ExpandedComponents"/> (keyed by the component <see cref="Type.FullName"/>).</para>
/// </summary>
public sealed class EditorPanelStateComponent
{
    /// <summary>Whether the SYSTEMS section (the registrar tree) is collapsed to just its header.</summary>
    public bool SystemsCollapsed;

    /// <summary>Whether the SCENE section (the entity tree) is collapsed to just its header.</summary>
    public bool SceneCollapsed;

    /// <summary>Whether the INSPECTOR section (the selected entity's components) is collapsed.</summary>
    public bool InspectorCollapsed;

    /// <summary>Full names of the registrar groups whose children are currently collapsed (hidden)
    /// inside the SYSTEMS section. Absent = expanded (the default). Ordinal keys, matching the
    /// registrar's <c>EditorPipelineEntry.Name</c>.</summary>
    public readonly HashSet<string> CollapsedGroups = new(StringComparer.Ordinal);

    /// <summary>Full type names of the components whose member rows are currently expanded (shown)
    /// inside the INSPECTOR section. Absent = collapsed (the default — a component shows its name
    /// only until the designer expands it). Ordinal keys, matching <see cref="Type.FullName"/>.</summary>
    public readonly HashSet<string> ExpandedComponents = new(StringComparer.Ordinal);

    /// <summary>Toggles a top-level section's collapse flag.</summary>
    public void ToggleSection(PanelSection section)
    {
        switch (section)
        {
            case PanelSection.Systems: SystemsCollapsed = !SystemsCollapsed; break;
            case PanelSection.Scene: SceneCollapsed = !SceneCollapsed; break;
            case PanelSection.Inspector: InspectorCollapsed = !InspectorCollapsed; break;
        }
    }

    /// <summary>Whether the given section is collapsed.</summary>
    public bool IsCollapsed(PanelSection section) => section switch
    {
        PanelSection.Systems => SystemsCollapsed,
        PanelSection.Scene => SceneCollapsed,
        PanelSection.Inspector => InspectorCollapsed,
        _ => false,
    };

    /// <summary>Toggles whether a registrar group's children are hidden (SYSTEMS section).</summary>
    public void ToggleGroup(string fullName)
    {
        if (!CollapsedGroups.Add(fullName)) CollapsedGroups.Remove(fullName);
    }

    /// <summary>Whether a registrar group (by full name) is collapsed (its children hidden).</summary>
    public bool IsGroupCollapsed(string fullName) => CollapsedGroups.Contains(fullName);

    /// <summary>Toggles whether a component's member rows are shown (INSPECTOR section).</summary>
    public void ToggleComponent(string typeFullName)
    {
        if (!ExpandedComponents.Add(typeFullName)) ExpandedComponents.Remove(typeFullName);
    }

    /// <summary>Whether a component (by full type name) has its member rows expanded.</summary>
    public bool IsComponentExpanded(string typeFullName) => ExpandedComponents.Contains(typeFullName);
}

/// <summary>The three collapsible sections of the editor's right-column panel, top to bottom.</summary>
public enum PanelSection
{
    /// <summary>The ECS pipeline tree (both registrars, groups, tri-state checkboxes).</summary>
    Systems,

    /// <summary>The world's entity tree (parent/child via <c>ChildOfComponent</c>), selectable.</summary>
    Scene,

    /// <summary>The selected entity's component list + read-only member values.</summary>
    Inspector,
}
