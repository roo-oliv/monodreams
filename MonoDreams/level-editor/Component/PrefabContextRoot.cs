#nullable enable
using DefaultEcs;
using MonoDreams.Component;

namespace MonoDreams.LevelEditor.Component;

/// <summary>
/// Resolves the single ROOT of a prefab-context world (PF-F) — the one parentless
/// <see cref="SceneObjectComponent"/> entity — so every root-creating action inside a prefab tab
/// (palette place, trigger place, Add Empty Entity, …) can <b>auto-parent</b> the new entity under it
/// (<c>ChildOf</c>) instead of creating a SECOND root. A prefab must have exactly one root
/// (<c>PrefabWriter</c> refuses a multi-root world), so assembly-by-placement would otherwise be
/// un-savable; parenting keeps the assembly one connected tree.
///
/// <para>Pure query over the world (ECS purity — a static helper). Returns <c>default</c> when there is
/// not exactly one root (never guesses — the caller then skips auto-parenting), and can exclude the
/// just-created entity so a resolve mid-placement still finds the pre-existing prefab root.</para>
/// </summary>
public static class PrefabContextRoot
{
    /// <summary>The single parentless <see cref="SceneObjectComponent"/> root of <paramref name="world"/>,
    /// excluding <paramref name="exclude"/> (the just-created entity, so it does not count itself). Returns
    /// <c>default</c> unless there is EXACTLY one such root.</summary>
    public static Entity Resolve(World world, Entity exclude = default)
    {
        Entity found = default;
        var count = 0;
        using var set = world.GetEntities().With<SceneObjectComponent>().AsSet();
        foreach (var e in set.GetEntities())
        {
            if (!e.IsAlive || e.Equals(exclude)) continue;
            // A root has no in-scope live ChildOf parent.
            if (e.Has<ChildOfComponent>() && e.Get<ChildOfComponent>().Parent.IsAlive) continue;
            found = e;
            count++;
        }
        return count == 1 ? found : default;
    }

    /// <summary>The active viewport context's kind from the shared shell state (the context stack writes
    /// the tab descriptors), or <see cref="ViewportContextKind.Scene"/> when unset.</summary>
    public static ViewportContextKind ActiveKind(EditorShellStateComponent? shell)
    {
        var tabs = shell?.ViewportTabs;
        var i = shell?.ActiveViewportTab ?? -1;
        return tabs != null && i >= 0 && i < tabs.Count ? tabs[i].Kind : ViewportContextKind.Scene;
    }

    /// <summary>The prefab root to auto-parent a new entity under — the single root of <paramref name="world"/>
    /// when the active context is a prefab, else <c>default</c> (no auto-parenting in a scene/game context).</summary>
    public static Entity ResolveIfPrefab(World world, EditorShellStateComponent? shell) =>
        ActiveKind(shell) == ViewportContextKind.Prefab ? Resolve(world) : default;
}
