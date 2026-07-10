using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Dialogue;
using MonoDreams.Examples.Component;
using MonoDreams.Message;
using MonoDreams.State;

namespace MonoDreams.Examples.System.Dialogue;

/// <summary>
/// Publishes <see cref="DialogueStartMessage"/> when the player enters a zone with a
/// <see cref="DialogueZoneComponent"/> via a collision tagged as <see cref="CollisionType.Dialogue"/>.
/// Engine-side <see cref="MonoDreams.Dialogue.DialogueSystem"/> stays free of collision/zone concepts.
/// </summary>
public class ZoneDialogueTriggerSystem : ISystem<GameState>
{
    private readonly World _world;

    public bool IsEnabled { get; set; } = true;

    public ZoneDialogueTriggerSystem(World world)
    {
        _world = world;
        world.Subscribe(this);
    }

    [Subscribe]
    private void OnCollision(in CollisionMessage message)
    {
        if (message.Type != CollisionType.Dialogue) return;

        // Identity consumer: the zone's DialogueZoneComponent lives on the COLLIDER entity (ColliderB),
        // not the resolved body — read the collider side.
        var zoneEntity = message.ColliderB;
        if (!zoneEntity.Has<DialogueZoneComponent>()) return;

        var zone = zoneEntity.Get<DialogueZoneComponent>();
        if (!zone.AutoStart) return;
        if (zone.OneTimeOnly && zone.HasBeenTriggered) return;

        zone.HasBeenTriggered = true;
        _world.Publish(new DialogueStartMessage(zoneEntity, zone.YarnNodeName));
    }

    public void Update(GameState state) { }

    public void Dispose()
    {
        global::System.GC.SuppressFinalize(this);
    }
}
