using DefaultEcs;
using DefaultEcs.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using MonoDreams.Component;
using MonoDreams.Examples.Component;
using MonoDreams.Examples.Message;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

public class DialogueTriggerSystem : AEntitySetSystem<GameState>
{
    private World _world;
    private readonly Entity _player;
    
    public DialogueTriggerSystem(World world, Entity player)
        : base(world.GetEntities().With<DialogueTrigger>().With<Position>().AsSet())
    {
        _world = world;
        _player = player;
    }
    
    protected override void Update(GameState state, in Entity entity)
    {
        var trigger = entity.Get<DialogueTrigger>();
        
        // Skip if the trigger is one-time-only and already used
        if (trigger.OneTimeOnly && trigger.HasBeenUsed) return;
        
        // Check if trigger conditions are met
        bool shouldTrigger = false;
        
        switch (trigger.Type)
        {
            case TriggerType.Proximity:
                // Check if player is within proximity radius
                if (entity.Has<Position>() && _player.Has<Position>())
                {
                    var triggerPos = entity.Get<Position>().Current;
                    var playerPos = _player.Get<Position>().Current;
                    
                    float distance = Vector2.Distance(triggerPos, playerPos);
                    shouldTrigger = distance <= trigger.ProximityRadius;
                }
                break;
                
            case TriggerType.Interaction:
                // Check if player is close and interaction key is pressed
                if (entity.Has<Position>() && _player.Has<Position>())
                {
                    var triggerPos = entity.Get<Position>().Current;
                    var playerPos = _player.Get<Position>().Current;
                    
                    float distance = Vector2.Distance(triggerPos, playerPos);
                    shouldTrigger = distance <= trigger.ProximityRadius && 
                                    Keyboard.GetState().IsKeyDown(trigger.InteractionKey);
                }
                break;
                
            case TriggerType.Automatic:
                // Always trigger
                shouldTrigger = true;
                break;
        }
        
        if (shouldTrigger)
        {
            // Mark trigger as used if one-time-only
            if (trigger.OneTimeOnly)
            {
                trigger.HasBeenUsed = true;
            }
            
            // Create or get dialogue entity
            Entity dialogueEntity;
            if (entity.Has<DialogueComponent>())
            {
                dialogueEntity = entity;
            }
            else
            {
                // Create a new dialogue entity
                dialogueEntity = _world.CreateEntity();
                // dialogueEntity.Set(new DialogueComponent());
                // dialogueEntity.Set(new DialogueState());
            }
            
            // Publish start message
            _world.Publish(new DialogueStartMessage(
                dialogueEntity,
                trigger.StartNode
            ));
        }
    }
}
