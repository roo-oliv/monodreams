#nullable enable
using System;
using System.Collections.Generic;
using DefaultEcs;

namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// The right-strip editor panel's collapse / expand UI state — pure data on a single editor-owned
/// entity (ECS purity: the <b>state</b> lives in a component, the <b>behavior</b> in
/// <c>EditorPanelSystem</c>). The panel is a vertical stack of collapsible <b>sections</b>
/// (Systems, Scene, Inspector), each of whose body can collapse; within the Systems section a
/// pipeline <b>group</b> row can collapse its children; within the Scene section an entity row can
/// collapse its child subtree; and within the Inspector a component row can expand to show its
/// member values.
///
/// <para>All state is <b>in-session</b> (no persistence — the transport Restart re-tags a fresh
/// state entity, and stale <see cref="Entity"/> keys simply never match again). Defaults are
/// "everything visible": no section collapsed, no group collapsed, no subtree collapsed, and
/// component member rows hidden until explicitly expanded (the Inspector shows the component list
/// by default, member values on demand — the "see the state when I want" ask).</para>
/// </summary>
public sealed class EditorPanelStateComponent
{
    /// <summary>Whether the "Systems" section body (the pipeline listing) is collapsed to its header.</summary>
    public bool SystemsCollapsed;

    /// <summary>Whether the "Scene" section body (the entity tree) is collapsed to its header.</summary>
    public bool SceneCollapsed;

    /// <summary>Whether the "Inspector" section body (the selection's components) is collapsed.</summary>
    public bool InspectorCollapsed;

    /// <summary>The full names of the pipeline <b>groups</b> whose children are hidden (absent =
    /// expanded). Keyed by <c>EditorPipelineEntry.Name</c> (e.g. <c>"editor.toolbar"</c>).</summary>
    public readonly HashSet<string> CollapsedGroups = new(StringComparer.Ordinal);

    /// <summary>The scene-tree entities whose child subtree is hidden (absent = expanded). Keyed by
    /// the live <see cref="Entity"/> (equality is stable; a disposed entity's key just stops
    /// matching).</summary>
    public readonly HashSet<Entity> CollapsedTreeEntities = new();

    /// <summary>The Inspector component rows whose member values are shown (absent = collapsed).
    /// Keyed by the component's full type name; reset when the bound selection changes so a stale
    /// component key from a previous selection never leaks.</summary>
    public readonly HashSet<string> ExpandedInspectorComponents = new(StringComparer.Ordinal);

    /// <summary>The entity the Inspector's <see cref="ExpandedInspectorComponents"/> set belongs to
    /// — when the selection changes, the system clears the expand set and updates this. Default =
    /// dead entity.</summary>
    public Entity InspectorEntity;
}
