using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Examples.Component;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

public class DialogueContentSystem : AEntitySetSystem<GameState>
{
    private World _world;
    
    public DialogueContentSystem(World world) 
        : base(world.GetEntities().With<DialogueComponent>().AsSet())
    {
        _world = world;
    }
    
    protected override void Update(GameState state, in Entity entity)
    {
        // Only process entities that have content but haven't been initialized
        var content = entity.Get<DialogueComponent>();
        
        // If the entity also has a state component, initialize from content
        // if (entity.Has<DialogueState>())
        // {
        //     // Initialize dialogue state from content if needed
        // }
    }
}
