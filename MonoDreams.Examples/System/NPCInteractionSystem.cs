using System;
using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Component;
using MonoDreams.Component.Collision;
using MonoDreams.Component.Draw;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Input;
using MonoDreams.Examples.Message;
using MonoDreams.Extensions.Monogame;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

public class NPCInteractionSystem : ISystem<GameState>
{
    private readonly World _world;
    private readonly EntitySet _playerSet;
    private readonly EntitySet _zoneSet;
    private bool _dialogueActive;

    public bool IsEnabled { get; set; } = true;

    public NPCInteractionSystem(World world)
    {
        _world = world;
        world.Subscribe(this);

        _playerSet = world.GetEntities()
            .With<PlayerState>()
            .With<Transform>()
            .With<ColliderTag>()
            .AsSet();

        _zoneSet = world.GetEntities()
            .With<DialogueZoneComponent>()
            .With<NPCInteractionIcon>()
            .With<Transform>()
            .With<ColliderTag>()
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
                if (icon.IconEntity.IsAlive && icon.IconEntity.Has<Visible>())
                    icon.IconEntity.Remove<Visible>();
            }
        }
    }

    public void Update(GameState state)
    {
        if (_dialogueActive) return;

        var players = _playerSet.GetEntities();
        if (players.Length == 0) return;

        var playerEntity = players[0];
        var playerTransform = playerEntity.Get<Transform>();
        var playerRect = playerEntity.Has<BoxCollider>()
            ? CollisionRect.FromBounds(playerEntity.Get<BoxCollider>().Bounds, playerTransform.WorldPosition)
            : playerEntity.Get<ConvexCollider>().BroadPhaseAABB;

        var interactJustPressed = InputState.Interact.JustPressed();

        foreach (var zone in _zoneSet.GetEntities())
        {
            var zoneTransform = zone.Get<Transform>();
            var zoneRect = zone.Has<BoxCollider>()
                ? CollisionRect.FromBounds(zone.Get<BoxCollider>().Bounds, zoneTransform.WorldPosition)
                : zone.Get<ConvexCollider>().BroadPhaseAABB;

            var icon = zone.Get<NPCInteractionIcon>();
            var iconEntity = icon.IconEntity;
            if (!iconEntity.IsAlive) continue;

            if (playerRect.Intersects(zoneRect))
            {
                if (!iconEntity.Has<Visible>())
                    iconEntity.Set<Visible>();

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
                if (iconEntity.Has<Visible>())
                    iconEntity.Remove<Visible>();
            }
        }
    }

    public void Dispose()
    {
        _playerSet.Dispose();
        _zoneSet.Dispose();
        GC.SuppressFinalize(this);
    }
}
