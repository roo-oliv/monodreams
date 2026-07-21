#nullable enable
using System;
using System.Collections.Generic;
using MonoDreams.Component.Draw;
using MonoDreams.LevelEditor.Component;

namespace MonoDreams.LevelEditor.Inspector;

/// <summary>
/// Derives the "+ Add component" candidate list (PF-A §3) — the DevTools command-palette contents —
/// from the component-serializer registry: the honest "what can this scene persist" set (engine + game
/// registered types) MINUS the types already on the entity MINUS structural/never-addable ones. Pure +
/// world-free (fed the registry's <c>(key, type)</c> pairs, the present-type set, and a
/// structural predicate), so the derivation is unit-testable directly.
/// </summary>
public static class InspectorAddCandidates
{
    /// <summary>
    /// Types that are registered (they persist) but are <b>never</b> offered by the generic Add menu —
    /// they are authored through a dedicated tool that gives them a usable value, and a zero/default
    /// instance would be useless (or worse) in the scene:
    /// <list type="bullet">
    ///   <item><c>SpriteInfoComponent</c> — a sprite needs an ASSET; it is placed via the palette (which
    ///   assigns the AssetKey + the paired DrawComponent). An asset-less SpriteInfo renders blank.
    ///   (The <c>AddComponentCommand</c> still honors the SpriteInfo⇒DrawComponent pairing if a headless
    ///   op force-adds it — the exclusion is a UI-candidate policy, not a command guardrail.)</item>
    ///   <item><c>BoundaryComponent</c> — a boundary is a polyline laid with the boundary tool; an empty
    ///   one bakes nothing.</item>
    ///   <item><c>BoxColliderComponent</c> / <c>ConvexColliderComponent</c> — a shape lives on its OWN
    ///   collider ENTITY now (colliders-as-entities); a collider is authored via <b>Add Collider ▸ Box /
    ///   Polygon</b> (the entity/Entity-header menu + the toolbar), which creates a footprint-shaped CHILD
    ///   collider entity — not a component the Inspector force-adds to an arbitrary entity.</item>
    ///   <item><c>CameraComponent</c> — a scene has exactly ONE camera entity (CM one-camera rule); the
    ///   reader ensures it exists, so it is never force-added to an arbitrary entity (a second camera would
    ///   be refused by the writer anyway).</item>
    /// </list>
    /// </summary>
    public static readonly IReadOnlySet<Type> NeverAddable = new HashSet<Type>
    {
        typeof(SpriteInfoComponent),
        typeof(BoundaryComponent),
        typeof(MonoDreams.Component.Collision.BoxColliderComponent),
        typeof(MonoDreams.Component.Collision.ConvexColliderComponent),
        typeof(MonoDreams.Component.CameraComponent),
    };

    /// <summary>One addable component: the registry <see cref="Key"/> (the id an add op / menu path
    /// carries), the short <see cref="DisplayName"/> (the menu label), and the CLR <see cref="Type"/>.</summary>
    public readonly record struct Candidate(string Key, string DisplayName, Type Type);

    /// <summary>
    /// The candidates for an entity: every <paramref name="registered"/> <c>(key, type)</c> whose type
    /// is not already <paramref name="present"/> on the entity, not <paramref name="isStructural"/>
    /// (the registry-marked structural fields — <c>ChildOfComponent</c>, <c>SceneEntityIdComponent</c>,
    /// prefab markers when they land), and not in <see cref="NeverAddable"/>. Sorted by key so the popup
    /// is deterministic.
    /// </summary>
    public static List<Candidate> Derive(
        IEnumerable<(string Key, Type Type)> registered,
        ISet<Type> present,
        Func<Type, bool> isStructural)
    {
        var list = new List<Candidate>();
        foreach (var (key, type) in registered)
        {
            if (present.Contains(type)) continue;
            if (isStructural(type)) continue;
            if (NeverAddable.Contains(type)) continue;
            list.Add(new Candidate(key, type.Name, type));
        }
        list.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        return list;
    }
}
