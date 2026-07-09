#nullable enable
using DefaultEcs;
using MonoDreams.Component;

namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// The ONE shared predicate every editor mutation path consults to enforce the <b>instance-children
/// guardrail</b> (PF-D): the children of a linked prefab instance are <b>selectable but not editable</b>
/// in a scene — a gizmo drag, a modal G/S/R, an Inspector value/add/remove, or a delete on a prefab-owned
/// child is refused with a status hint ("open the prefab to edit its children, or Unpack"). The instance
/// ROOT itself stays fully editable (its Transform always; other components become overrides naturally via
/// the diff), and deleting the ROOT is legal (the whole instance goes). So "owned" means <b>a strict
/// <c>ChildOf</c> descendant of a <see cref="PrefabInstanceComponent"/> root</b> — never the root.
///
/// <para>Pure query over components (ECS purity — a static predicate, not a component): it walks the
/// <c>ChildOf</c> chain upward and returns true when it reaches a <see cref="PrefabInstanceComponent"/>
/// ancestor. The walk is bounded against a malformed cycle. Godot's editable-children is named terrain;
/// v1 refuses the edit and points at Unpack.</para>
/// </summary>
public static class PrefabGuards
{
    /// <summary>Guards the <c>ChildOf</c> ancestor walk against a malformed cycle.</summary>
    private const int MaxParentWalk = 64;

    /// <summary>
    /// Whether <paramref name="entity"/> is <b>owned by a prefab instance</b> — a strict <c>ChildOf</c>
    /// descendant of an entity carrying <see cref="PrefabInstanceComponent"/>. The instance root itself
    /// (which carries the marker) is NOT owned — it is the editable instance. A non-prefab entity is not
    /// owned. Dead entities are not owned.
    /// </summary>
    public static bool IsPrefabOwned(Entity entity)
    {
        if (!entity.IsAlive) return false;

        var current = entity;
        for (var depth = 0; depth < MaxParentWalk; depth++)
        {
            if (!current.Has<ChildOfComponent>()) return false; // reached a top-level entity, no prefab ancestor
            var parent = current.Get<ChildOfComponent>().Parent;
            if (!parent.IsAlive) return false;
            if (parent.Has<PrefabInstanceComponent>()) return true; // an ancestor is an instance root → owned
            current = parent;
        }

        return false; // bounded walk exhausted (malformed cycle) — treat as not owned
    }

    /// <summary>The ONE loud status hint every refused mutation on a prefab-owned child emits (there is no
    /// transient-toast channel in the editor — a <c>Logger.Warning</c> IS the status hint). Naming the
    /// escape hatches (open the prefab, or Unpack) keeps the refusal actionable.</summary>
    public static string Refusal(string action) =>
        $"[level-editor] {action} refused: this entity is a prefab instance's child — open the prefab to " +
        "edit its children, or Unpack the instance.";
}
