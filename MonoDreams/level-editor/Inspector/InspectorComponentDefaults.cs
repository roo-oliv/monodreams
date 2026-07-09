#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DefaultEcs;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Component.Physics;
using MonoDreams.LevelEditor.Proxy;

namespace MonoDreams.LevelEditor.Inspector;

/// <summary>
/// The per-type <b>default-initializer table</b> for "+ Add component" (PF-A §3): builds the boxed
/// value a fresh component is added with. Some components' zero value is unusable, so this hands them a
/// sensible footprint/default:
/// <list type="bullet">
///   <item><c>BoxColliderComponent</c> / <c>ConvexColliderComponent</c> — the standard footprint
///   (full sprite width × the bottom quarter, feet-anchored — <see cref="ColliderDefaults"/>), passive,
///   so a placed static collider blocks without drifting. A sprite-less entity gets the fallback.</item>
///   <item><c>RigidBodyComponent</c> / <c>VelocityComponent</c> / <c>CameraFollowTargetComponent</c> /
///   <c>EntityInfoComponent</c> — their construction defaults (mass 1, gravity active, …).</item>
///   <item><c>SpriteInfoComponent</c> — a Main-target sprite (excluded from the UI candidate list, but
///   handled here so a headless <c>inspector:add</c> op is well-defined; the paired DrawComponent is
///   added by <c>AddComponentCommand</c>).</item>
/// </list>
/// Any other registered type (the game's own components) is built by <see cref="Activator.CreateInstance"/>
/// when it has a public parameterless ctor, else <see cref="RuntimeHelpers.GetUninitializedObject"/>
/// (an all-default instance the designer then edits) — so the table needs no per-game entry.
/// </summary>
public static class InspectorComponentDefaults
{
    private static readonly HashSet<int> AllLayers = new() { -1 };

    /// <summary>Builds the default boxed component instance to add for <paramref name="type"/> on
    /// <paramref name="entity"/> (the colliders read the entity's sprite footprint).</summary>
    public static object Build(Type type, Entity entity)
    {
        if (type == typeof(BoxColliderComponent))
        {
            var bounds = entity.IsAlive && entity.Has<SpriteInfoComponent>()
                ? ColliderDefaults.FootprintBounds(entity.Get<SpriteInfoComponent>())
                : ColliderDefaults.FallbackFootprint;
            return new BoxColliderComponent(bounds, new HashSet<int>(AllLayers), ColliderDefaults.FootprintPassive, true);
        }
        if (type == typeof(ConvexColliderComponent))
        {
            var hexagon = entity.IsAlive && entity.Has<SpriteInfoComponent>()
                ? ColliderDefaults.FootprintHexagon(entity.Get<SpriteInfoComponent>())
                : ColliderDefaults.FallbackHexagon();
            return new ConvexColliderComponent(hexagon, new HashSet<int>(AllLayers), ColliderDefaults.FootprintPassive, true, false);
        }
        if (type == typeof(RigidBodyComponent)) return new RigidBodyComponent();
        if (type == typeof(VelocityComponent)) return new VelocityComponent();
        if (type == typeof(CameraFollowTargetComponent)) return new CameraFollowTargetComponent();
        if (type == typeof(EntityInfoComponent)) return new EntityInfoComponent("Entity");
        if (type == typeof(TransformComponent)) return new TransformComponent(Vector2.Zero);
        if (type == typeof(SpriteInfoComponent)) return new SpriteInfoComponent { Target = RenderTargetID.Main };

        // A game component (or any other registered type): its parameterless ctor, else an all-default
        // (uninitialized) instance the designer then edits in the Inspector.
        try
        {
            return Activator.CreateInstance(type) ?? RuntimeHelpers.GetUninitializedObject(type);
        }
        catch
        {
            return RuntimeHelpers.GetUninitializedObject(type);
        }
    }
}
