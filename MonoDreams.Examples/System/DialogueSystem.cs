using DefaultEcs;
using DefaultEcs.System;
using MonoDreams.Examples.Message;
using MonoDreams.State;

namespace MonoDreams.Examples.System;

public class DialogueSystem : ISystem<GameState>
{
    private readonly World _world;

    public DialogueSystem(World world)
    {
        _world = world;
        _world.Subscribe(this);
    }

    public bool IsEnabled { get; set; } = true;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public void Update(GameState state)
    {
        // Update logic if needed
    }

    [Subscribe]
    public void On(in CollisionMessage message)
    {
        if (message.Type != CollisionType.Dialogue) return;
        StartDialogue(message);
    }

    private void StartDialogue(CollisionMessage message)
    {
        // Logic to start the dialogue
        // For example, display the dialogue box and text
        Console.WriteLine("Starting dialogue with entity: " + message.CollidingEntity);
    }
}