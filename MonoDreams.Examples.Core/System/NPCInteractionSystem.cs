using System;
using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Dialogue;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Input;
using MonoDreams.Extensions.Monogame;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

public class NPCInteractionSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly EntitySet _playerSet;
    private readonly EntitySet _zoneSet;
    // Colliders-as-entities: an entity's collider may live on a ChildOf child, so we no longer
    // require ColliderTagComponent on the player/zone entity itself — the rect helper finds the
    // collider on the entity OR on a collider child.
    private readonly EntitySet _colliderChildSet;
    private bool _dialogueActive;

    public bool IsEnabled { get; set; } = true;

    public NPCInteractionSystem(World world)
    {
        _world = world;
        world.Subscribe(this);

        _playerSet = world.GetEntities()
            .With<PlayerState>()
            .With<TransformComponent>()
            .AsSet();

        _zoneSet = world.GetEntities()
            .With<DialogueZoneComponent>()
            .With<NPCInteractionIcon>()
            .With<TransformComponent>()
            .AsSet();

        _colliderChildSet = world.GetEntities()
            .With<ColliderTagComponent>()
            .With<TransformComponent>()
            .With<ChildOfComponent>()
            .AsSet();
    }

    [Subscribe]
    private void OnDialogueActive(in DialogueActiveMessage message)
    {
        _dialogueActive = message.IsActive;

        if (_dialogueActive)
        {
            // Hide all NPC icons while dialogue is active
            foreach (var zone in _zoneSet.GetEntities())
            {
                var icon = zone.Get<NPCInteractionIcon>();
                if (icon.IconEntity.IsAlive && icon.IconEntity.Has<VisibleComponent>())
                    icon.IconEntity.Remove<VisibleComponent>();
            }
        }
    }

    public void Update(GameState state)
    {
        if (_dialogueActive) return;

        var players = _playerSet.GetEntities();
        if (players.Length == 0) return;

        var playerEntity = players[0];
        if (!TryColliderWorldRect(playerEntity, out var playerRect)) return;

        var interactJustPressed = InputState.Interact.JustPressed();

        foreach (var zone in _zoneSet.GetEntities())
        {
            if (!TryColliderWorldRect(zone, out var zoneRect)) continue;

            var icon = zone.Get<NPCInteractionIcon>();
            var iconEntity = icon.IconEntity;
            if (!iconEntity.IsAlive) continue;

            if (playerRect.Intersects(zoneRect))
            {
                if (!iconEntity.Has<VisibleComponent>())
                    iconEntity.Set<VisibleComponent>();

                if (interactJustPressed)
                {
                    var dialogueZone = zone.Get<DialogueZoneComponent>();
                    if (dialogueZone.OneTimeOnly && dialogueZone.HasBeenTriggered)
                        continue;

                    dialogueZone.HasBeenTriggered = true;
                    InputState.Interact.Consume();
                    _world.Publish(new DialogueStartMessage(zone, dialogueZone.YarnNodeName));
                    break;
                }
            }
            else
            {
                if (iconEntity.Has<VisibleComponent>())
                    iconEntity.Remove<VisibleComponent>();
            }
        }
    }

    /// <summary>The entity's collider world rect — from its own collider, or, under the
    /// colliders-as-entities model, from its first collider CHILD entity. Returns false if the
    /// entity has no collider anywhere.</summary>
    private bool TryColliderWorldRect(Entity e, out CollisionRect rect)
    {
        if (e.Has<BoxColliderComponent>())
        {
            rect = SATCollision.BoxWorldRect(e.Get<BoxColliderComponent>(), e.Get<TransformComponent>());
            return true;
        }
        if (e.Has<ConvexColliderComponent>())
        {
            rect = e.Get<ConvexColliderComponent>().BroadPhaseAABB;
            return true;
        }

        foreach (var child in _colliderChildSet.GetEntities())
        {
            if (!child.IsAlive || child.Get<ChildOfComponent>().Parent != e) continue;
            if (child.Has<BoxColliderComponent>())
            {
                rect = SATCollision.BoxWorldRect(child.Get<BoxColliderComponent>(), child.Get<TransformComponent>());
                return true;
            }
            if (child.Has<ConvexColliderComponent>())
            {
                rect = child.Get<ConvexColliderComponent>().BroadPhaseAABB;
                return true;
            }
        }

        rect = default;
        return false;
    }

    public void Dispose()
    {
        _playerSet.Dispose();
        _zoneSet.Dispose();
        _colliderChildSet.Dispose();
        GC.SuppressFinalize(this);
    }
}
