using System.Collections.Generic;
using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using MonoDreams.Component;
using MonoDreams.State;

namespace MonoDreams.UI;

/// <summary>
/// Drives every <see cref="PanelGroupComponent"/>: the active member sits at its own position and
/// every other member is PARKED — translated by <see cref="PanelGroupComponent.ParkOffset"/> so it
/// renders outside the viewport while staying fully alive: nothing is removed, the layout solver and
/// the sizing pass keep running over it, and its widget state survives untouched. Switching back
/// restores the member's position verbatim, so the panel returns exactly as it left instead of
/// re-solving in view.
///
/// <para>This is the sanctioned implementation of the module's <b>park, don't hide</b> premise:
/// game code writes <see cref="PanelGroupComponent.Active"/> (or <see cref="PanelGroupComponent.None"/>
/// for a closed menu) and nothing else — the parking dance is never hand-written.</para>
///
/// <para><b>Parked panels are inert.</b> Every <see cref="FocusableComponent"/> whose transform
/// chain reaches a parked member is <see cref="FocusableComponent.Disabled"/>, so Tab / arrow
/// navigation cannot walk into a panel the player cannot see; the active member's focusables are
/// re-enabled. A member's own focusable (if it has one) is gated too.</para>
///
/// <para><b>Ordering.</b> Run this AFTER every system that writes member positions — in particular
/// <c>AutoLayoutSystem</c>, which rewrites each root slot's position from scratch every frame — and
/// BEFORE <c>HierarchySystem</c>, so the park lands on the members' descendants in the same frame.
/// A member whose position is rewritten while parked is simply re-parked from the fresher value
/// (that value becomes its new home), which is what lets a layout-driven panel be a member without
/// the offset ever compounding.</para>
/// </summary>
public sealed class PanelGroupSystem : ISystem<GameState>
{
    /// Depth cap for the transform-parent walk that maps a focusable to its owning panel; a
    /// malformed (cyclic) hierarchy must not hang the frame.
    private const int MaxParentWalk = 64;

    private readonly EntitySet _groups;
    private readonly EntitySet _focusables;

    // Per-frame map: a member's transform → is that member active. Keyed by the transform (not the
    // entity) because that is exactly what the park moves — a focusable belongs to the panel its
    // TRANSFORM chain reaches, whether it was attached with SetParent or by transform alone.
    private readonly Dictionary<TransformComponent, bool> _memberTransforms = new();

    public bool IsEnabled { get; set; } = true;

    public PanelGroupSystem(World world)
    {
        _groups = world.GetEntities().With<PanelGroupComponent>().AsSet();
        _focusables = world.GetEntities().With<FocusableComponent>().With<TransformComponent>().AsSet();
    }

    public void Update(GameState state)
    {
        if (!IsEnabled) return;

        var groups = _groups.GetEntities();
        if (groups.Length == 0) return;

        _memberTransforms.Clear();

        foreach (var groupEntity in groups)
        {
            var group = groupEntity.Get<PanelGroupComponent>();
            var members = group.Members;
            for (var i = 0; i < members.Length; i++)
            {
                var member = members[i];
                if (!member.IsAlive || !member.Has<TransformComponent>()) continue;

                var active = i == group.Active;
                _memberTransforms[member.Get<TransformComponent>()] = active;

                if (active) Restore(member);
                else Park(member, group.ParkOffset);
            }
        }

        GateFocusables();
    }

    /// Moves a member to <c>home + offset</c> and remembers both. Already-parked members are left
    /// alone unless something moved them (a fresher home) or the offset itself changed — so the
    /// offset is applied exactly once no matter how many frames the panel stays parked.
    private static void Park(Entity member, Vector2 offset)
    {
        var transform = member.Get<TransformComponent>();

        if (member.Has<PanelParkedComponent>())
        {
            var stash = member.Get<PanelParkedComponent>();
            if (transform.Position == stash.Parked)
            {
                var wanted = stash.Home + offset;
                if (wanted == stash.Parked) return; // already parked exactly where it belongs
                Write(member, transform, stash.Home, wanted); // the group's park offset changed
                return;
            }
            // Position differs from what we wrote: another system (typically AutoLayoutSystem) owns
            // this panel's position and rewrote it. That value is the panel's real home now.
        }

        Write(member, transform, transform.Position, transform.Position + offset);
    }

    /// Puts an active member back at the position it held before parking. If something rewrote the
    /// position while it was parked, that writer is newer than our stash — keep its value.
    private static void Restore(Entity member)
    {
        if (!member.Has<PanelParkedComponent>()) return;

        var stash = member.Get<PanelParkedComponent>();
        var transform = member.Get<TransformComponent>();
        if (transform.Position == stash.Parked)
        {
            transform.Position = stash.Home;
            member.NotifyChanged<TransformComponent>();
        }

        member.Remove<PanelParkedComponent>();
    }

    private static void Write(Entity member, TransformComponent transform, Vector2 home, Vector2 parked)
    {
        transform.Position = parked;
        member.Set(new PanelParkedComponent { Home = home, Parked = parked });
        member.NotifyChanged<TransformComponent>();
    }

    /// Disables every focusable that lives under a parked member and enables the ones under the
    /// active member. Focusables outside any group are untouched.
    private void GateFocusables()
    {
        if (_memberTransforms.Count == 0) return;

        foreach (var entity in _focusables.GetEntities())
        {
            var transform = entity.Get<TransformComponent>();
            for (var depth = 0; transform != null && depth < MaxParentWalk; depth++)
            {
                if (_memberTransforms.TryGetValue(transform, out var active))
                {
                    entity.Get<FocusableComponent>().Disabled = !active;
                    break;
                }

                transform = transform.Parent;
            }
        }
    }

    public void Dispose()
    {
        _groups.Dispose();
        _focusables.Dispose();
        _memberTransforms.Clear();
    }
}
