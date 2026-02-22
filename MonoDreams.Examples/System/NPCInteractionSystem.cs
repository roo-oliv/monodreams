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
    private bool _inputConsumed;

    public bool IsEnabled { get; set; } = true;

    public NPCInteractionSystem(World world)
    {
        _world = world;
        world.Subscribe(this);

        _playerSet = world.GetEntities()
            .With<PlayerState>()
            .With<Transform>()
            .With<BoxCollider>()
            .AsSet();

        _zoneSet = world.GetEntities()
            .With<DialogueZoneComponent>()
            .With<NPCInteractionIcon>()
            .With<Transform>()
            .With<BoxCollider>()
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
        var playerCollider = playerEntity.Get<BoxCollider>();
        var playerRect = CollisionRect.FromBounds(playerCollider.Bounds, playerTransform.WorldPosition);

        // Edge-trigger: release consumed flag when interact is no longer held
        if (!InputState.Interact.Pressed(state))
            _inputConsumed = false;

        foreach (var zone in _zoneSet.GetEntities())
        {
            var zoneTransform = zone.Get<Transform>();
            var zoneCollider = zone.Get<BoxCollider>();
            var zoneRect = CollisionRect.FromBounds(zoneCollider.Bounds, zoneTransform.WorldPosition);

            var icon = zone.Get<NPCInteractionIcon>();
            var iconEntity = icon.IconEntity;
            if (!iconEntity.IsAlive) continue;

            if (playerRect.Intersects(zoneRect))
            {
                // Show icon
                if (!iconEntity.Has<Visible>())
                    iconEntity.Set<Visible>();

                // Check for interact press
                if (InputState.Interact.Pressed(state) && !_inputConsumed)
                {
                    _inputConsumed = true;

                    var dialogueZone = zone.Get<DialogueZoneComponent>();
                    if (dialogueZone.OneTimeOnly && dialogueZone.HasBeenTriggered)
                        continue;

                    dialogueZone.HasBeenTriggered = true;
                    _world.Publish(new DialogueStartMessage(zone, dialogueZone.YarnNodeName));
                }
            }
            else
            {
                // Hide icon
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
